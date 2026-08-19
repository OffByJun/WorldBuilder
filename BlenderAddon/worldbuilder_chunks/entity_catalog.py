"""Mirror of the Unity entity prefab catalog.

Blender only reads this file. Unity's WorldEntityRuntimeAuthoring stays the single
source of truth for prefab ids; loading the catalog here turns a hand-typed integer
into a validated pick so a typo cannot reach the importer.
"""

import json
import os

import bpy
from bpy.props import (
    BoolProperty,
    CollectionProperty,
    EnumProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup

from . import contract, localization

SCHEMA_VERSION = 1
UNSET = "NONE"

_items_cache = [(UNSET, "<no catalog loaded>", "")]


class WBEntityCatalogItem(PropertyGroup):
    prefab_id: IntProperty(name="Prefab ID")
    display_name: StringProperty(name="Name")
    kind: StringProperty(name="Kind", default="Generic")
    persistent: BoolProperty(name="Persistent")
    region_streamed: BoolProperty(name="Region Streamed", default=True)
    replicated: BoolProperty(name="Replicated")
    lifetime: FloatProperty(name="Lifetime", min=0.0)


class WBEntityCatalogSettings(PropertyGroup):
    catalog_file: StringProperty(name="Entity Catalog", subtype="FILE_PATH")
    items: CollectionProperty(type=WBEntityCatalogItem)
    status: StringProperty(default="No catalog loaded")


def settings(scene):
    return getattr(scene, "worldbuilder_entity_catalog", None)


def rebuild_items(scene) -> None:
    global _items_cache
    value = settings(scene)
    entries = [
        (
            str(item.prefab_id),
            f"{item.prefab_id}  {item.display_name or 'Unnamed'}",
            item.kind,
        )
        for item in (value.items if value else [])
    ]
    _items_cache = entries or [(UNSET, "<no catalog loaded>", "")]


def enum_items(self, context):
    return _items_cache


def load(scene) -> int:
    value = settings(scene)
    path = bpy.path.abspath(value.catalog_file) if value and value.catalog_file else ""
    if not path or not os.path.isfile(path):
        raise ValueError("Choose an entity catalog JSON exported from Unity")
    with open(path, "r", encoding="utf-8") as stream:
        payload = json.load(stream)
    if int(payload.get("schemaVersion", 0)) != SCHEMA_VERSION:
        raise ValueError("Unsupported entity catalog schema version")
    records = payload.get("entities")
    if not isinstance(records, list):
        raise ValueError("Entity catalog must contain an entities array")

    value.items.clear()
    seen = set()
    for record in sorted(records, key=lambda entry: int(entry.get("prefabId", 0))):
        prefab_id = int(record.get("prefabId", 0))
        if prefab_id in seen:
            raise ValueError(f"Duplicate prefab id {prefab_id} in catalog")
        seen.add(prefab_id)
        item = value.items.add()
        item.prefab_id = prefab_id
        item.display_name = str(record.get("name", ""))
        kind = str(record.get("kind", "Generic"))
        item.kind = kind if kind in contract.ENTITY_KINDS else "Generic"
        flags = set(record.get("flags") or ())
        item.persistent = "Persistent" in flags
        item.region_streamed = "RegionStreamed" in flags
        item.replicated = "Replicated" in flags
        item.lifetime = max(0.0, float(record.get("lifetimeSeconds", 0.0)))
    value.status = f"{len(value.items)} entities"
    rebuild_items(scene)
    return len(value.items)


def find(scene, prefab_id):
    value = settings(scene)
    if value is None:
        return None
    return next((item for item in value.items if item.prefab_id == int(prefab_id)), None)


def is_loaded(scene) -> bool:
    value = settings(scene)
    return value is not None and len(value.items) > 0


def is_known(scene, prefab_id) -> bool:
    """Unknown ids only matter once a catalog is loaded, so validation stays opt-in."""
    return not is_loaded(scene) or find(scene, prefab_id) is not None


def apply_pick(properties, scene, identifier, prefix="entity_") -> bool:
    """Mirror every catalog field, not just the id.

    The catalog reflects what WorldEntityAuthoring actually carries in Unity, so copying
    kind, flags, and lifetime keeps Blender from exporting a silently divergent entity
    block. Anything the artist changes afterwards is treated as a deliberate override.
    """
    if identifier == UNSET:
        return False
    item = find(scene, int(identifier))
    if item is None:
        return False
    setattr(properties, prefix + "prefab_id", item.prefab_id)
    setattr(properties, prefix + "kind", item.kind if item.kind in contract.ENTITY_KINDS else "Generic")
    setattr(properties, prefix + "persistent", item.persistent)
    setattr(properties, prefix + "region_streamed", item.region_streamed)
    setattr(properties, prefix + "replicated", item.replicated)
    setattr(properties, prefix + "lifetime", item.lifetime)
    return True


def draw_picker(layout, scene, properties, pick_attribute, id_attribute="entity_prefab_id") -> None:
    row = layout.row(align=True)
    row.prop(properties, pick_attribute, text="")
    row.operator("worldbuilder.entity_catalog_reload", text="", icon="FILE_REFRESH")
    if not is_loaded(scene):
        layout.label(text="Load a Unity entity catalog to pick by name", icon="INFO")
    elif not is_known(scene, getattr(properties, id_attribute)):
        layout.label(text=f"Prefab id {getattr(properties, id_attribute)} is not in the catalog", icon="ERROR")


class WB_OT_entity_catalog_reload(Operator):
    bl_idname = "worldbuilder.entity_catalog_reload"
    bl_label = "Reload Entity Catalog"

    def execute(self, context):
        try:
            count = load(context.scene)
        except (OSError, ValueError) as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, f"Loaded {count} catalog entities")
        return {"FINISHED"}


class WB_PT_entity_catalog(Panel):
    bl_label = "Entity Catalog"
    bl_idname = "WB_PT_entity_catalog"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        value = settings(context.scene)
        if value is None:
            return
        row = layout.row(align=True)
        row.prop(value, "catalog_file", text=localization.tr("entity_catalog_file", context.scene))
        row.operator("worldbuilder.entity_catalog_reload", text="", icon="FILE_REFRESH")
        layout.label(text=value.status)
        layout.label(text="Exported from Unity: WorldBuilder > World > Entity Catalog", icon="INFO")


CLASSES = (WBEntityCatalogItem, WBEntityCatalogSettings, WB_OT_entity_catalog_reload, WB_PT_entity_catalog)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_entity_catalog = PointerProperty(type=WBEntityCatalogSettings)


def unregister():
    if hasattr(bpy.types.Scene, "worldbuilder_entity_catalog"):
        del bpy.types.Scene.worldbuilder_entity_catalog
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
