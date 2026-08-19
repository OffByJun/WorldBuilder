from __future__ import annotations

import hashlib
import json
import os
import struct
import tempfile
import uuid
from collections import defaultdict
from dataclasses import dataclass

import bpy
from mathutils import Matrix, Vector

from . import contract, entity_catalog, profile, state

CACHE_FILE = ".worldbuilder-export-cache.json"
GEOMETRY_ROLES = {"GEOMETRY", "COLLISION"}
PLACEMENT_ROLES = {"INSTANCE", "MARKER", "ENTITY"}
ASSET_ROLES = {"INSTANCE", "ENTITY"}


@dataclass(frozen=True)
class OwnershipInfo:
    coordinate: tuple[int, int]
    source: str  # OVERRIDE, COLLECTION, PIVOT


@dataclass(frozen=True)
class BoundsStatus:
    role: str
    owner: tuple[int, int]
    ownership_source: str
    bounds: tuple[Vector, Vector] | None
    crosses_chunk: bool
    allow_cross_chunk: bool


def object_role(obj) -> str:
    # Scatter previews are authoring-only even though committed instances use INSTANCE.
    if obj.get("wb_scatter_preview", False):
        return "GLOBAL"
    properties = getattr(obj, "worldbuilder_chunk", None)
    explicit = str(
        properties.role if properties is not None else obj.get("wb_role", "AUTO")
    ).upper()
    if explicit != "AUTO":
        return explicit
    if obj.type not in {"MESH", "CURVE", "SURFACE", "META", "FONT", "EMPTY"}:
        return "GLOBAL"
    name = obj.name.upper()
    if name.startswith("COL_"):
        return "COLLISION"
    if name.startswith("ENT_"):
        return "ENTITY"
    if name.startswith("INST_"):
        return "INSTANCE"
    if name.startswith(("MARKER_", "SPAWN_", "SOCKET_", "GUIDE_")):
        return "MARKER"
    return "MARKER" if obj.type == "EMPTY" else "GEOMETRY"


def ensure_stable_id(obj) -> str:
    properties = getattr(obj, "worldbuilder_chunk", None)
    value = str(
        properties.stable_id if properties is not None else obj.get("wb_id", "")
    ).strip()
    if not value:
        value = uuid.uuid4().hex
        if properties is not None:
            properties.stable_id = value
        else:
            obj["wb_id"] = value
    return value


def stable_id(obj) -> str:
    """Read an object's stable ID without mutating Blender data-blocks."""
    properties = getattr(obj, "worldbuilder_chunk", None)
    return str(
        properties.stable_id if properties is not None else obj.get("wb_id", "")
    ).strip()


def readonly_sort_key(obj) -> tuple[str, str]:
    # Object names are unique within a Blend file and give missing IDs a deterministic order.
    return stable_id(obj), obj.name


def chunk_collection_map(scene) -> dict[object, tuple[int, int]]:
    result: dict[object, tuple[int, int]] = {}

    def visit(collection, inherited=None):
        coordinate = ((int(collection["wb_chunk_x"]), int(collection["wb_chunk_z"]))
                      if "wb_chunk_x" in collection and "wb_chunk_z" in collection
                      else contract.parse_chunk_name(collection.name) or inherited)
        if coordinate is not None:
            for obj in collection.objects:
                result[obj] = coordinate
        for child in collection.children:
            visit(child, coordinate)

    visit(scene.collection)
    return result


def object_ownership(obj, settings, collection_map=None) -> OwnershipInfo:
    """Shared ownership resolution used by export, validation, and viewport overlay."""
    properties = getattr(obj, "worldbuilder_chunk", None)
    if properties is not None and properties.override_chunk:
        return OwnershipInfo((properties.chunk_x, properties.chunk_z), "OVERRIDE")
    if "wb_chunk_x" in obj and "wb_chunk_z" in obj:
        return OwnershipInfo((int(obj["wb_chunk_x"]), int(obj["wb_chunk_z"])), "OVERRIDE")
    if collection_map is not None and obj in collection_map:
        return OwnershipInfo(collection_map[obj], "COLLECTION")
    location = obj.matrix_world.translation
    coordinate = contract.chunk_coord_from_xy(
        location.x,
        location.y,
        settings.origin_x,
        settings.origin_y,
        settings.chunk_size,
    )
    return OwnershipInfo(coordinate, "PIVOT")


def object_chunk(obj, settings, collection_map=None) -> tuple[int, int]:
    return object_ownership(obj, settings, collection_map).coordinate


def object_layer(obj, settings) -> int:
    """Resolve the authoring layer that owns an object.

    Layers never affect chunk ownership or runtime streaming; they exist so vertical
    authoring stays navigable and so Unity can filter placements by band.
    """
    properties = getattr(obj, "worldbuilder_chunk", None)
    count = max(1, int(getattr(settings, "layer_count", 1)))
    if properties is not None and properties.override_layer:
        return contract.clamp_layer(properties.layer_index, count)
    index = contract.layer_index_for_z(
        obj.matrix_world.translation.z,
        getattr(settings, "layer_base_z", 0.0),
        getattr(settings, "layer_height", 16.0),
    )
    return contract.clamp_layer(index, count)


def entity_payload(obj) -> dict[str, object] | None:
    properties = getattr(obj, "worldbuilder_chunk", None)
    if properties is None:
        return None
    kind = str(properties.entity_kind)
    return {
        "prefabId": int(properties.entity_prefab_id),
        "kind": kind if kind in contract.ENTITY_KINDS else "Generic",
        "flags": contract.entity_flag_names(
            bool(properties.entity_persistent),
            bool(properties.entity_region_streamed),
            bool(properties.entity_replicated),
        ),
        "lifetimeSeconds": contract.normalized_float(properties.entity_lifetime),
    }


def collect_chunks(scene, settings) -> dict[tuple[int, int], list[object]]:
    collection_map = chunk_collection_map(scene)
    chunks: defaultdict[tuple[int, int], list[object]] = defaultdict(list)
    for obj in scene.objects:
        role = object_role(obj)
        if role == "GLOBAL" or obj.hide_render:
            continue
        if role in GEOMETRY_ROLES and obj.type not in {
            "MESH",
            "CURVE",
            "SURFACE",
            "META",
            "FONT",
        }:
            continue
        chunks[object_chunk(obj, settings, collection_map)].append(obj)
    for objects in chunks.values():
        # This function is also called by Panel.draw(), where Blender forbids ID writes.
        objects.sort(key=readonly_sort_key)
    return dict(sorted(chunks.items()))


def objects_in_chunk(scene, settings, coordinate: tuple[int, int]) -> list[object]:
    return collect_chunks(scene, settings).get(coordinate, [])


def world_bounds(obj) -> tuple[Vector, Vector] | None:
    """Return a world-space AABB without evaluating the object's mesh."""
    if not getattr(obj, "bound_box", None):
        location = obj.matrix_world.translation.copy()
        return location, location
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector(
        (
            min(point.x for point in points),
            min(point.y for point in points),
            min(point.z for point in points),
        )
    )
    maximum = Vector(
        (
            max(point.x for point in points),
            max(point.y for point in points),
            max(point.z for point in points),
        )
    )
    return minimum, maximum


def object_bounds_status(obj, settings, collection_map=None) -> BoundsStatus:
    role = object_role(obj)
    ownership = object_ownership(obj, settings, collection_map)
    properties = getattr(obj, "worldbuilder_chunk", None)
    allow_cross = bool(properties.allow_cross_chunk) if properties is not None else bool(
        obj.get("wb_allow_cross_chunk", False)
    )
    bounds = world_bounds(obj) if role in GEOMETRY_ROLES else None
    crossing = False
    if bounds is not None:
        minimum, maximum = bounds
        crossing = contract.bounds_cross_chunk_xy(
            minimum.x,
            minimum.y,
            maximum.x,
            maximum.y,
            ownership.coordinate,
            settings.origin_x,
            settings.origin_y,
            settings.chunk_size,
        )
    return BoundsStatus(
        role=role,
        owner=ownership.coordinate,
        ownership_source=ownership.source,
        bounds=bounds,
        crosses_chunk=crossing,
        allow_cross_chunk=allow_cross,
    )


def validate_scene(scene, settings, only_objects=None) -> list[tuple[str, str, object | None]]:
    issues: list[tuple[str, str, object | None]] = []
    units = scene.unit_settings
    if units.system != "METRIC" or abs(units.scale_length - 1.0) > 1e-6:
        issues.append(("ERROR", "Scene units must be Metric with Unit Scale 1.0.", None))
    if settings.chunk_size <= 0.0 or settings.chunks_per_region <= 0:
        issues.append(("ERROR", "Chunk size and chunks-per-region must be positive.", None))
        return issues

    collection_map = chunk_collection_map(scene)
    seen_ids: dict[str, object] = {}
    objects = list(only_objects) if only_objects is not None else list(scene.objects)
    for obj in objects:
        role = object_role(obj)
        if role == "GLOBAL" or obj.hide_render:
            continue
        stable_id = ensure_stable_id(obj)
        if stable_id in seen_ids:
            issues.append(
                ("ERROR", f"Duplicate wb_id also used by '{seen_ids[stable_id].name}'.", obj)
            )
        else:
            seen_ids[stable_id] = obj

        object_properties = getattr(obj, "worldbuilder_chunk", None)
        asset_id = (
            object_properties.asset_id
            if object_properties is not None
            else obj.get("wb_asset_id", "")
        )
        if role in ASSET_ROLES and not str(asset_id).strip():
            issues.append(("ERROR", f"{role} requires wb_asset_id.", obj))
        if role == "ENTITY" and object_properties is None:
            issues.append(("ERROR", "ENTITY requires the WorldBuilder object properties.", obj))
        elif role == "ENTITY" and object_properties.entity_prefab_id < 0:
            issues.append(("ERROR", "ENTITY requires a non-negative entity prefab id.", obj))
        elif role == "ENTITY" and not entity_catalog.is_known(scene, object_properties.entity_prefab_id):
            issues.append(
                (
                    "WARNING",
                    f"Entity prefab id {object_properties.entity_prefab_id} is not in the loaded Unity catalog.",
                    obj,
                )
            )

        status = object_bounds_status(obj, settings, collection_map)
        if role in GEOMETRY_ROLES:
            if obj.type not in {"MESH", "CURVE", "SURFACE", "META", "FONT"}:
                issues.append(("ERROR", f"{role} must be exportable geometry, got {obj.type}.", obj))
                continue
            if any(abs(component - 1.0) > 1e-4 for component in obj.scale):
                issues.append(("WARNING", "Apply scale before export for stable normals and bounds.", obj))
            if status.crosses_chunk:
                if status.allow_cross_chunk:
                    issues.append(
                        (
                            "WARNING",
                            "Geometry crosses its owning chunk with Allow Cross Chunk enabled.",
                            obj,
                        )
                    )
                else:
                    issues.append(
                        (
                            "ERROR",
                            "Geometry crosses its owning chunk. Split it or enable Allow Cross Chunk intentionally.",
                            obj,
                        )
                    )
    return issues


def authoring_hash(objects, coordinate, settings, depsgraph) -> str:
    digest = hashlib.sha256()
    digest.update(
        contract.canonical_json(
            {
                "version": contract.MANIFEST_VERSION,
                "worldId": contract.safe_world_id(settings.world_id),
                "chunk": coordinate,
                "chunkSize": contract.normalized_float(settings.chunk_size),
                "origin": [
                    contract.normalized_float(settings.origin_x),
                    contract.normalized_float(settings.origin_y),
                ],
            },
            False,
        ).encode("utf-8")
    )
    for obj in sorted(objects, key=lambda value: (ensure_stable_id(value), value.name)):
        role = object_role(obj)
        properties = getattr(obj, "worldbuilder_chunk", None)
        custom = {
            str(key): str(obj[key])
            for key in sorted(obj.keys())
            if str(key).startswith("wb_")
        }
        if properties is not None:
            custom.update(
                {
                    "role": properties.role,
                    "asset_id": properties.asset_id,
                    "marker_type": properties.marker_type,
                    "allow_cross_chunk": str(properties.allow_cross_chunk),
                    "override_chunk": str(properties.override_chunk),
                    "chunk_x": str(properties.chunk_x),
                    "chunk_z": str(properties.chunk_z),
                    "layer": str(object_layer(obj, settings)),
                    "entity_prefab_id": str(properties.entity_prefab_id),
                    "entity_kind": str(properties.entity_kind),
                    "entity_persistent": str(properties.entity_persistent),
                    "entity_region_streamed": str(properties.entity_region_streamed),
                    "entity_replicated": str(properties.entity_replicated),
                    "entity_lifetime": str(
                        contract.normalized_float(properties.entity_lifetime)
                    ),
                }
            )
        matrix = [
            [obj.matrix_world[row][column] for column in range(4)] for row in range(4)
        ]
        digest.update(
            contract.canonical_json(
                {
                    "id": ensure_stable_id(obj),
                    "name": obj.name,
                    "role": role,
                    "matrix": contract.normalized_matrix(matrix),
                    "custom": custom,
                },
                False,
            ).encode("utf-8")
        )
        if role not in GEOMETRY_ROLES:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = None
        try:
            mesh = evaluated.to_mesh()
            if mesh is None:
                continue
            digest.update(struct.pack("<II", len(mesh.vertices), len(mesh.polygons)))
            for vertex in mesh.vertices:
                digest.update(
                    struct.pack(
                        "<fff", *[round(float(value), 7) for value in vertex.co]
                    )
                )
            for polygon in mesh.polygons:
                digest.update(struct.pack("<I", len(polygon.vertices)))
                for index in polygon.vertices:
                    digest.update(struct.pack("<I", index))
            for material in mesh.materials:
                digest.update((material.name if material else "").encode("utf-8") + b"\0")
        finally:
            if mesh is not None:
                evaluated.to_mesh_clear()
    return digest.hexdigest()


def export_chunk(context, settings, coordinate, objects, force=False) -> tuple[str, str]:
    profile_path = bpy.path.abspath(settings.profile_path) if settings.profile_path else ""
    allowed, message = profile.export_allowed(settings, profile_path or None)
    if not allowed:
        raise RuntimeError(message)
    root = bpy.path.abspath(settings.export_root)
    if not root:
        raise RuntimeError("Export Root is empty")
    world_id = contract.safe_world_id(settings.world_id)
    chunk_folder = os.path.join(root, world_id, contract.chunk_name(coordinate))
    os.makedirs(chunk_folder, exist_ok=True)
    cache_path = os.path.join(root, world_id, CACHE_FILE)
    cache = _load_cache(cache_path)
    depsgraph = context.evaluated_depsgraph_get()
    source_hash = authoring_hash(objects, coordinate, settings, depsgraph)
    cache_key = f"{coordinate[0]},{coordinate[1]}"
    manifest_path = os.path.join(
        chunk_folder, contract.chunk_name(coordinate) + ".chunk.json"
    )
    if not force and cache.get(cache_key) == source_hash and os.path.isfile(manifest_path):
        return "SKIPPED", manifest_path

    origin_x = settings.origin_x + coordinate[0] * settings.chunk_size
    origin_y = settings.origin_y + coordinate[1] * settings.chunk_size
    geometry = [obj for obj in objects if object_role(obj) == "GEOMETRY"]
    collision = [obj for obj in objects if object_role(obj) == "COLLISION"]
    placements = [obj for obj in objects if object_role(obj) in PLACEMENT_ROLES]
    geometry_path = os.path.join(chunk_folder, "geometry.fbx")
    collision_path = os.path.join(chunk_folder, "collision.fbx")
    placements_path = os.path.join(chunk_folder, "placements.json")
    if geometry:
        _export_fbx(context, geometry, geometry_path, origin_x, origin_y)
    if collision:
        _export_fbx(context, collision, collision_path, origin_x, origin_y)
    if placements:
        _write_placements(
            placements_path,
            world_id,
            coordinate,
            placements,
            origin_x,
            origin_y,
            settings,
        )

    geometry_ref = (
        contract.file_reference(geometry_path, chunk_folder) if geometry else _empty_file()
    )
    collision_ref = (
        contract.file_reference(collision_path, chunk_folder) if collision else _empty_file()
    )
    placements_ref = (
        contract.file_reference(placements_path, chunk_folder)
        if placements
        else _empty_file()
    )
    bounds = _local_unity_bounds(geometry + collision + placements, origin_x, origin_y)
    region = contract.region_coord(
        coordinate[0], coordinate[1], settings.chunks_per_region
    )
    manifest = {
        "version": contract.MANIFEST_VERSION,
        "worldId": world_id,
        "chunk": {"x": coordinate[0], "z": coordinate[1]},
        "region": {"x": region[0], "z": region[1]},
        "chunkSize": contract.normalized_float(settings.chunk_size),
        "localOrigin": {"x": 0.0, "y": 0.0, "z": 0.0},
        "coordinateSystem": {
            "unit": "meter",
            "unitsPerMeter": 1.0,
            "blenderUpAxis": "Z",
            "blenderForwardAxis": "Y",
            "unityUpAxis": "Y",
            "unityForwardAxis": "Z",
            "vectorMapping": "XZY",
        },
        "exporter": {
            "name": "WorldBuilder Blender Add-on",
            "version": contract.ADDON_VERSION,
        },
        "source": {
            "blendFile": os.path.basename(bpy.data.filepath),
            "collection": contract.chunk_name(coordinate),
            "authoringHash": source_hash,
        },
        "contentHash": contract.content_hash(
            source_hash, [geometry_ref, collision_ref, placements_ref]
        ),
        "content": {
            "geometry": geometry_ref,
            "collision": collision_ref,
            "placements": placements_ref,
            "geometryObjectCount": len(geometry),
            "collisionObjectCount": len(collision),
            "placementCount": len(placements),
            "localBounds": bounds,
        },
    }
    _atomic_write(manifest_path, contract.canonical_json(manifest))
    cache[cache_key] = source_hash
    _atomic_write(cache_path, contract.canonical_json(dict(sorted(cache.items()))))
    return "EXPORTED", manifest_path


def _profile_issue(settings) -> tuple[str, str, object | None] | None:
    profile_path = bpy.path.abspath(settings.profile_path) if settings.profile_path else ""
    allowed, message = profile.export_allowed(settings, profile_path or None)
    if allowed:
        if message:
            print(f"[WorldBuilder][WARNING] {message}")
        return None
    return "ERROR", message, None


def export_dirty_chunks(context, settings, force=False, selected_only=False):
    profile_issue = _profile_issue(settings)
    if profile_issue is not None:
        return [], [profile_issue]

    chunks = collect_chunks(context.scene, settings)
    if selected_only:
        selected = set(context.selected_objects)
        chunks = {
            coordinate: objects
            for coordinate, objects in chunks.items()
            if any(obj in selected for obj in objects)
        }
    issues = validate_scene(
        context.scene,
        settings,
        [obj for objects in chunks.values() for obj in objects]
        if selected_only
        else None,
    )
    errors = [issue for issue in issues if issue[0] == "ERROR"]
    if errors:
        return [], issues
    results = []
    for coordinate, objects in chunks.items():
        status, path = export_chunk(context, settings, coordinate, objects, force)
        results.append((coordinate, status, path))
    state.clear_dirty(settings, [coordinate for coordinate, _, _ in results])
    return results, issues


def export_coordinates(context, settings, coordinates, force=False):
    profile_issue = _profile_issue(settings)
    if profile_issue is not None:
        return [], [profile_issue]
    chunks = collect_chunks(context.scene, settings)
    coordinates = sorted(set(coordinates))
    objects = [obj for coordinate in coordinates for obj in chunks.get(coordinate, [])]
    issues = validate_scene(context.scene, settings, objects)
    if any(issue[0] == "ERROR" for issue in issues):
        return [], issues
    results = []
    for coordinate in coordinates:
        chunk_objects = chunks.get(coordinate, [])
        status, path = export_chunk(context, settings, coordinate, chunk_objects, force)
        results.append((coordinate, status, path))
    state.clear_dirty(settings, coordinates)
    return results, issues


def ensure_chunk_collection(scene, coordinate):
    name = contract.chunk_name(coordinate)
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        scene.collection.children.link(collection)
    return collection


def assign_objects_to_chunk(scene, objects, coordinate, explicit_override=False):
    """Assign objects to a CH_* collection by default.

    Explicit object override is reserved for intentional exceptional ownership.
    """
    target = ensure_chunk_collection(scene, coordinate)
    for obj in objects:
        properties = getattr(obj, "worldbuilder_chunk", None)
        if properties is not None:
            properties.override_chunk = bool(explicit_override)
            if explicit_override:
                properties.chunk_x, properties.chunk_z = coordinate
        if explicit_override:
            obj["wb_chunk_x"], obj["wb_chunk_z"] = coordinate
        else:
            if "wb_chunk_x" in obj:
                del obj["wb_chunk_x"]
            if "wb_chunk_z" in obj:
                del obj["wb_chunk_z"]
        if obj.name not in target.objects:
            target.objects.link(obj)
        for collection in list(obj.users_collection):
            if collection != target and contract.parse_chunk_name(collection.name) is not None:
                collection.objects.unlink(obj)


def _export_fbx(context, objects, path, origin_x, origin_y):
    previous_active = context.view_layer.objects.active
    previous_selection = list(context.selected_objects)
    if context.object is not None and context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    temp_collection = bpy.data.collections.new("__WB_EXPORT_TEMP__")
    context.scene.collection.children.link(temp_collection)
    duplicates = []
    try:
        offset = Matrix.Translation((-origin_x, -origin_y, 0.0))
        bpy.ops.object.select_all(action="DESELECT")
        for source in objects:
            duplicate = source.copy()
            duplicate.matrix_world = offset @ source.matrix_world
            temp_collection.objects.link(duplicate)
            duplicate.select_set(True)
            duplicates.append(duplicate)
        context.view_layer.objects.active = duplicates[0]
        os.makedirs(os.path.dirname(path), exist_ok=True)
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH", "OTHER"},
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z",
            axis_up="Y",
            use_mesh_modifiers=True,
            mesh_smooth_type="OFF",
            use_tspace=True,
            add_leaf_bones=False,
            bake_anim=False,
        )
    finally:
        for duplicate in duplicates:
            bpy.data.objects.remove(duplicate, do_unlink=True)
        bpy.data.collections.remove(temp_collection)
        for obj in previous_selection:
            if obj.name in context.view_layer.objects:
                obj.select_set(True)
        if previous_active is not None and previous_active.name in context.view_layer.objects:
            context.view_layer.objects.active = previous_active


def _write_placements(path, world_id, coordinate, objects, origin_x, origin_y, settings):
    records = []
    for obj in sorted(objects, key=lambda value: (ensure_stable_id(value), value.name)):
        role = object_role(obj)
        matrix = [
            [obj.matrix_world[row][column] for column in range(4)] for row in range(4)
        ]
        properties = []
        for key in sorted(obj.keys()):
            key_text = str(key)
            if key_text.startswith("wb_prop_"):
                properties.append({"key": key_text[8:], "value": str(obj[key])})
        object_properties = getattr(obj, "worldbuilder_chunk", None)
        if role == "MARKER" and not any(
            item["key"] == "marker_type" for item in properties
        ):
            marker_type = (
                object_properties.marker_type
                if object_properties is not None
                else obj.get("wb_marker_type", "")
            )
            properties.append({"key": "marker_type", "value": str(marker_type)})
        record = {
            "stableId": ensure_stable_id(obj),
            "name": obj.name,
            "role": role,
            "assetId": str(
                object_properties.asset_id
                if object_properties is not None
                else obj.get("wb_asset_id", "")
            )
            if role in ASSET_ROLES
            else "",
            "layer": object_layer(obj, settings),
            "matrix": contract.blender_matrix_to_unity_row_major(
                matrix, origin_x, origin_y
            ),
            "properties": sorted(properties, key=lambda item: item["key"]),
        }
        if role == "ENTITY":
            payload = entity_payload(obj)
            if payload is not None:
                record["entity"] = payload
        records.append(record)
    document = {
        "version": contract.PLACEMENTS_VERSION,
        "worldId": world_id,
        "chunk": {"x": coordinate[0], "z": coordinate[1]},
        "objects": records,
    }
    _atomic_write(path, contract.canonical_json(document))


def _local_unity_bounds(objects, origin_x, origin_y):
    points = []
    for obj in objects:
        bounds = world_bounds(obj)
        if bounds is None:
            continue
        minimum, maximum = bounds
        for x in (minimum.x, maximum.x):
            for y in (minimum.y, maximum.y):
                for z in (minimum.z, maximum.z):
                    points.append((x - origin_x, z, y - origin_y))
    if not points:
        points = [(0.0, 0.0, 0.0)]
    minimum = [min(point[axis] for point in points) for axis in range(3)]
    maximum = [max(point[axis] for point in points) for axis in range(3)]
    return {
        "min": {
            "x": contract.normalized_float(minimum[0]),
            "y": contract.normalized_float(minimum[1]),
            "z": contract.normalized_float(minimum[2]),
        },
        "max": {
            "x": contract.normalized_float(maximum[0]),
            "y": contract.normalized_float(maximum[1]),
            "z": contract.normalized_float(maximum[2]),
        },
    }


def _empty_file():
    return {"path": "", "sha256": "", "bytes": 0}


def _load_cache(path):
    try:
        with open(path, "r", encoding="utf-8") as stream:
            value = json.load(stream)
            return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        return {}


def _atomic_write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(
        prefix=".wb-", suffix=".tmp", dir=os.path.dirname(path)
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(text)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.remove(temporary)
