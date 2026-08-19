from __future__ import annotations

bl_info = {
    "name": "Nex Stylized Terrain Toolkit",
    "author": "Nex EngineWorks / Emiteat",
    "version": (0, 1, 0),
    "blender": (4, 2, 0),
    "location": "View3D > Sidebar > WorldBuilder",
    "description": "Generate, stylize, shade, chunk, and populate faceted painterly low-poly terrain",
    "category": "Mesh",
}

import math
import random
from typing import Iterable

import bmesh
import bpy
from bpy.props import (
    BoolProperty,
    EnumProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
)
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Matrix, Quaternion, Vector

from . import biome


ADDON_TAG = "nex_stylized_terrain_generated"
KIND_TAG = "nex_stylized_terrain_kind"
OUTPUT_COLLECTION_NAME = "NexTerrain_Generated"
SCATTER_COLLECTION_NAME = "NexTerrain_Scatter"


# -----------------------------------------------------------------------------
# Deterministic noise
# -----------------------------------------------------------------------------


def _hash_u32(x: int) -> int:
    x &= 0xFFFFFFFF
    x ^= x >> 16
    x = (x * 0x7FEB352D) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 0x846CA68B) & 0xFFFFFFFF
    x ^= x >> 16
    return x & 0xFFFFFFFF


def _hash_2d(ix: int, iy: int, seed: int) -> float:
    value = _hash_u32(ix * 0x1F123BB5 ^ iy * 0x5F356495 ^ seed * 0x6C8E9CF5)
    return (value / 4294967295.0) * 2.0 - 1.0


def _smoothstep(value: float) -> float:
    return value * value * (3.0 - 2.0 * value)


def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def _value_noise_2d(x: float, y: float, seed: int) -> float:
    x0 = math.floor(x)
    y0 = math.floor(y)
    tx = _smoothstep(x - x0)
    ty = _smoothstep(y - y0)

    n00 = _hash_2d(x0, y0, seed)
    n10 = _hash_2d(x0 + 1, y0, seed)
    n01 = _hash_2d(x0, y0 + 1, seed)
    n11 = _hash_2d(x0 + 1, y0 + 1, seed)

    nx0 = _lerp(n00, n10, tx)
    nx1 = _lerp(n01, n11, tx)
    return _lerp(nx0, nx1, ty)


def _fbm_2d(
    x: float,
    y: float,
    seed: int,
    octaves: int,
    persistence: float,
    lacunarity: float = 2.0,
) -> float:
    total = 0.0
    amplitude = 1.0
    frequency = 1.0
    norm = 0.0

    for octave in range(max(1, octaves)):
        total += _value_noise_2d(x * frequency, y * frequency, seed + octave * 131) * amplitude
        norm += amplitude
        amplitude *= persistence
        frequency *= lacunarity

    return total / max(norm, 1e-8)


def _stable_random_01(index: int, seed: int) -> float:
    return _hash_u32(index * 0x9E3779B1 ^ seed * 0x85EBCA77) / 4294967295.0


# -----------------------------------------------------------------------------
# Data and material helpers
# -----------------------------------------------------------------------------


TERRAIN_PRESETS = [
    ("REEF_PLAINS", "Reef Plains", "Broad rolling seabed with restrained vertical relief"),
    ("RIDGED", "Ridged Seabed", "Sharper ridges and taller rock-like terrain masses"),
    ("CANYON", "Canyon", "Continuous channels and ravine-like cuts"),
    ("PLATEAU", "Terraced Plateau", "Stepped shelves and layered cliffs"),
    ("ISLAND", "Island / Basin", "Raised center with controlled falloff toward the boundary"),
]


def _tag(id_block, kind: str) -> None:
    id_block[ADDON_TAG] = True
    id_block[KIND_TAG] = kind


def _is_generated(obj: bpy.types.Object, kind: str | None = None) -> bool:
    if not obj.get(ADDON_TAG):
        return False
    return kind is None or obj.get(KIND_TAG) == kind


def _ensure_collection(context: bpy.types.Context, name: str, kind: str) -> bpy.types.Collection:
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        context.scene.collection.children.link(collection)
        _tag(collection, kind)
    elif collection.name not in context.scene.collection.children:
        context.scene.collection.children.link(collection)
    return collection


def _link_object(collection: bpy.types.Collection, obj: bpy.types.Object) -> None:
    if obj.name not in collection.objects:
        collection.objects.link(obj)


def _set_principled_material(
    name: str,
    color: tuple[float, float, float, float],
    roughness: float,
    metallic: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    material.diffuse_color = color

    nodes = material.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if bsdf is not None:
        if bsdf.inputs.get("Base Color"):
            bsdf.inputs["Base Color"].default_value = color
        if bsdf.inputs.get("Roughness"):
            bsdf.inputs["Roughness"].default_value = roughness
        if bsdf.inputs.get("Metallic"):
            bsdf.inputs["Metallic"].default_value = metallic
        if bsdf.inputs.get("Specular IOR Level"):
            bsdf.inputs["Specular IOR Level"].default_value = 0.28
    return material


def _terrain_palette() -> list[bpy.types.Material]:
    return [
        _set_principled_material("NexTerrain_Sand", (0.63, 0.49, 0.29, 1.0), 0.95),
        _set_principled_material("NexTerrain_Rock_Mid", (0.27, 0.36, 0.49, 1.0), 0.88),
        _set_principled_material("NexTerrain_Rock_Light", (0.47, 0.51, 0.57, 1.0), 0.84),
        _set_principled_material("NexTerrain_Rock_Dark", (0.12, 0.19, 0.30, 1.0), 0.92),
        _set_principled_material("NexTerrain_Algae", (0.18, 0.40, 0.25, 1.0), 0.90),
    ]


def _append_materials(obj: bpy.types.Object, materials: Iterable[bpy.types.Material]) -> None:
    existing = {material.name for material in obj.data.materials if material is not None}
    for material in materials:
        if material.name not in existing:
            obj.data.materials.append(material)
            existing.add(material.name)


def _assign_terrain_materials(obj: bpy.types.Object, settings: "NEX_PG_TerrainSettings") -> None:
    mesh = obj.data
    if not isinstance(mesh, bpy.types.Mesh):
        return

    palette = _terrain_palette()
    mesh.materials.clear()
    _append_materials(obj, palette)
    mesh.update()

    if not mesh.vertices or not mesh.polygons:
        return

    z_values = [vertex.co.z for vertex in mesh.vertices]
    z_min = min(z_values)
    z_max = max(z_values)
    z_span = max(z_max - z_min, 1e-6)
    cliff_cos = math.cos(math.radians(settings.cliff_slope))

    for polygon in mesh.polygons:
        polygon.use_smooth = False
        center_z = polygon.center.z
        relative_height = (center_z - z_min) / z_span
        upward = max(-1.0, min(1.0, polygon.normal.z))
        random_value = _stable_random_01(polygon.index + int(center_z * 1000.0), settings.seed)

        if center_z <= settings.sea_level + settings.sand_band and upward >= cliff_cos:
            material_index = 0  # sand
        elif upward < cliff_cos:
            material_index = 3 if random_value < 0.72 else 1  # dark cliff / mid
        elif settings.algae_chance > 0.0 and random_value < settings.algae_chance and relative_height < 0.68:
            material_index = 4
        elif relative_height > 0.60 or upward > 0.93:
            material_index = 2 if random_value < 0.78 else 1
        else:
            material_index = 1 if random_value < 0.80 else 3

        polygon.material_index = material_index


def _new_mesh_object(
    name: str,
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    collection: bpy.types.Collection,
    kind: str,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)

    obj = bpy.data.objects.new(name, mesh)
    _link_object(collection, obj)
    _tag(mesh, kind)
    _tag(obj, kind)
    if kind == "TERRAIN":
        biome.mark_biome_target(obj)
    return obj


def _select_only(context: bpy.types.Context, obj: bpy.types.Object) -> None:
    for selected in context.selected_objects:
        selected.select_set(False)
    obj.select_set(True)
    context.view_layer.objects.active = obj


def _apply_modifier(context: bpy.types.Context, obj: bpy.types.Object, modifier_name: str) -> bool:
    try:
        _select_only(context, obj)
        with context.temp_override(
            object=obj,
            active_object=obj,
            selected_objects=[obj],
            selected_editable_objects=[obj],
        ):
            bpy.ops.object.modifier_apply(modifier=modifier_name)
        return True
    except (RuntimeError, TypeError):
        return False


# -----------------------------------------------------------------------------
# Terrain generation
# -----------------------------------------------------------------------------


def _preset_height(
    x: float,
    y: float,
    total_width: float,
    total_depth: float,
    settings: "NEX_PG_TerrainSettings",
) -> float:
    scale = max(settings.noise_scale, 0.001)
    nx = x / scale
    ny = y / scale

    macro = _fbm_2d(nx, ny, settings.seed, settings.octaves, settings.persistence)
    detail = _fbm_2d(nx * 2.45, ny * 2.45, settings.seed + 701, max(1, settings.octaves - 1), 0.48)
    ridge_source = _fbm_2d(nx * 0.72, ny * 0.72, settings.seed + 1709, max(2, settings.octaves), 0.56)
    ridged = 1.0 - abs(ridge_source)
    ridged = ridged * ridged

    if settings.preset == "REEF_PLAINS":
        shape = macro * 0.72 + detail * 0.18 + ridged * settings.ridge_strength * 0.22
    elif settings.preset == "RIDGED":
        shape = macro * 0.42 + detail * 0.16 + ridged * (0.78 + settings.ridge_strength * 0.55)
        shape -= 0.28
    elif settings.preset == "CANYON":
        channel_noise = _fbm_2d(nx * 0.58, ny * 0.58, settings.seed + 3203, 3, 0.58)
        channel = math.exp(-abs(channel_noise) * 8.5)
        shape = macro * 0.52 + detail * 0.13 - channel * (0.85 + settings.ridge_strength * 0.35)
    elif settings.preset == "PLATEAU":
        shape = macro * 0.78 + ridged * settings.ridge_strength * 0.28
    else:  # ISLAND
        shape = macro * 0.55 + detail * 0.10 + ridged * settings.ridge_strength * 0.18
        radius = math.sqrt((x / max(total_width * 0.5, 1e-6)) ** 2 + (y / max(total_depth * 0.5, 1e-6)) ** 2)
        island = max(0.0, 1.0 - radius)
        shape += island * 0.85 - (1.0 - island) * settings.edge_falloff

    height = settings.base_height + shape * settings.height

    if settings.terrace_steps > 1 and settings.terrace_strength > 0.0:
        normalized = (height - settings.base_height) / max(settings.height, 1e-6)
        stepped = round(normalized * settings.terrace_steps) / settings.terrace_steps
        terraced_height = settings.base_height + stepped * settings.height
        height = _lerp(height, terraced_height, settings.terrace_strength)

    return height


def _grid_jitter(global_ix: int, global_iy: int, seed: int, amount: float) -> tuple[float, float]:
    if amount <= 0.0:
        return 0.0, 0.0
    index = global_ix * 73856093 ^ global_iy * 19349663
    jx = (_stable_random_01(index, seed + 41) * 2.0 - 1.0) * amount
    jy = (_stable_random_01(index, seed + 97) * 2.0 - 1.0) * amount
    return jx, jy


def _add_outer_skirt(
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    cells: int,
    side: str,
    base_z: float,
) -> None:
    if side == "LEFT":
        top_indices = [iy * (cells + 1) for iy in range(cells + 1)]
    elif side == "RIGHT":
        top_indices = [iy * (cells + 1) + cells for iy in range(cells + 1)]
    elif side == "BOTTOM":
        top_indices = list(range(cells + 1))
    else:  # TOP
        start = cells * (cells + 1)
        top_indices = [start + ix for ix in range(cells + 1)]

    bottom_indices: list[int] = []
    for top_index in top_indices:
        x, y, _ = vertices[top_index]
        bottom_indices.append(len(vertices))
        vertices.append((x, y, base_z))

    for index in range(cells):
        a = top_indices[index]
        b = top_indices[index + 1]
        c = bottom_indices[index + 1]
        d = bottom_indices[index]
        if side in {"LEFT", "TOP"}:
            faces.append((a, d, c, b))
        else:
            faces.append((a, b, c, d))


def _build_chunk(
    context: bpy.types.Context,
    settings: "NEX_PG_TerrainSettings",
    chunk_x: int,
    chunk_y: int,
) -> bpy.types.Object:
    cells = settings.resolution
    chunks_x = settings.chunks_x
    chunks_y = settings.chunks_y
    chunk_width = settings.width / chunks_x
    chunk_depth = settings.depth / chunks_y
    step_x = chunk_width / cells
    step_y = chunk_depth / cells

    center_x = -settings.width * 0.5 + chunk_width * (chunk_x + 0.5)
    center_y = -settings.depth * 0.5 + chunk_depth * (chunk_y + 0.5)

    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []

    jitter_limit = min(step_x, step_y) * 0.42
    jitter_amount = settings.vertex_jitter * jitter_limit

    for iy in range(cells + 1):
        for ix in range(cells + 1):
            global_ix = chunk_x * cells + ix
            global_iy = chunk_y * cells + iy
            jitter_x, jitter_y = _grid_jitter(global_ix, global_iy, settings.seed, jitter_amount)

            local_x = -chunk_width * 0.5 + ix * step_x + jitter_x
            local_y = -chunk_depth * 0.5 + iy * step_y + jitter_y
            world_x = center_x + local_x
            world_y = center_y + local_y
            z = _preset_height(world_x, world_y, settings.width, settings.depth, settings)
            vertices.append((local_x, local_y, z))

    for iy in range(cells):
        for ix in range(cells):
            a = iy * (cells + 1) + ix
            b = a + 1
            d = (iy + 1) * (cells + 1) + ix
            c = d + 1
            global_parity = (chunk_x * cells + ix + chunk_y * cells + iy) & 1
            if global_parity == 0:
                faces.extend(((a, b, c), (a, c, d)))
            else:
                faces.extend(((a, b, d), (b, c, d)))

    if settings.create_skirt:
        skirt_z = settings.base_height - abs(settings.skirt_depth)
        if chunk_x == 0:
            _add_outer_skirt(vertices, faces, cells, "LEFT", skirt_z)
        if chunk_x == chunks_x - 1:
            _add_outer_skirt(vertices, faces, cells, "RIGHT", skirt_z)
        if chunk_y == 0:
            _add_outer_skirt(vertices, faces, cells, "BOTTOM", skirt_z)
        if chunk_y == chunks_y - 1:
            _add_outer_skirt(vertices, faces, cells, "TOP", skirt_z)

    collection = _ensure_collection(context, OUTPUT_COLLECTION_NAME, "COLLECTION")
    name = f"NexTerrain_{chunk_x:02d}_{chunk_y:02d}"
    obj = _new_mesh_object(name, vertices, faces, collection, "TERRAIN")
    obj.location = (center_x, center_y, 0.0)
    obj["nex_chunk_x"] = chunk_x
    obj["nex_chunk_y"] = chunk_y
    obj["nex_seed"] = settings.seed
    _assign_terrain_materials(obj, settings)
    return obj


def _remove_generated_objects(kind: str | None = None) -> int:
    removed = 0
    for obj in list(bpy.data.objects):
        if _is_generated(obj, kind):
            bpy.data.objects.remove(obj, do_unlink=True)
            removed += 1

    for mesh in list(bpy.data.meshes):
        if mesh.get(ADDON_TAG) and mesh.users == 0:
            bpy.data.meshes.remove(mesh)

    for collection_name in (OUTPUT_COLLECTION_NAME, SCATTER_COLLECTION_NAME):
        collection = bpy.data.collections.get(collection_name)
        if collection is not None and len(collection.objects) == 0 and len(collection.children) == 0:
            bpy.data.collections.remove(collection)
    return removed


# -----------------------------------------------------------------------------
# Existing mesh stylization
# -----------------------------------------------------------------------------


def _duplicate_mesh_object(context: bpy.types.Context, source: bpy.types.Object) -> bpy.types.Object:
    duplicate = source.copy()
    duplicate.data = source.data.copy()
    duplicate.animation_data_clear()
    target_collection = source.users_collection[0] if source.users_collection else context.collection
    target_collection.objects.link(duplicate)
    duplicate.name = f"{source.name}_Stylized"
    _tag(duplicate, "STYLIZED")
    _tag(duplicate.data, "STYLIZED")
    return duplicate


def _stylize_mesh_data(obj: bpy.types.Object, settings: "NEX_PG_TerrainSettings") -> None:
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    boundary_vertices = {
        vertex
        for edge in bm.edges
        if len(edge.link_faces) == 1
        for vertex in edge.verts
    }

    if settings.triangulate_existing:
        bmesh.ops.triangulate(
            bm,
            faces=list(bm.faces),
            quad_method="BEAUTY",
            ngon_method="BEAUTY",
        )
        bm.verts.ensure_lookup_table()

    terrace_size = settings.existing_terrace_size
    for vertex in bm.verts:
        if settings.preserve_boundary and vertex in boundary_vertices:
            continue

        random_x = _stable_random_01(vertex.index * 3 + 0, settings.seed + 401) * 2.0 - 1.0
        random_y = _stable_random_01(vertex.index * 3 + 1, settings.seed + 503) * 2.0 - 1.0
        random_z = _stable_random_01(vertex.index * 3 + 2, settings.seed + 607) * 2.0 - 1.0

        vertex.co.x += random_x * settings.existing_xy_jitter
        vertex.co.y += random_y * settings.existing_xy_jitter
        vertex.co.z += random_z * settings.existing_z_jitter

        if terrace_size > 0.0:
            stepped = round(vertex.co.z / terrace_size) * terrace_size
            vertex.co.z = _lerp(vertex.co.z, stepped, settings.existing_terrace_strength)

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)

    for polygon in mesh.polygons:
        polygon.use_smooth = False


def _decimate_object(context: bpy.types.Context, obj: bpy.types.Object, ratio: float) -> bool:
    if ratio >= 0.999:
        return True
    modifier = obj.modifiers.new(name="Nex Terrain Decimate", type="DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = max(0.02, min(1.0, ratio))
    modifier.use_collapse_triangulate = True
    success = _apply_modifier(context, obj, modifier.name)
    if not success and modifier.name in obj.modifiers:
        obj.modifiers.remove(modifier)
    return success


# -----------------------------------------------------------------------------
# Scatter
# -----------------------------------------------------------------------------


def _scatter_sources(collection: bpy.types.Collection | None, terrain: bpy.types.Object) -> list[bpy.types.Object]:
    if collection is None:
        return []
    return [
        obj
        for obj in collection.all_objects
        if obj != terrain and obj.type in {"MESH", "CURVE", "SURFACE", "META", "FONT"}
    ]


def _scatter_on_terrain(
    context: bpy.types.Context,
    terrain: bpy.types.Object,
    settings: "NEX_PG_TerrainSettings",
) -> tuple[int, int]:
    sources = _scatter_sources(settings.scatter_collection, terrain)
    if not sources:
        return 0, 0

    bbox = [Vector(corner) for corner in terrain.bound_box]
    min_x = min(point.x for point in bbox)
    max_x = max(point.x for point in bbox)
    min_y = min(point.y for point in bbox)
    max_y = max(point.y for point in bbox)
    min_z = min(point.z for point in bbox)
    max_z = max(point.z for point in bbox)

    rng = random.Random(settings.scatter_seed)
    output = _ensure_collection(context, SCATTER_COLLECTION_NAME, "COLLECTION")
    created = 0
    attempts = 0
    max_attempts = max(settings.scatter_count * 20, 100)
    max_slope_cos = math.cos(math.radians(settings.scatter_max_slope))

    while created < settings.scatter_count and attempts < max_attempts:
        attempts += 1
        x = rng.uniform(min_x, max_x)
        y = rng.uniform(min_y, max_y)
        origin = Vector((x, y, max_z + max(10.0, (max_z - min_z) * 2.0)))
        direction = Vector((0.0, 0.0, -1.0))
        hit, location_local, normal_local, _face_index = terrain.ray_cast(
            origin,
            direction,
            distance=max(100.0, (max_z - min_z) * 8.0 + 20.0),
        )
        if not hit:
            continue

        location_world = terrain.matrix_world @ location_local
        normal_world = (terrain.matrix_world.to_3x3() @ normal_local).normalized()
        if normal_world.z < max_slope_cos:
            continue
        if location_world.z < settings.scatter_min_height or location_world.z > settings.scatter_max_height:
            continue
        if rng.random() > settings.scatter_density_mask:
            continue

        source = rng.choice(sources)
        instance = source.copy()
        if source.data is not None:
            instance.data = source.data
        instance.animation_data_clear()
        instance.parent = None
        instance.matrix_parent_inverse = Matrix.Identity(4)
        instance.name = f"NexScatter_{source.name}_{created:04d}"
        _link_object(output, instance)
        _tag(instance, "SCATTER")

        scale = rng.uniform(settings.scatter_min_scale, settings.scatter_max_scale)
        scale_x = source.scale.x * scale * rng.uniform(0.88, 1.12)
        scale_y = source.scale.y * scale * rng.uniform(0.88, 1.12)
        scale_z = source.scale.z * scale * rng.uniform(0.92, 1.15)
        instance.scale = (scale_x, scale_y, scale_z)
        instance.location = location_world

        if settings.scatter_align_normal:
            align = Vector((0.0, 0.0, 1.0)).rotation_difference(normal_world)
            yaw = Quaternion(normal_world, rng.uniform(0.0, math.tau))
            rotation = yaw @ align
        else:
            rotation = Quaternion((0.0, 0.0, 1.0), rng.uniform(0.0, math.tau))

        tilt = math.radians(settings.scatter_random_tilt)
        if tilt > 0.0:
            rotation = Quaternion((1.0, 0.0, 0.0), rng.uniform(-tilt, tilt)) @ rotation
            rotation = Quaternion((0.0, 1.0, 0.0), rng.uniform(-tilt, tilt)) @ rotation

        instance.rotation_mode = "QUATERNION"
        instance.rotation_quaternion = rotation
        created += 1

    return created, attempts


# -----------------------------------------------------------------------------
# Preview helpers
# -----------------------------------------------------------------------------


def _create_preview_lighting(context: bpy.types.Context) -> None:
    collection = _ensure_collection(context, OUTPUT_COLLECTION_NAME, "COLLECTION")

    for name in ("NexTerrain_Sun", "NexTerrain_Fill"):
        existing = bpy.data.objects.get(name)
        if existing is not None:
            bpy.data.objects.remove(existing, do_unlink=True)

    sun_data = bpy.data.lights.new(name="NexTerrain_Sun_Data", type="SUN")
    sun_data.energy = 2.1
    sun_data.angle = math.radians(18.0)
    sun = bpy.data.objects.new("NexTerrain_Sun", sun_data)
    _link_object(collection, sun)
    _tag(sun, "LIGHT")
    _tag(sun_data, "LIGHT")
    sun.rotation_euler = (math.radians(28.0), math.radians(-22.0), math.radians(32.0))

    fill_data = bpy.data.lights.new(name="NexTerrain_Fill_Data", type="AREA")
    fill_data.energy = 850.0
    fill_data.shape = "DISK"
    fill_data.size = 18.0
    fill_data.color = (0.14, 0.48, 0.82)
    fill = bpy.data.objects.new("NexTerrain_Fill", fill_data)
    _link_object(collection, fill)
    _tag(fill, "LIGHT")
    _tag(fill_data, "LIGHT")
    fill.location = (-8.0, -6.0, 12.0)
    fill.rotation_euler = (math.radians(18.0), 0.0, math.radians(-35.0))

    world = context.scene.world
    if world is None:
        world = bpy.data.worlds.new("NexTerrain_World")
        context.scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.012, 0.075, 0.13, 1.0)
        background.inputs["Strength"].default_value = 0.34


# -----------------------------------------------------------------------------
# Operators
# -----------------------------------------------------------------------------


class NEX_OT_GenerateTerrain(Operator):
    bl_idname = "nex.generate_stylized_terrain"
    bl_label = "Generate Stylized Terrain"
    bl_description = "Generate a seamless grid of faceted terrain chunks"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        settings = context.scene.nex_terrain_settings
        vertex_count = settings.chunks_x * settings.chunks_y * (settings.resolution + 1) ** 2
        if vertex_count > 1_200_000:
            self.report({"ERROR"}, f"Requested terrain is too dense ({vertex_count:,} vertices). Lower chunks or resolution.")
            return {"CANCELLED"}

        if settings.replace_generated:
            _remove_generated_objects("TERRAIN")

        created: list[bpy.types.Object] = []
        for chunk_y in range(settings.chunks_y):
            for chunk_x in range(settings.chunks_x):
                created.append(_build_chunk(context, settings, chunk_x, chunk_y))

        if created:
            _select_only(context, created[0])
        self.report({"INFO"}, f"Generated {len(created)} terrain chunk(s), approximately {vertex_count:,} top vertices.")
        return {"FINISHED"}


class NEX_OT_StylizeSelectedTerrain(Operator):
    bl_idname = "nex.stylize_selected_terrain"
    bl_label = "Stylize Selected Terrain"
    bl_description = "Duplicate or modify the active mesh into a faceted low-poly terrain asset"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        settings = context.scene.nex_terrain_settings
        source = context.active_object
        if source is None or source.type != "MESH":
            self.report({"ERROR"}, "Select an active mesh terrain object first.")
            return {"CANCELLED"}

        obj = _duplicate_mesh_object(context, source) if settings.duplicate_existing else source
        if not settings.duplicate_existing:
            _tag(obj, "STYLIZED")
            _tag(obj.data, "STYLIZED")
        biome.mark_biome_target(obj)

        if not _decimate_object(context, obj, settings.decimate_ratio):
            self.report({"WARNING"}, "Decimate could not be applied; continuing with the original topology.")

        _stylize_mesh_data(obj, settings)
        if settings.apply_material_palette:
            _assign_terrain_materials(obj, settings)

        _select_only(context, obj)
        self.report({"INFO"}, f"Stylized {obj.name}.")
        return {"FINISHED"}


class NEX_OT_ReapplyTerrainMaterials(Operator):
    bl_idname = "nex.reapply_terrain_materials"
    bl_label = "Reapply Terrain Palette"
    bl_description = "Recalculate sand, rock, cliff, highlight, and algae material assignment"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        settings = context.scene.nex_terrain_settings
        objects = [obj for obj in context.selected_objects if obj.type == "MESH"]
        if not objects and context.active_object and context.active_object.type == "MESH":
            objects = [context.active_object]
        if not objects:
            self.report({"ERROR"}, "Select at least one mesh object.")
            return {"CANCELLED"}

        for obj in objects:
            _assign_terrain_materials(obj, settings)
        self.report({"INFO"}, f"Updated palette on {len(objects)} object(s).")
        return {"FINISHED"}


class NEX_OT_UseGeneratedRockCollection(Operator):
    bl_idname = "nex.use_generated_rock_collection"
    bl_label = "Use Generated Rocks"
    bl_description = "Use the collection created by the integrated Stylized Rock Generator as the terrain scatter source"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context):
        collection = bpy.data.collections.get("NexRock_Generated")
        if collection is None:
            self.report({"ERROR"}, "Generate rocks first; NexRock_Generated does not exist.")
            return {"CANCELLED"}
        context.scene.nex_terrain_settings.scatter_collection = collection
        self.report({"INFO"}, "Scatter source set to NexRock_Generated.")
        return {"FINISHED"}


class NEX_OT_ScatterTerrainProps(Operator):
    bl_idname = "nex.scatter_terrain_props"
    bl_label = "Scatter Props on Active Terrain"
    bl_description = "Scatter linked copies from a collection onto the active terrain mesh"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        settings = context.scene.nex_terrain_settings
        terrain = context.active_object
        if terrain is None or terrain.type != "MESH":
            self.report({"ERROR"}, "Make a terrain mesh the active object.")
            return {"CANCELLED"}
        if settings.scatter_collection is None:
            self.report({"ERROR"}, "Choose a source collection containing rocks, coral, or vegetation.")
            return {"CANCELLED"}

        if settings.replace_scatter:
            _remove_generated_objects("SCATTER")

        created, attempts = _scatter_on_terrain(context, terrain, settings)
        if created == 0:
            self.report({"WARNING"}, "No props were placed. Check collection, slope, height, and density filters.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Placed {created} linked prop instances in {attempts} attempts.")
        return {"FINISHED"}


class NEX_OT_JoinTerrainChunks(Operator):
    bl_idname = "nex.join_terrain_chunks"
    bl_label = "Join Generated Chunks"
    bl_description = "Join generated terrain chunks and weld matching seam vertices"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        chunks = [obj for obj in bpy.data.objects if obj.type == "MESH" and _is_generated(obj, "TERRAIN")]
        if len(chunks) < 2:
            self.report({"ERROR"}, "At least two generated terrain chunks are required.")
            return {"CANCELLED"}

        for selected in context.selected_objects:
            selected.select_set(False)
        for chunk in chunks:
            chunk.select_set(True)
        active = chunks[0]
        context.view_layer.objects.active = active

        try:
            with context.temp_override(
                object=active,
                active_object=active,
                selected_objects=chunks,
                selected_editable_objects=chunks,
            ):
                bpy.ops.object.join()
        except RuntimeError as exc:
            self.report({"ERROR"}, f"Could not join chunks: {exc}")
            return {"CANCELLED"}

        active.name = "NexTerrain_Joined"
        bm = bmesh.new()
        bm.from_mesh(active.data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        bm.normal_update()
        bm.to_mesh(active.data)
        bm.free()
        active.data.update(calc_edges=True)
        _tag(active, "TERRAIN")
        _tag(active.data, "TERRAIN")
        _assign_terrain_materials(active, context.scene.nex_terrain_settings)
        _select_only(context, active)
        self.report({"INFO"}, "Joined and welded generated terrain chunks.")
        return {"FINISHED"}


class NEX_OT_CreateTerrainLighting(Operator):
    bl_idname = "nex.create_terrain_lighting"
    bl_label = "Create Underwater Preview Lighting"
    bl_description = "Create a simple blue fill, sun light, and dark underwater world preview"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context):
        _create_preview_lighting(context)
        self.report({"INFO"}, "Created underwater preview lighting.")
        return {"FINISHED"}


class NEX_OT_ClearGeneratedTerrain(Operator):
    bl_idname = "nex.clear_generated_terrain"
    bl_label = "Clear Generated"
    bl_description = "Remove only objects created and tagged by this add-on"
    bl_options = {"REGISTER", "UNDO"}

    clear_mode: EnumProperty(
        name="Clear",
        items=[
            ("ALL", "Everything", "Terrain, stylized copies, scatter, and preview lights"),
            ("TERRAIN", "Terrain", "Generated terrain chunks only"),
            ("SCATTER", "Scatter", "Scattered props only"),
            ("STYLIZED", "Stylized Copies", "Stylized mesh copies only"),
            ("LIGHT", "Preview Lights", "Preview lights only"),
        ],
        default="ALL",
    )

    def execute(self, context: bpy.types.Context):
        kind = None if self.clear_mode == "ALL" else self.clear_mode
        removed = _remove_generated_objects(kind)
        self.report({"INFO"}, f"Removed {removed} generated object(s).")
        return {"FINISHED"}


# -----------------------------------------------------------------------------
# Properties and UI
# -----------------------------------------------------------------------------


class NEX_PG_TerrainSettings(PropertyGroup):
    preset: EnumProperty(name="Terrain Type", items=TERRAIN_PRESETS, default="REEF_PLAINS")
    seed: IntProperty(name="Seed", default=1337, min=0, max=2_147_483_647)
    width: FloatProperty(name="Width", default=60.0, min=1.0, soft_max=500.0, unit="LENGTH")
    depth: FloatProperty(name="Depth", default=60.0, min=1.0, soft_max=500.0, unit="LENGTH")
    height: FloatProperty(name="Relief", default=10.0, min=0.05, soft_max=100.0, unit="LENGTH")
    base_height: FloatProperty(name="Base Height", default=0.0, soft_min=-100.0, soft_max=100.0, unit="LENGTH")
    resolution: IntProperty(name="Cells per Chunk", default=28, min=4, max=160)
    chunks_x: IntProperty(name="Chunks X", default=2, min=1, max=8)
    chunks_y: IntProperty(name="Chunks Y", default=2, min=1, max=8)
    noise_scale: FloatProperty(name="Feature Size", default=12.0, min=0.1, soft_max=100.0, unit="LENGTH")
    octaves: IntProperty(name="Noise Octaves", default=5, min=1, max=8)
    persistence: FloatProperty(name="Roughness", default=0.52, min=0.1, max=0.9)
    ridge_strength: FloatProperty(name="Ridge Strength", default=0.55, min=0.0, max=2.0)
    terrace_steps: IntProperty(name="Terrace Steps", default=8, min=1, max=64)
    terrace_strength: FloatProperty(name="Terrace Strength", default=0.20, min=0.0, max=1.0)
    edge_falloff: FloatProperty(name="Edge Falloff", default=0.85, min=0.0, max=3.0)
    vertex_jitter: FloatProperty(name="Vertex Irregularity", default=0.34, min=0.0, max=1.0)
    create_skirt: BoolProperty(name="Create Outer Skirt", default=False)
    skirt_depth: FloatProperty(name="Skirt Depth", default=8.0, min=0.1, soft_max=100.0, unit="LENGTH")
    replace_generated: BoolProperty(name="Replace Previous Terrain", default=True)

    sea_level: FloatProperty(name="Sand Height", default=-1.0, soft_min=-100.0, soft_max=100.0, unit="LENGTH")
    sand_band: FloatProperty(name="Sand Band", default=1.8, min=0.0, soft_max=20.0, unit="LENGTH")
    cliff_slope: FloatProperty(name="Cliff Angle (°)", default=52.0, min=1.0, max=89.0)
    algae_chance: FloatProperty(name="Algae Face Chance", default=0.06, min=0.0, max=0.6)

    duplicate_existing: BoolProperty(name="Duplicate Before Stylizing", default=True)
    decimate_ratio: FloatProperty(name="Topology Ratio", default=0.38, min=0.02, max=1.0)
    triangulate_existing: BoolProperty(name="Triangulate", default=True)
    preserve_boundary: BoolProperty(name="Preserve Open Borders", default=True)
    existing_xy_jitter: FloatProperty(name="XY Irregularity", default=0.12, min=0.0, soft_max=5.0, unit="LENGTH")
    existing_z_jitter: FloatProperty(name="Z Irregularity", default=0.08, min=0.0, soft_max=5.0, unit="LENGTH")
    existing_terrace_size: FloatProperty(name="Terrace Height", default=0.0, min=0.0, soft_max=10.0, unit="LENGTH")
    existing_terrace_strength: FloatProperty(name="Terrace Blend", default=0.75, min=0.0, max=1.0)
    apply_material_palette: BoolProperty(name="Apply Terrain Palette", default=True)

    scatter_collection: PointerProperty(name="Source Collection", type=bpy.types.Collection)
    scatter_seed: IntProperty(name="Scatter Seed", default=404, min=0, max=2_147_483_647)
    scatter_count: IntProperty(name="Count", default=120, min=1, max=20_000)
    scatter_min_scale: FloatProperty(name="Min Scale", default=0.65, min=0.001, soft_max=10.0)
    scatter_max_scale: FloatProperty(name="Max Scale", default=1.55, min=0.001, soft_max=20.0)
    scatter_max_slope: FloatProperty(name="Maximum Slope (°)", default=48.0, min=0.0, max=89.0)
    scatter_min_height: FloatProperty(name="Min Height", default=-10_000.0, soft_min=-100.0, soft_max=100.0, unit="LENGTH")
    scatter_max_height: FloatProperty(name="Max Height", default=10_000.0, soft_min=-100.0, soft_max=100.0, unit="LENGTH")
    scatter_density_mask: FloatProperty(name="Placement Probability", default=1.0, min=0.0, max=1.0)
    scatter_align_normal: BoolProperty(name="Align to Surface", default=True)
    scatter_random_tilt: FloatProperty(name="Random Tilt (°)", default=4.0, min=0.0, max=45.0)
    replace_scatter: BoolProperty(name="Replace Previous Scatter", default=True)


class NEX_PT_StylizedTerrainPanel(Panel):
    bl_label = "Nex Stylized Terrain"
    bl_idname = "NEX_PT_stylized_terrain"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context: bpy.types.Context):
        layout = self.layout
        settings = context.scene.nex_terrain_settings

        generator = layout.box()
        generator.label(text="Generate Terrain", icon="MESH_GRID")
        generator.prop(settings, "preset")
        row = generator.row(align=True)
        row.prop(settings, "seed")
        row.operator("nex.generate_stylized_terrain", text="Generate", icon="MOD_DISPLACE")

        dims = generator.column(align=True)
        row = dims.row(align=True)
        row.prop(settings, "width")
        row.prop(settings, "depth")
        row = dims.row(align=True)
        row.prop(settings, "height")
        row.prop(settings, "base_height")

        mesh_box = generator.box()
        mesh_box.label(text="Topology / Chunks")
        row = mesh_box.row(align=True)
        row.prop(settings, "resolution")
        row.prop(settings, "vertex_jitter")
        row = mesh_box.row(align=True)
        row.prop(settings, "chunks_x")
        row.prop(settings, "chunks_y")
        mesh_box.prop(settings, "replace_generated")
        row = mesh_box.row(align=True)
        row.prop(settings, "create_skirt")
        sub = row.row(align=True)
        sub.enabled = settings.create_skirt
        sub.prop(settings, "skirt_depth")

        shape_box = generator.box()
        shape_box.label(text="Shape")
        row = shape_box.row(align=True)
        row.prop(settings, "noise_scale")
        row.prop(settings, "octaves")
        row = shape_box.row(align=True)
        row.prop(settings, "persistence")
        row.prop(settings, "ridge_strength")
        row = shape_box.row(align=True)
        row.prop(settings, "terrace_steps")
        row.prop(settings, "terrace_strength")
        if settings.preset == "ISLAND":
            shape_box.prop(settings, "edge_falloff")

        palette = layout.box()
        palette.label(text="Stylized Surface Palette", icon="MATERIAL")
        row = palette.row(align=True)
        row.prop(settings, "sea_level")
        row.prop(settings, "sand_band")
        row = palette.row(align=True)
        row.prop(settings, "cliff_slope")
        row.prop(settings, "algae_chance")
        palette.operator("nex.reapply_terrain_materials", icon="SHADING_RENDERED")

        existing = layout.box()
        existing.label(text="Stylize Existing Mesh", icon="MOD_DECIM")
        existing.prop(settings, "duplicate_existing")
        existing.prop(settings, "decimate_ratio")
        row = existing.row(align=True)
        row.prop(settings, "triangulate_existing")
        row.prop(settings, "preserve_boundary")
        row = existing.row(align=True)
        row.prop(settings, "existing_xy_jitter")
        row.prop(settings, "existing_z_jitter")
        row = existing.row(align=True)
        row.prop(settings, "existing_terrace_size")
        row.prop(settings, "existing_terrace_strength")
        existing.prop(settings, "apply_material_palette")
        existing.operator("nex.stylize_selected_terrain", icon="SCULPTMODE_HLT")

        scatter = layout.box()
        scatter.label(text="Populate Terrain", icon="OUTLINER_COLLECTION")
        scatter.prop(settings, "scatter_collection")
        scatter.operator("nex.use_generated_rock_collection", icon="EYEDROPPER")
        row = scatter.row(align=True)
        row.prop(settings, "scatter_seed")
        row.prop(settings, "scatter_count")
        row = scatter.row(align=True)
        row.prop(settings, "scatter_min_scale")
        row.prop(settings, "scatter_max_scale")
        row = scatter.row(align=True)
        row.prop(settings, "scatter_max_slope")
        row.prop(settings, "scatter_density_mask")
        row = scatter.row(align=True)
        row.prop(settings, "scatter_min_height")
        row.prop(settings, "scatter_max_height")
        row = scatter.row(align=True)
        row.prop(settings, "scatter_align_normal")
        row.prop(settings, "scatter_random_tilt")
        scatter.prop(settings, "replace_scatter")
        scatter.operator("nex.scatter_terrain_props", icon="PARTICLES")

        utilities = layout.box()
        utilities.label(text="Utilities", icon="TOOL_SETTINGS")
        row = utilities.row(align=True)
        row.operator("nex.join_terrain_chunks", icon="AUTOMERGE_ON")
        row.operator("nex.create_terrain_lighting", icon="LIGHT_SUN")
        row = utilities.row(align=True)
        op = row.operator("nex.clear_generated_terrain", text="Clear Terrain")
        op.clear_mode = "TERRAIN"
        op = row.operator("nex.clear_generated_terrain", text="Clear Scatter")
        op.clear_mode = "SCATTER"
        op = utilities.operator("nex.clear_generated_terrain", text="Clear Everything", icon="TRASH")
        op.clear_mode = "ALL"


CLASSES = (
    NEX_PG_TerrainSettings,
    NEX_OT_GenerateTerrain,
    NEX_OT_StylizeSelectedTerrain,
    NEX_OT_ReapplyTerrainMaterials,
    NEX_OT_UseGeneratedRockCollection,
    NEX_OT_ScatterTerrainProps,
    NEX_OT_JoinTerrainChunks,
    NEX_OT_CreateTerrainLighting,
    NEX_OT_ClearGeneratedTerrain,
    NEX_PT_StylizedTerrainPanel,
)


def register() -> None:
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.nex_terrain_settings = PointerProperty(type=NEX_PG_TerrainSettings)


def unregister() -> None:
    if hasattr(bpy.types.Scene, "nex_terrain_settings"):
        del bpy.types.Scene.nex_terrain_settings
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
