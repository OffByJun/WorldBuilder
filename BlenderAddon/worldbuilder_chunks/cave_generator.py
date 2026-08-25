"""Deterministic procedural cave networks — the Blender authoring twin of Unity's CaveField.

Builds closed tunnel tubes (parallel-transport frames, vertical squash) with room bulges,
matching the Limestone/LavaTube/FloodedGrotto/AbyssalNetwork presets used by Terrain Forge.
Generated meshes carry the ``WB_BIOME_CAVE`` float attribute so exported chunks arrive in
Unity already classified as cave geometry.
"""
from __future__ import annotations

import math
import random
import uuid

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Vector

from . import localization, rock_generator

TAG = "wb_cave_generated"
SCHEMA_VERSION = 1
COLLECTION_NAME = "WB_CAVES"
BIOME_ATTRIBUTE = "WB_BIOME_CAVE"

PRESETS = (
    ("LIMESTONE", "Limestone Caves", "Winding walkable passages with occasional chambers"),
    ("LAVA_TUBES", "Lava Tubes", "Long flat-roofed tubes, few chambers"),
    ("FLOODED_GROTTO", "Flooded Grotto", "Low chambers below the waterline band"),
    ("ABYSSAL_NETWORK", "Abyssal Network", "Broad deep tunnel web with large rooms"),
)

PRESET_DEFAULTS = {
    "LIMESTONE": dict(radius=2.2, squash=0.55, winding=0.55, tunnel_count=3, segments=48,
                      step_length=4.0, radius_variance=0.35, room_count=4, room_scale=2.1),
    "LAVA_TUBES": dict(radius=2.6, squash=0.32, winding=0.75, tunnel_count=2, segments=56,
                       step_length=5.0, radius_variance=0.22, room_count=1, room_scale=1.6),
    "FLOODED_GROTTO": dict(radius=3.4, squash=0.62, winding=0.45, tunnel_count=2, segments=40,
                           step_length=4.5, radius_variance=0.4, room_count=6, room_scale=2.6),
    "ABYSSAL_NETWORK": dict(radius=3.8, squash=0.48, winding=0.65, tunnel_count=4, segments=64,
                            step_length=5.5, radius_variance=0.45, room_count=7, room_scale=2.9),
}

_PRESET_INDEX = {name: index for index, (name, _, _) in enumerate(PRESETS)}


def _stable_id(seed, suffix):
    return uuid.uuid5(uuid.NAMESPACE_URL, f"worldbuilder:cave:{seed}:{suffix}").hex


def _apply_preset(settings):
    values = PRESET_DEFAULTS.get(settings.preset)
    if not values:
        return
    for key, value in values.items():
        setattr(settings, key, value)


def _on_preset_changed(settings, _context):
    _apply_preset(settings)


def _generation_centre():
    cursor = getattr(bpy.context.scene, "cursor", None)
    return cursor.location.copy() if cursor is not None else Vector((0.0, 0.0, 0.0))


def _tunnel_path(settings, rng, index):
    """Random-walk worm inside axis-aligned bounds centred on the generation centre."""
    centre = _generation_centre()
    half_w = max(settings.width * 0.5 - settings.radius - 0.5, 0.5)
    half_d = max(settings.depth * 0.5 - settings.radius - 0.5, 0.5)
    half_h = max(settings.height * 0.5 - settings.radius - 0.5, 0.5)

    point = Vector((
        rng.uniform(-half_w, half_w),
        rng.uniform(-half_d, half_d),
        rng.uniform(-half_h, half_h),
    )) + centre

    yaw = math.tau * ((index * 0.6180339887) % 1.0) + rng.uniform(0.0, math.tau)
    pitch = rng.uniform(-0.25, 0.25)
    direction = Vector((math.cos(yaw), math.sin(yaw), pitch)).normalized()

    points = [point.copy()]
    radii_profile = []
    segment_count = max(8, int(settings.segments))
    for step in range(segment_count):
        turn_yaw = (rng.random() - 0.5) * math.tau * (1.0 - settings.winding * 0.85)
        turn_pitch = (rng.random() - 0.5) * 0.6 * (1.0 - settings.winding * 0.5)
        turn = Vector((math.cos(turn_yaw), math.sin(turn_yaw), turn_pitch))
        direction = (direction + turn * 0.35).normalized()

        point = point + direction * max(0.5, settings.step_length)
        # Soft wall response: clamp back into bounds and reflect the offending axis so
        # tunnels bounce around the volume instead of hugging one wall.
        if abs(point.x - centre.x) > half_w:
            point.x = centre.x + math.copysign(half_w, point.x - centre.x)
            direction.x *= -1.0
        if abs(point.y - centre.y) > half_d:
            point.y = centre.y + math.copysign(half_d, point.y - centre.y)
            direction.y *= -1.0
        if abs(point.z - centre.z) > half_h:
            point.z = centre.z + math.copysign(half_h, point.z - centre.z)
            direction.z *= -1.0

        points.append(point.copy())
        t = step / max(1, segment_count - 1)
        wave = 0.5 + 0.5 * math.sin(t * math.tau * 2.7 + index)
        radii_profile.append(1.0 + settings.radius_variance * (wave - 0.5) * 2.0)
    return points, radii_profile


def _room_multipliers(segment_count, room_nodes, room_scale, falloff_rings):
    multipliers = [1.0] * segment_count
    if room_scale <= 1.0 or not room_nodes:
        return multipliers
    span = max(1, int(falloff_rings))
    for node in room_nodes:
        for offset in range(-span, span + 1):
            ring = node + offset
            if 0 <= ring < segment_count:
                weight = 1.0 - abs(offset) / (span + 1.0)
                boost = 1.0 + (room_scale - 1.0) * (weight * weight * (3.0 - 2.0 * weight))
                multipliers[ring] = max(multipliers[ring], boost)
    return multipliers


class TubeBuilder:
    """Closed tube mesh with twist-free parallel-transport rings and fan caps."""

    def __init__(self):
        self.vertices = []
        self.faces = []

    def add(self, points, radii, sides, squash):
        sides = max(5, int(sides))
        count = len(points)
        prev_up = None
        prev_side = None

        for ring, (point, radius) in enumerate(zip(points, radii)):
            if ring + 1 < count:
                edge = points[ring + 1] - point
            else:
                edge = point - points[ring - 1]
            if edge.length <= 1e-6:
                edge = Vector((1.0, 0.0, 0.0))
            tangent = edge.normalized()

            if prev_up is None:
                reference = (Vector((0.0, 0.0, 1.0)) if abs(tangent.z) < 0.95
                             else Vector((1.0, 0.0, 0.0)))
                side = tangent.cross(reference).normalized()
                up = side.cross(tangent).normalized()
            else:
                # Parallel transport: carry the old basis along the new tangent.
                up = prev_up - tangent * prev_up.dot(tangent)
                if up.length <= 1e-5:
                    reference = (Vector((0.0, 0.0, 1.0)) if abs(tangent.z) < 0.95
                                 else Vector((1.0, 0.0, 0.0)))
                    up = reference - tangent * reference.dot(tangent)
                up.normalize()
                side = tangent.cross(up).normalized()

            prev_up = up
            prev_side = side

            start = len(self.vertices)
            for s in range(sides):
                angle = math.tau * s / sides
                local = (math.cos(angle) * side + math.sin(angle) * up) * radius
                local.z *= squash
                self.vertices.append(tuple(point + local))

            if ring == 0:
                continue
            previous_start = start - sides
            for s in range(sides):
                nxt = (s + 1) % sides
                self.faces.append((
                    previous_start + s,
                    previous_start + nxt,
                    start + nxt,
                    start + s,
                ))

        for ring_index in (0, count - 1):
            base = ring_index * sides
            centre_vertex = len(self.vertices)
            self.vertices.append(tuple(points[ring_index]))
            for s in range(sides):
                nxt = (s + 1) % sides
                if ring_index == 0:
                    self.faces.append((centre_vertex, base + nxt, base + s))
                else:
                    self.faces.append((centre_vertex, base + s, base + nxt))
        _ = prev_side  # kept symmetric for readability of the transport block


def _cave_materials():
    palette = rock_generator._get_palette()
    dark = bpy.data.materials.get("WB_Cave_Rock_Dark")
    if dark is None:
        dark = bpy.data.materials.new("WB_Cave_Rock_Dark")
        dark.diffuse_color = (0.09, 0.075, 0.10, 1.0)
        dark.use_nodes = True
        node = dark.node_tree.nodes.get("Principled BSDF")
        if node:
            node.inputs["Base Color"].default_value = (0.09, 0.075, 0.10, 1.0)
            node.inputs["Roughness"].default_value = 0.94
    return [palette.get("rock_base") or palette["rock"], dark]


def clear_generated(_context=None):
    removed = 0
    for obj in list(bpy.data.objects):
        if obj.get(TAG):
            bpy.data.objects.remove(obj, do_unlink=True)
            removed += 1
    collection = bpy.data.collections.get(COLLECTION_NAME)
    if collection is not None and len(collection.objects) == 0:
        bpy.data.collections.remove(collection)
    return removed


def generate(context, settings):
    rng = random.Random(int(settings.seed) * 7917 + _PRESET_INDEX.get(settings.preset, 0) * 104729)

    collection = bpy.data.collections.get(COLLECTION_NAME)
    if collection is None:
        collection = bpy.data.collections.new(COLLECTION_NAME)
        context.scene.collection.children.link(collection)
    collection[TAG] = True
    collection["wb_cave_schema_version"] = SCHEMA_VERSION
    collection["wb_cave_preset"] = settings.preset

    materials = _cave_materials()
    created = []
    for index in range(max(1, int(settings.tunnel_count))):
        points, profile = _tunnel_path(settings, rng, index)
        pool = range(4, max(5, len(points) - 4))
        room_nodes = sorted(rng.sample(pool, min(int(settings.room_count), len(pool))))
        multipliers = _room_multipliers(len(points), room_nodes, settings.room_scale, 4)

        radii = []
        for ring, factor in enumerate(profile):
            radius = settings.radius * factor * multipliers[ring]
            taper = min(1.0, (ring + 1) / 6.0, (len(points) - ring) / 6.0)
            radii.append(max(0.35, radius * (0.55 + 0.45 * taper)))

        builder = TubeBuilder()
        builder.add(points, radii, settings.ring_sides, max(0.05, settings.squash))

        name = f"CaveNetwork_{settings.preset.title()}_{index + 1:02d}"
        mesh = bpy.data.meshes.new(name + "Mesh")
        mesh.from_pydata(builder.vertices, [], builder.faces)
        mesh.update()
        mesh.validate(verbose=False)

        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        for material in materials:
            mesh.materials.append(material)

        attribute = mesh.attributes.get(BIOME_ATTRIBUTE)
        if attribute is None:
            attribute = mesh.attributes.new(name=BIOME_ATTRIBUTE, type="FLOAT", domain="POINT")
        for item in attribute.data:
            item.value = 1.0

        obj[TAG] = True
        obj["wb_role"] = "TERRAIN"
        obj["wb_shader_family"] = "CAVE"
        obj["wb_id"] = _stable_id(settings.seed, f"{settings.preset}.{index}")
        created.append(obj)

    if created:
        context.view_layer.objects.active = created[0]
        for obj in created:
            obj.select_set(True)
    return collection, created


class WBCaveSettings(PropertyGroup):
    preset: EnumProperty(name="Preset", items=PRESETS, default="LIMESTONE",
                         update=_on_preset_changed)
    seed: IntProperty(name="Seed", default=911, min=0, max=999999)
    width: FloatProperty(name="Width", default=96, min=16, max=1024, unit="LENGTH")
    depth: FloatProperty(name="Depth", default=96, min=16, max=1024, unit="LENGTH")
    height: FloatProperty(name="Height", default=32, min=8, max=512, unit="LENGTH")
    tunnel_count: IntProperty(name="Tunnels", default=3, min=1, max=12)
    segments: IntProperty(name="Segments", default=48, min=8, max=256)
    step_length: FloatProperty(name="Step Length", default=4.0, min=1.0, max=24.0, unit="LENGTH")
    radius: FloatProperty(name="Radius", default=2.2, min=0.5, max=16.0, unit="LENGTH")
    radius_variance: FloatProperty(name="Radius Variance", default=0.35, min=0.0, max=0.9,
                                   subtype="FACTOR")
    squash: FloatProperty(name="Vertical Squash", default=0.55, min=0.05, max=1.0,
                          subtype="FACTOR")
    winding: FloatProperty(name="Winding", default=0.55, min=0.0, max=1.0, subtype="FACTOR")
    room_count: IntProperty(name="Rooms", default=4, min=0, max=32)
    room_scale: FloatProperty(name="Room Scale", default=2.1, min=1.0, max=6.0)
    ring_sides: IntProperty(name="Ring Sides", default=10, min=5, max=24)
    replace_existing: BoolProperty(name="Replace Existing", default=True)


class WB_OT_generate_caves(Operator):
    bl_idname = "worldbuilder.generate_caves"
    bl_label = "Generate Cave Network"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.worldbuilder_cave
        try:
            if settings.replace_existing:
                clear_generated(context)
            collection, created = generate(context, settings)
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, f"Generated {len(created)} cave tunnel(s) into {collection.name}")
        return {"FINISHED"}


class WB_OT_randomize_cave_seed(Operator):
    bl_idname = "worldbuilder.randomize_cave_seed"
    bl_label = "Randomize Cave Seed"
    bl_options = {"UNDO"}

    def execute(self, context):
        context.scene.worldbuilder_cave.seed = random.randint(0, 999999)
        return {"FINISHED"}


class WB_OT_clear_caves(Operator):
    bl_idname = "worldbuilder.clear_generated_caves"
    bl_label = "Clear Generated Caves"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        removed = clear_generated(context)
        self.report({"INFO"}, f"Removed {removed} cave object(s)")
        return {"FINISHED"}


class WB_PT_cave_generator(Panel):
    bl_label = "Cave Network Builder"
    bl_idname = "WB_PT_cave_generator"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        settings = context.scene.worldbuilder_cave
        korean = localization.language(context.scene) == "KO"

        header = layout.column()
        header.label(text="동굴 네트워크 제작기" if korean else "Cave Network Builder", icon="SPHERE")

        row = header.row(align=True)
        row.prop(settings, "seed", text="시드" if korean else "Seed")
        row.operator("worldbuilder.randomize_cave_seed", text="", icon="FILE_REFRESH")

        shape = layout.box()
        shape.prop(settings, "preset", text="프리셋" if korean else "Preset")
        shape.prop(settings, "width", text="너비" if korean else "Width")
        shape.prop(settings, "depth", text="깊이" if korean else "Depth")
        shape.prop(settings, "height", text="높이" if korean else "Height")

        tunnels = layout.box()
        tunnels.label(text="터널" if korean else "Tunnels")
        tunnels.prop(settings, "tunnel_count", text="개수" if korean else "Count")
        tunnels.prop(settings, "segments", text="세그먼트" if korean else "Segments")
        tunnels.prop(settings, "step_length", text="보폭" if korean else "Step Length")
        tunnels.prop(settings, "winding", text="굽이" if korean else "Winding")

        profile = layout.box()
        profile.label(text="단면" if korean else "Profile")
        profile.prop(settings, "radius", text="반지름" if korean else "Radius")
        profile.prop(settings, "radius_variance",
                     text="반지름 변화" if korean else "Radius Variance")
        profile.prop(settings, "squash", text="수직 스쿼시" if korean else "Vertical Squash")
        profile.prop(settings, "ring_sides", text="링 분할" if korean else "Ring Sides")

        rooms = layout.box()
        rooms.label(text="케버른 룸" if korean else "Cavern Rooms")
        rooms.prop(settings, "room_count", text="개수" if korean else "Count")
        rooms.prop(settings, "room_scale", text="크기 배율" if korean else "Scale")

        layout.prop(settings, "replace_existing",
                    text="기존 생성물 교체" if korean else "Replace Existing")
        layout.operator("worldbuilder.generate_caves",
                        text="동굴 네트워크 생성" if korean else "Generate Cave Network",
                        icon="MOD_BUILD")
        layout.operator("worldbuilder.clear_generated_caves",
                        text="생성물 삭제" if korean else "Clear Generated Caves", icon="TRASH")
        layout.label(text="정점에 WB_BIOME_CAVE 속성이 부여됩니다."
                     if korean else "Vertices carry the WB_BIOME_CAVE attribute.", icon="INFO")


CLASSES = (
    WBCaveSettings,
    WB_OT_generate_caves,
    WB_OT_randomize_cave_seed,
    WB_OT_clear_caves,
    WB_PT_cave_generator,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_cave = PointerProperty(type=WBCaveSettings)


def unregister():
    if hasattr(bpy.types.Scene, "worldbuilder_cave"):
        del bpy.types.Scene.worldbuilder_cave
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
