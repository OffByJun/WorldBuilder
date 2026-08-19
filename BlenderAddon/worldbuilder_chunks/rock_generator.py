from __future__ import annotations

bl_info = {
    "name": "Nex Stylized Rock Generator",
    "author": "Nex EngineWorks / Emiteat",
    "version": (0, 1, 0),
    "blender": (4, 2, 0),
    "location": "View3D > Sidebar > WorldBuilder",
    "description": "Generate stylized low-poly underwater rocks, clusters, terraces, pillars, and arches",
    "category": "Add Mesh",
}

import math
import random
from dataclasses import dataclass
from typing import Iterable

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Vector


ADDON_TAG = "nex_stylized_rock_generated"
OUTPUT_COLLECTION_NAME = "NexRock_Generated"


# -----------------------------------------------------------------------------
# Data
# -----------------------------------------------------------------------------


@dataclass(frozen=True)
class RockBuildParams:
    style: str
    seed: int
    width: float
    depth: float
    height: float
    sides: int
    levels: int
    irregularity: float
    decorations: bool
    decoration_amount: int
    sand_base: bool


ROCK_STYLES = [
    ("BOULDER", "Boulder", "Rounded, chunky boulder"),
    ("PILLAR", "Pillar", "Tall vertical rock pillar"),
    ("TERRACE", "Terrace", "Wide layered rock shelves"),
    ("JAGGED", "Jagged", "Sharp asymmetrical rock peak"),
    ("SLAB", "Slab", "Low, broad reef-like formation"),
    ("CLUSTER", "Cluster", "Several rocks grouped as one prop"),
    ("ARCH", "Arch", "Rock arch built from stylized stone chunks"),
]


# -----------------------------------------------------------------------------
# Utility
# -----------------------------------------------------------------------------


def _tag(id_block) -> None:
    id_block[ADDON_TAG] = True


def _safe_link_object(collection: bpy.types.Collection, obj: bpy.types.Object) -> None:
    if obj.name not in collection.objects:
        collection.objects.link(obj)


def _ensure_output_collection(context: bpy.types.Context) -> bpy.types.Collection:
    collection = bpy.data.collections.get(OUTPUT_COLLECTION_NAME)
    if collection is None:
        collection = bpy.data.collections.new(OUTPUT_COLLECTION_NAME)
        context.scene.collection.children.link(collection)
        _tag(collection)
    elif collection.name not in context.scene.collection.children:
        # The collection may exist in the file but not be linked to this scene.
        context.scene.collection.children.link(collection)
    return collection


def _remove_generated() -> int:
    removed = 0
    for obj in list(bpy.data.objects):
        if obj.get(ADDON_TAG):
            bpy.data.objects.remove(obj, do_unlink=True)
            removed += 1

    for collection in list(bpy.data.collections):
        if collection.get(ADDON_TAG) and collection.name != OUTPUT_COLLECTION_NAME:
            bpy.data.collections.remove(collection)

    output = bpy.data.collections.get(OUTPUT_COLLECTION_NAME)
    if output is not None and len(output.objects) == 0 and len(output.children) == 0:
        bpy.data.collections.remove(output)
    return removed


def _set_principled_material(
    name: str,
    color: tuple[float, float, float, float],
    roughness: float = 0.8,
    metallic: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name=name)
        material.use_nodes = True

    material.diffuse_color = color
    material.use_nodes = True
    nodes = material.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if bsdf is not None:
        base_color = bsdf.inputs.get("Base Color")
        if base_color is not None:
            base_color.default_value = color
        rough = bsdf.inputs.get("Roughness")
        if rough is not None:
            rough.default_value = roughness
        metal = bsdf.inputs.get("Metallic")
        if metal is not None:
            metal.default_value = metallic
    return material


def _get_palette() -> dict[str, bpy.types.Material]:
    return {
        "rock_base": _set_principled_material("NexRock_Rock_Base", (0.28, 0.37, 0.50, 1.0), 0.86),
        "rock_light": _set_principled_material("NexRock_Rock_Light", (0.48, 0.52, 0.58, 1.0), 0.82),
        "rock_dark": _set_principled_material("NexRock_Rock_Dark", (0.13, 0.21, 0.33, 1.0), 0.9),
        "sand": _set_principled_material("NexRock_Sand", (0.72, 0.58, 0.34, 1.0), 0.95),
        "seaweed": _set_principled_material("NexRock_Seaweed", (0.16, 0.48, 0.18, 1.0), 0.78),
        "seaweed_light": _set_principled_material("NexRock_Seaweed_Light", (0.42, 0.68, 0.16, 1.0), 0.76),
        "coral_pink": _set_principled_material("NexRock_Coral_Pink", (0.92, 0.21, 0.53, 1.0), 0.74),
        "coral_blue": _set_principled_material("NexRock_Coral_Blue", (0.08, 0.57, 0.92, 1.0), 0.72),
        "coral_yellow": _set_principled_material("NexRock_Coral_Yellow", (0.92, 0.63, 0.12, 1.0), 0.76),
        "coral_dark": _set_principled_material("NexRock_Coral_Dark", (0.11, 0.08, 0.12, 1.0), 0.9),
    }


def _new_mesh_object(
    name: str,
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    collection: bpy.types.Collection,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)

    obj = bpy.data.objects.new(name, mesh)
    _safe_link_object(collection, obj)
    _tag(mesh)
    _tag(obj)
    return obj


def _append_materials(obj: bpy.types.Object, materials: Iterable[bpy.types.Material]) -> None:
    for material in materials:
        obj.data.materials.append(material)


def _assign_rock_materials(obj: bpy.types.Object, seed: int) -> None:
    palette = _get_palette()
    _append_materials(obj, (palette["rock_base"], palette["rock_light"], palette["rock_dark"]))

    for poly in obj.data.polygons:
        poly.use_smooth = False
        face_rng = random.Random(seed * 100003 + poly.index * 9176)
        nz = poly.normal.z
        if nz > 0.42:
            poly.material_index = 1
        elif nz < -0.18 or face_rng.random() < 0.20:
            poly.material_index = 2
        else:
            poly.material_index = 0


def _assign_single_material(obj: bpy.types.Object, material: bpy.types.Material) -> None:
    obj.data.materials.append(material)
    for poly in obj.data.polygons:
        poly.material_index = 0
        poly.use_smooth = False


def _profile(style: str, t: float, rng: random.Random) -> float:
    if style == "PILLAR":
        value = 0.78 - 0.10 * t + 0.08 * math.sin(t * math.pi * 3.0)
    elif style == "TERRACE":
        step = min(4, int(t * 5.0))
        value = (1.00, 0.96, 0.80, 0.65, 0.48)[step]
    elif style == "JAGGED":
        value = 1.00 - 0.64 * t + 0.12 * math.sin(t * math.pi * 5.0)
    elif style == "SLAB":
        value = 1.00 - 0.22 * t + 0.08 * math.sin(t * math.pi * 2.0)
    else:  # BOULDER and internal cluster stones
        value = 0.86 + 0.23 * math.sin(t * math.pi) - 0.48 * (t * t)

    return max(0.22, value + rng.uniform(-0.025, 0.025))


def _build_rock_geometry(params: RockBuildParams, style_override: str | None = None):
    style = style_override or params.style
    rng = random.Random(params.seed)
    sides = max(5, params.sides)
    levels = max(3, params.levels)
    width = max(0.05, params.width)
    depth = max(0.05, params.depth)
    height = max(0.05, params.height)

    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    ring_phases: list[float] = []
    cumulative_phase = rng.uniform(-0.18, 0.18)

    for ring in range(levels + 1):
        t = ring / levels
        cumulative_phase += rng.uniform(-0.16, 0.16)
        ring_phases.append(cumulative_phase)
        profile = _profile(style, t, rng)

        # Uneven vertical spacing creates broader ledges and less mechanical rings.
        z_noise = rng.uniform(-0.035, 0.035) * height if ring not in (0, levels) else 0.0
        z = height * t + z_noise

        for side in range(sides):
            angle = (math.tau * side / sides) + ring_phases[ring]
            angular_noise = rng.uniform(-0.08, 0.08)
            radius_noise = 1.0 + rng.uniform(-params.irregularity, params.irregularity)

            # Low-frequency asymmetry keeps the silhouette from looking like a cone.
            lobe = 1.0 + 0.10 * math.sin(angle * 2.0 + params.seed * 0.37)
            x = math.cos(angle + angular_noise) * width * 0.5 * profile * radius_noise * lobe
            y = math.sin(angle + angular_noise) * depth * 0.5 * profile * radius_noise / lobe
            vertices.append((x, y, z))

    for ring in range(levels):
        for side in range(sides):
            nxt = (side + 1) % sides
            a = ring * sides + side
            b = ring * sides + nxt
            c = (ring + 1) * sides + nxt
            d = (ring + 1) * sides + side
            if (ring + side) % 2 == 0:
                faces.extend(((a, b, c), (a, c, d)))
            else:
                faces.extend(((a, b, d), (b, c, d)))

    bottom_center = len(vertices)
    vertices.append((0.0, 0.0, -height * 0.025))
    top_center = len(vertices)
    vertices.append((
        rng.uniform(-0.05, 0.05) * width,
        rng.uniform(-0.05, 0.05) * depth,
        height * 1.015,
    ))

    for side in range(sides):
        nxt = (side + 1) % sides
        faces.append((bottom_center, nxt, side))
        top_a = levels * sides + side
        top_b = levels * sides + nxt
        faces.append((top_center, top_a, top_b))

    return vertices, faces


def _create_rock_object(
    params: RockBuildParams,
    collection: bpy.types.Collection,
    name: str,
    parent: bpy.types.Object,
    style_override: str | None = None,
    location: tuple[float, float, float] = (0.0, 0.0, 0.0),
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0),
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    seed_offset: int = 0,
) -> bpy.types.Object:
    local_params = RockBuildParams(
        style=style_override or params.style,
        seed=params.seed + seed_offset,
        width=params.width,
        depth=params.depth,
        height=params.height,
        sides=params.sides,
        levels=params.levels,
        irregularity=params.irregularity,
        decorations=params.decorations,
        decoration_amount=params.decoration_amount,
        sand_base=params.sand_base,
    )
    vertices, faces = _build_rock_geometry(local_params, style_override)
    obj = _new_mesh_object(name, vertices, faces, collection)
    _assign_rock_materials(obj, local_params.seed)
    obj.parent = parent
    obj.location = location
    obj.scale = scale
    obj.rotation_euler = rotation
    return obj


def _create_sand_base(
    params: RockBuildParams,
    collection: bpy.types.Collection,
    parent: bpy.types.Object,
    radius_multiplier: float = 0.75,
) -> bpy.types.Object:
    rng = random.Random(params.seed + 991)
    segments = max(10, params.sides + 4)
    rx = params.width * radius_multiplier
    ry = params.depth * radius_multiplier
    thickness = max(0.03, min(params.height * 0.045, 0.12))

    vertices: list[tuple[float, float, float]] = []
    for z, scale in ((0.0, 1.0), (-thickness, 0.92)):
        for i in range(segments):
            angle = math.tau * i / segments
            wobble = 1.0 + rng.uniform(-0.13, 0.13)
            vertices.append((math.cos(angle) * rx * wobble * scale, math.sin(angle) * ry * wobble * scale, z))

    top_center = len(vertices)
    vertices.append((0.0, 0.0, 0.015))
    bottom_center = len(vertices)
    vertices.append((0.0, 0.0, -thickness))

    faces: list[tuple[int, ...]] = []
    for i in range(segments):
        nxt = (i + 1) % segments
        faces.append((top_center, i, nxt))
        faces.append((bottom_center, segments + nxt, segments + i))
        faces.append((i, segments + i, segments + nxt, nxt))

    obj = _new_mesh_object("NexRock_SandBase", vertices, faces, collection)
    _assign_single_material(obj, _get_palette()["sand"])
    obj.parent = parent
    obj.location.z = -0.025
    return obj


def _create_lowpoly_tube(
    name: str,
    collection: bpy.types.Collection,
    parent: bpy.types.Object,
    location: tuple[float, float, float],
    radius: float,
    height: float,
    material: bpy.types.Material,
    seed: int,
    sides: int = 6,
    lean: tuple[float, float] = (0.0, 0.0),
    dark_cap: bool = False,
) -> bpy.types.Object:
    rng = random.Random(seed)
    vertices: list[tuple[float, float, float]] = []
    top_scale = rng.uniform(0.62, 0.86)
    for z, ring_scale, offset_scale in ((0.0, 1.0, 0.0), (height, top_scale, 1.0)):
        for i in range(sides):
            angle = math.tau * i / sides + rng.uniform(-0.04, 0.04)
            vertices.append((
                math.cos(angle) * radius * ring_scale + lean[0] * offset_scale,
                math.sin(angle) * radius * ring_scale + lean[1] * offset_scale,
                z,
            ))

    bottom_center = len(vertices)
    vertices.append((0.0, 0.0, 0.0))
    top_center = len(vertices)
    vertices.append((lean[0], lean[1], height))

    faces: list[tuple[int, ...]] = []
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append((i, nxt, sides + nxt, sides + i))
        faces.append((bottom_center, nxt, i))
        faces.append((top_center, sides + i, sides + nxt))

    obj = _new_mesh_object(name, vertices, faces, collection)
    palette = _get_palette()
    _append_materials(obj, (material, palette["coral_dark"]))
    for poly in obj.data.polygons:
        poly.use_smooth = False
        poly.material_index = 1 if dark_cap and poly.center.z > height * 0.92 else 0
    obj.parent = parent
    obj.location = location
    obj.rotation_euler.z = rng.uniform(-math.pi, math.pi)
    return obj


def _decorate_base(
    params: RockBuildParams,
    collection: bpy.types.Collection,
    parent: bpy.types.Object,
    radius_multiplier: float = 0.66,
) -> None:
    if not params.decorations or params.decoration_amount <= 0:
        return

    palette = _get_palette()
    rng = random.Random(params.seed + 4127)
    count = max(1, params.decoration_amount)
    rx = params.width * radius_multiplier
    ry = params.depth * radius_multiplier

    coral_materials = (palette["coral_pink"], palette["coral_blue"], palette["coral_yellow"])

    # Coral clusters.
    for i in range(count):
        angle = rng.uniform(0.0, math.tau)
        distance = rng.uniform(0.62, 1.0)
        anchor = Vector((math.cos(angle) * rx * distance, math.sin(angle) * ry * distance, 0.0))
        material = coral_materials[i % len(coral_materials)]
        tubes = rng.randint(2, 4)
        for tube in range(tubes):
            local_angle = rng.uniform(0.0, math.tau)
            local_distance = rng.uniform(0.0, params.width * 0.08)
            location = (
                anchor.x + math.cos(local_angle) * local_distance,
                anchor.y + math.sin(local_angle) * local_distance,
                0.0,
            )
            h = params.height * rng.uniform(0.075, 0.15)
            r = params.width * rng.uniform(0.022, 0.042)
            _create_lowpoly_tube(
                f"NexRock_Coral_{i}_{tube}", collection, parent, location, r, h, material,
                params.seed + i * 71 + tube * 13,
                sides=5,
                lean=(rng.uniform(-r, r), rng.uniform(-r, r)),
                dark_cap=True,
            )

    # Seaweed tufts use narrow tapered prisms, intentionally simple and low-poly.
    seaweed_count = max(2, count + 1)
    for i in range(seaweed_count):
        angle = rng.uniform(0.0, math.tau)
        distance = rng.uniform(0.55, 1.0)
        base = Vector((math.cos(angle) * rx * distance, math.sin(angle) * ry * distance, 0.0))
        stems = rng.randint(2, 5)
        for stem in range(stems):
            h = params.height * rng.uniform(0.11, 0.27)
            r = params.width * rng.uniform(0.010, 0.022)
            offset = Vector((rng.uniform(-r * 2.0, r * 2.0), rng.uniform(-r * 2.0, r * 2.0), 0.0))
            material = palette["seaweed_light"] if (stem + i) % 3 == 0 else palette["seaweed"]
            _create_lowpoly_tube(
                f"NexRock_Seaweed_{i}_{stem}", collection, parent,
                tuple(base + offset), r, h, material,
                params.seed + 8000 + i * 37 + stem,
                sides=4,
                lean=(rng.uniform(-r * 3.0, r * 3.0), rng.uniform(-r * 3.0, r * 3.0)),
                dark_cap=False,
            )


def _create_root(collection: bpy.types.Collection, name: str, location=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    root = bpy.data.objects.new(name, None)
    _safe_link_object(collection, root)
    _tag(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.5
    root.location = location
    return root


def _build_asset(
    params: RockBuildParams,
    collection: bpy.types.Collection,
    name: str,
    location=(0.0, 0.0, 0.0),
) -> bpy.types.Object:
    root = _create_root(collection, name, location)

    if params.sand_base:
        _create_sand_base(params, collection, root, 0.82 if params.style != "ARCH" else 1.15)

    if params.style == "CLUSTER":
        offsets = (
            (-params.width * 0.22, 0.0, 0.0, 0.82, 0.98, 0.90),
            ( params.width * 0.20, params.depth * 0.08, 0.0, 0.70, 0.76, 0.72),
            ( 0.02, -params.depth * 0.20, 0.0, 0.56, 0.64, 0.55),
        )
        for i, (x, y, z, sx, sy, sz) in enumerate(offsets):
            _create_rock_object(
                params, collection, f"{name}_Stone_{i}", root,
                style_override="BOULDER",
                location=(x, y, z),
                scale=(sx, sy, sz),
                rotation=(0.0, 0.0, i * 0.85),
                seed_offset=i * 211,
            )

    elif params.style == "ARCH":
        arch_radius = params.width * 0.56
        stone_count = max(7, params.sides)
        for i in range(stone_count):
            t = i / (stone_count - 1)
            angle = math.pi * (1.0 - t)
            x = math.cos(angle) * arch_radius
            z = math.sin(angle) * params.height * 0.78
            scale = 0.34 + 0.06 * math.sin(math.pi * t)
            _create_rock_object(
                params, collection, f"{name}_ArchStone_{i}", root,
                style_override="BOULDER",
                location=(x, 0.0, z),
                scale=(scale, scale * 0.92, scale * 0.72),
                rotation=(0.0, -(angle - math.pi * 0.5), i * 0.13),
                seed_offset=i * 127,
            )

        # Thicker supports make the arch readable and less fragile.
        for side in (-1.0, 1.0):
            for level in range(2):
                _create_rock_object(
                    params, collection, f"{name}_Support_{side}_{level}", root,
                    style_override="BOULDER",
                    location=(side * arch_radius, 0.0, level * params.height * 0.18),
                    scale=(0.48, 0.52, 0.40),
                    rotation=(0.0, 0.0, side * 0.17 + level * 0.21),
                    seed_offset=4000 + level * 79 + int(side * 19),
                )
    else:
        _create_rock_object(params, collection, f"{name}_Rock", root)

    _decorate_base(params, collection, root, 0.80 if params.style == "ARCH" else 0.68)
    return root


def _params_from_settings(settings: "NEXROCK_Settings", style: str | None = None, seed: int | None = None,
                          width: float | None = None, depth: float | None = None,
                          height: float | None = None) -> RockBuildParams:
    chosen_style = style or settings.style
    chosen_height = height if height is not None else settings.height
    if chosen_style == "SLAB" and height is None:
        chosen_height *= 0.48
    elif chosen_style == "PILLAR" and height is None:
        chosen_height *= 1.45
    elif chosen_style == "JAGGED" and height is None:
        chosen_height *= 1.18

    return RockBuildParams(
        style=chosen_style,
        seed=seed if seed is not None else settings.seed,
        width=width if width is not None else settings.width,
        depth=depth if depth is not None else settings.depth,
        height=chosen_height,
        sides=settings.sides,
        levels=settings.levels,
        irregularity=settings.irregularity,
        decorations=settings.decorations,
        decoration_amount=settings.decoration_amount,
        sand_base=settings.sand_base,
    )


def _select_only(context: bpy.types.Context, obj: bpy.types.Object) -> None:
    for selected in context.selected_objects:
        selected.select_set(False)
    obj.select_set(True)
    context.view_layer.objects.active = obj


# -----------------------------------------------------------------------------
# Properties
# -----------------------------------------------------------------------------


class NEXROCK_Settings(PropertyGroup):
    style: EnumProperty(name="Rock Type", items=ROCK_STYLES, default="BOULDER")
    seed: IntProperty(name="Seed", default=1, min=0, max=999999)
    width: FloatProperty(name="Width", default=3.2, min=0.2, max=100.0, unit="LENGTH")
    depth: FloatProperty(name="Depth", default=2.8, min=0.2, max=100.0, unit="LENGTH")
    height: FloatProperty(name="Height", default=3.0, min=0.2, max=100.0, unit="LENGTH")
    sides: IntProperty(name="Sides", default=7, min=5, max=16)
    levels: IntProperty(name="Vertical Levels", default=6, min=3, max=16)
    irregularity: FloatProperty(name="Irregularity", default=0.20, min=0.0, max=0.55, subtype="FACTOR")
    decorations: BoolProperty(name="Coral and Seaweed", default=True)
    decoration_amount: IntProperty(name="Decoration Amount", default=4, min=0, max=20)
    sand_base: BoolProperty(name="Sand Base", default=True)
    clear_before_sheet: BoolProperty(name="Clear Before Sheet", default=True)


# -----------------------------------------------------------------------------
# Operators
# -----------------------------------------------------------------------------


class NEXROCK_OT_generate(Operator):
    bl_idname = "nexrock.generate"
    bl_label = "Generate Rock Asset"
    bl_description = "Generate one stylized rock asset at the 3D cursor"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.nexrock_settings
        collection = _ensure_output_collection(context)
        params = _params_from_settings(settings)
        location = tuple(context.scene.cursor.location)
        root = _build_asset(params, collection, f"NexRock_{settings.style}_{settings.seed}", location)
        _select_only(context, root)
        self.report({"INFO"}, f"Generated {settings.style.lower()} rock asset")
        return {"FINISHED"}


class NEXROCK_OT_randomize_seed(Operator):
    bl_idname = "nexrock.randomize_seed"
    bl_label = "Randomize Seed"
    bl_description = "Choose a new random seed"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        context.scene.nexrock_settings.seed = random.randint(0, 999999)
        return {"FINISHED"}


class NEXROCK_OT_generate_sheet(Operator):
    bl_idname = "nexrock.generate_sheet"
    bl_label = "Generate 12-Rock Sheet"
    bl_description = "Generate a 4 by 3 sheet of varied stylized rock assets"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.nexrock_settings
        if settings.clear_before_sheet:
            _remove_generated()
        collection = _ensure_output_collection(context)

        base = Vector(context.scene.cursor.location)
        spacing_x = max(settings.width * 2.3, 7.0)
        spacing_y = max(settings.depth * 2.6, 7.0)

        variants = [
            ("BOULDER", 0.80, 0.82, 0.70),
            ("CLUSTER", 1.05, 1.00, 0.95),
            ("SLAB", 1.10, 1.00, 0.55),
            ("TERRACE", 1.18, 1.05, 0.80),
            ("PILLAR", 0.65, 0.62, 1.38),
            ("BOULDER", 1.12, 1.00, 1.05),
            ("JAGGED", 0.88, 0.80, 1.25),
            ("PILLAR", 0.78, 0.72, 1.58),
            ("TERRACE", 1.28, 1.08, 0.62),
            ("ARCH", 1.25, 0.80, 1.05),
            ("SLAB", 1.30, 1.12, 0.48),
            ("CLUSTER", 0.94, 0.92, 0.84),
        ]

        roots: list[bpy.types.Object] = []
        for index, (style, width_mul, depth_mul, height_mul) in enumerate(variants):
            row = index // 4
            col = index % 4
            location = base + Vector(((col - 1.5) * spacing_x, -(row - 1.0) * spacing_y, 0.0))
            params = _params_from_settings(
                settings,
                style=style,
                seed=settings.seed + index * 137,
                width=settings.width * width_mul,
                depth=settings.depth * depth_mul,
                height=settings.height * height_mul,
            )
            roots.append(_build_asset(params, collection, f"NexRock_Sheet_{index + 1:02d}_{style}", tuple(location)))

        if roots:
            _select_only(context, roots[0])
        self.report({"INFO"}, "Generated a 12-rock asset sheet")
        return {"FINISHED"}


class NEXROCK_OT_clear(Operator):
    bl_idname = "nexrock.clear_generated"
    bl_label = "Clear Generated"
    bl_description = "Delete objects created by Nex Stylized Rock Generator"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        removed = _remove_generated()
        self.report({"INFO"}, f"Removed {removed} generated objects")
        return {"FINISHED"}


class NEXROCK_OT_preview_lights(Operator):
    bl_idname = "nexrock.preview_lights"
    bl_label = "Add Preview Lights"
    bl_description = "Add a simple cool underwater-style preview lighting rig"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        collection = _ensure_output_collection(context)
        cursor = Vector(context.scene.cursor.location)

        specs = (
            ("NexRock_Key", "AREA", cursor + Vector((4.5, -4.0, 7.0)), 1100.0, (0.68, 0.88, 1.0), 5.0),
            ("NexRock_Fill", "AREA", cursor + Vector((-4.0, 1.5, 3.5)), 650.0, (0.20, 0.55, 1.0), 4.0),
            ("NexRock_Rim", "AREA", cursor + Vector((1.0, 4.5, 5.0)), 850.0, (0.45, 1.0, 0.72), 3.0),
        )

        for name, light_type, location, energy, color, size in specs:
            old = bpy.data.objects.get(name)
            if old is not None:
                bpy.data.objects.remove(old, do_unlink=True)
            data = bpy.data.lights.new(name=name, type=light_type)
            data.energy = energy
            data.color = color
            data.shape = "DISK"
            data.size = size
            obj = bpy.data.objects.new(name, data)
            collection.objects.link(obj)
            obj.location = location
            direction = cursor - obj.location
            obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
            _tag(data)
            _tag(obj)

        world = context.scene.world
        if world is None:
            world = bpy.data.worlds.new("NexRock_World")
            context.scene.world = world
        world.use_nodes = True
        background = world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (0.015, 0.055, 0.095, 1.0)
            background.inputs["Strength"].default_value = 0.28

        self.report({"INFO"}, "Added underwater-style preview lights")
        return {"FINISHED"}


# -----------------------------------------------------------------------------
# UI
# -----------------------------------------------------------------------------


class NEXROCK_PT_main(Panel):
    bl_label = "Stylized Rock Generator"
    bl_idname = "NEXROCK_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        settings = context.scene.nexrock_settings

        layout.prop(settings, "style")

        seed_row = layout.row(align=True)
        seed_row.prop(settings, "seed")
        seed_row.operator("nexrock.randomize_seed", text="", icon="FILE_REFRESH")

        size_box = layout.box()
        size_box.label(text="Shape")
        size_box.prop(settings, "width")
        size_box.prop(settings, "depth")
        size_box.prop(settings, "height")
        size_box.prop(settings, "sides")
        size_box.prop(settings, "levels")
        size_box.prop(settings, "irregularity")

        detail_box = layout.box()
        detail_box.label(text="Environment Details")
        detail_box.prop(settings, "sand_base")
        detail_box.prop(settings, "decorations")
        sub = detail_box.column()
        sub.enabled = settings.decorations
        sub.prop(settings, "decoration_amount")

        layout.operator("nexrock.generate", icon="MESH_ICOSPHERE")

        sheet_box = layout.box()
        sheet_box.label(text="Asset Sheet")
        sheet_box.prop(settings, "clear_before_sheet")
        sheet_box.operator("nexrock.generate_sheet", icon="OUTLINER_COLLECTION")

        layout.separator()
        layout.operator("nexrock.preview_lights", icon="LIGHT_AREA")
        layout.operator("nexrock.clear_generated", icon="TRASH")

        info = layout.box()
        info.label(text="Flat shading + faceted geometry")
        info.label(text="Materials are intentionally simple")


CLASSES = (
    NEXROCK_Settings,
    NEXROCK_OT_generate,
    NEXROCK_OT_randomize_seed,
    NEXROCK_OT_generate_sheet,
    NEXROCK_OT_clear,
    NEXROCK_OT_preview_lights,
    NEXROCK_PT_main,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.nexrock_settings = PointerProperty(type=NEXROCK_Settings)


def unregister():
    if hasattr(bpy.types.Scene, "nexrock_settings"):
        del bpy.types.Scene.nexrock_settings
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
