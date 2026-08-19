"""Gameplay water depth bands.

Authoring aid only. Unity keeps its own pressure/depth zone authoring; this shows the
same thresholds while the geometry is being modelled so cave depth is visible in place.
"""

import bpy
import gpu
from bpy.props import BoolProperty, FloatProperty, FloatVectorProperty, PointerProperty
from bpy.types import Operator, Panel, PropertyGroup
from gpu_extras.batch import batch_for_shader

from . import contract, localization

_handle = None

BAND_COLORS = (
    (0.35, 0.85, 1.00, 0.85),
    (0.20, 0.70, 1.00, 0.70),
    (0.12, 0.45, 0.95, 0.60),
    (0.06, 0.22, 0.70, 0.55),
)


class WBWaterSettings(PropertyGroup):
    sea_level: FloatProperty(name="Sea Level", default=0.0, unit="LENGTH")
    shallow_depth: FloatProperty(name="Shallow", default=20.0, min=0.0, unit="LENGTH")
    mid_depth: FloatProperty(name="Mid", default=60.0, min=0.0, unit="LENGTH")
    deep_depth: FloatProperty(name="Deep", default=120.0, min=0.0, unit="LENGTH")
    show_bands: BoolProperty(name="Show Depth Bands", default=False)
    surface_color: FloatVectorProperty(name="Surface", subtype="COLOR", size=4,
                                       default=(0.35, 0.85, 1.0, 0.85), min=0.0, max=1.0)


def settings(scene):
    return getattr(scene, "worldbuilder_water", None)


def band_index(scene, z) -> int:
    value = settings(scene)
    if value is None:
        return 0
    return contract.depth_band_index(value.sea_level, z, value.shallow_depth,
                                     value.mid_depth, value.deep_depth)


def band_name(scene, z) -> str:
    return contract.depth_band_name(band_index(scene, z))


def depth_at(scene, z) -> float:
    value = settings(scene)
    return contract.depth_below(value.sea_level, z) if value else 0.0


def _extent(scene):
    grid = scene.worldbuilder_chunks
    radius = max(1, grid.grid_radius)
    span = grid.chunk_size * radius
    focus = scene.cursor.location
    return focus.x - span, focus.y - span, focus.x + span, focus.y + span


def _draw():
    scene = getattr(bpy.context, "scene", None)
    value = settings(scene) if scene else None
    if value is None or not value.show_bands or not hasattr(scene, "worldbuilder_chunks"):
        return
    minimum_x, minimum_y, maximum_x, maximum_y = _extent(scene)
    boundaries = contract.depth_band_boundaries(value.sea_level, value.shallow_depth,
                                                value.mid_depth, value.deep_depth)
    shader = gpu.shader.from_builtin("UNIFORM_COLOR")
    gpu.state.blend_set("ALPHA")
    try:
        for index, z in enumerate(boundaries):
            vertices = [
                (minimum_x, minimum_y, z), (maximum_x, minimum_y, z),
                (maximum_x, minimum_y, z), (maximum_x, maximum_y, z),
                (maximum_x, maximum_y, z), (minimum_x, maximum_y, z),
                (minimum_x, maximum_y, z), (minimum_x, minimum_y, z),
            ]
            batch = batch_for_shader(shader, "LINES", {"pos": vertices})
            shader.bind()
            shader.uniform_float("color", BAND_COLORS[index])
            batch.draw(shader)
    finally:
        gpu.state.blend_set("NONE")


class WB_OT_water_sync_vertex_bake(Operator):
    bl_idname = "worldbuilder.water_sync_vertex_bake"
    bl_label = "Push Sea Level to Vertex Bake"

    def execute(self, context):
        bake = getattr(context.scene, "worldbuilder_vertex_bake", None)
        value = settings(context.scene)
        if bake is None or value is None:
            return {"CANCELLED"}
        bake.sea_level = value.sea_level
        self.report({"INFO"}, f"Vertex bake sea level set to {value.sea_level:g}")
        return {"FINISHED"}


class WB_OT_water_cursor_to_band(Operator):
    bl_idname = "worldbuilder.water_cursor_to_band"
    bl_label = "Cursor to Band Floor"
    bl_options = {"REGISTER", "UNDO"}

    band: bpy.props.IntProperty(default=1, min=0, max=3)

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        boundaries = contract.depth_band_boundaries(value.sea_level, value.shallow_depth,
                                                    value.mid_depth, value.deep_depth)
        context.scene.cursor.location.z = boundaries[self.band]
        self.report({"INFO"}, f"{contract.depth_band_name(self.band)} floor")
        return {"FINISHED"}


class WB_PT_water(Panel):
    bl_label = "Depth Bands"
    bl_idname = "WB_PT_water_depth"
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
        translate = lambda key: localization.tr(key, context.scene)
        column = layout.column(align=True)
        column.prop(value, "sea_level", text=translate("sea_level"))
        row = column.row(align=True)
        row.prop(value, "shallow_depth", text=translate("band_shallow"))
        row.prop(value, "mid_depth", text=translate("band_mid"))
        row.prop(value, "deep_depth", text=translate("band_deep"))
        layout.prop(value, "show_bands", text=translate("show_bands"))

        cursor_z = context.scene.cursor.location.z
        box = layout.box()
        box.label(text=f"{translate('cursor_depth')}: {depth_at(context.scene, cursor_z):.1f} m "
                       f"({band_name(context.scene, cursor_z)})", icon="MOD_FLUIDSIM")
        obj = context.object
        if obj is not None:
            z = obj.matrix_world.translation.z
            box.label(text=f"{obj.name}: {depth_at(context.scene, z):.1f} m ({band_name(context.scene, z)})")

        row = layout.row(align=True)
        for index in range(4):
            row.operator("worldbuilder.water_cursor_to_band",
                         text=contract.depth_band_name(index)).band = index
        layout.operator("worldbuilder.water_sync_vertex_bake", icon="EXPORT")


CLASSES = (WBWaterSettings, WB_OT_water_sync_vertex_bake, WB_OT_water_cursor_to_band, WB_PT_water)


def register():
    global _handle
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_water = PointerProperty(type=WBWaterSettings)
    if _handle is None:
        _handle = bpy.types.SpaceView3D.draw_handler_add(_draw, (), "WINDOW", "POST_VIEW")


def unregister():
    global _handle
    if _handle is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_handle, "WINDOW")
        _handle = None
    if hasattr(bpy.types.Scene, "worldbuilder_water"):
        del bpy.types.Scene.worldbuilder_water
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
