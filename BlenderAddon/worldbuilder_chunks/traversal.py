"""Player-scale traversal probes.

Answers the questions slope heat maps cannot: can the player stand here, fit under that
ceiling, or swim through that gap. Probes are cast against the evaluated scene, so
generated terrain, spline meshes, and placed structures all participate.
"""

import bpy
import gpu
from bpy.props import EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup
from gpu_extras.batch import batch_for_shader
from mathutils import Vector

from . import analysis, contract, layers, localization, state, water

_results = []
_handle = None
_EPSILON = 1e-3
_BAND_MARGIN = 0.05
_MAX_SURFACES = 16

STATUS_COLORS = {
    analysis.OK: (0.15, 0.90, 0.35, 0.85),
    analysis.NO_GROUND: (0.35, 0.35, 0.40, 0.55),
    analysis.STEEP: (1.00, 0.65, 0.10, 0.90),
    analysis.LOW_CEILING: (1.00, 0.20, 0.15, 0.95),
    analysis.NARROW: (1.00, 0.20, 0.15, 0.95),
    analysis.BLOCKED: (0.55, 0.10, 0.60, 0.90),
}

_FREE_DIRECTIONS = (
    Vector((1.0, 0.0, 0.0)), Vector((-1.0, 0.0, 0.0)),
    Vector((0.0, 1.0, 0.0)), Vector((0.0, -1.0, 0.0)),
    Vector((0.0, 0.0, 1.0)), Vector((0.0, 0.0, -1.0)),
)


class WBTraversalSettings(PropertyGroup):
    profile: EnumProperty(
        name="Profile",
        items=(("WALK", "Walk", "Ground, slope, and headroom"),
               ("SWIM", "Swim", "Free volume around the probe")),
        default="WALK",
    )
    player_height: FloatProperty(name="Player Height", default=1.8, min=0.1, unit="LENGTH")
    player_radius: FloatProperty(name="Player Radius", default=0.4, min=0.01, unit="LENGTH")
    maximum_slope: FloatProperty(name="Max Slope", default=45.0, min=0.0, max=89.0)
    probe_spacing: FloatProperty(name="Probe Spacing", default=4.0, min=0.25, unit="LENGTH")
    scope: EnumProperty(
        name="Scope",
        items=(("ACTIVE_CHUNK", "Active Chunk", "Probe the active chunk only"),
               ("SELECTED_CHUNKS", "Selected Chunks", "Probe every selected chunk")),
        default="ACTIVE_CHUNK",
    )
    report: StringProperty(default="No scan")
    last_count: IntProperty(default=0)


def settings(scene):
    return getattr(scene, "worldbuilder_traversal", None)


def _cast(scene, depsgraph, origin, direction, distance):
    hit, location, normal, *_ = scene.ray_cast(depsgraph, origin, direction, distance=distance)
    return hit, location, normal


def lowest_surface(scene, depsgraph, x, y, floor_z, ceiling_z):
    """Return the lowest surface inside the band, or None.

    A single downward ray stops on the first surface it meets, which under a cave roof
    reports the roof instead of the floor. Stepping past each hit finds the surface the
    player would actually stand on. The margin catches geometry snapped exactly to a
    layer boundary.
    """
    top = ceiling_z + _BAND_MARGIN
    bottom = floor_z - _BAND_MARGIN
    down = Vector((0.0, 0.0, -1.0))
    found = None
    origin = Vector((x, y, top))
    for _ in range(_MAX_SURFACES):
        remaining = origin.z - bottom
        if remaining <= _EPSILON:
            break
        hit, location, normal = _cast(scene, depsgraph, origin, down, remaining)
        if not hit:
            break
        found = (location.copy(), normal.copy())
        origin = Vector((x, y, location.z - _EPSILON))
    return found


def probe_walk(scene, depsgraph, x, y, floor_z, ceiling_z, value):
    """Stand on the band's lowest surface, then check slope and headroom above it."""
    surface = lowest_surface(scene, depsgraph, x, y, floor_z, ceiling_z)
    if surface is None:
        return analysis.NO_GROUND, Vector((x, y, floor_z))
    location, normal = surface
    slope = analysis.slope_from_normal_z(normal.z)
    start = location + Vector((0.0, 0.0, _EPSILON))
    blocked, ceiling_hit, _ = _cast(scene, depsgraph, start, Vector((0.0, 0.0, 1.0)), value.player_height)
    headroom = (ceiling_hit - start).length if blocked else value.player_height
    return analysis.walk_status(True, slope, headroom, value.maximum_slope, value.player_height), location


def probe_swim(scene, depsgraph, x, y, floor_z, ceiling_z, value):
    """Measure the narrowest free distance around a point, vertically included."""
    point = Vector((x, y, (floor_z + ceiling_z) * 0.5))
    free = value.player_radius
    for direction in _FREE_DIRECTIONS:
        hit, location, _ = _cast(scene, depsgraph, point, direction, value.player_radius)
        if hit:
            free = min(free, (location - point).length)
    return analysis.swim_status(free, value.player_radius), point


def scan(context):
    global _results
    scene = context.scene
    value = settings(scene)
    grid = scene.worldbuilder_chunks
    if value is None:
        raise ValueError("Traversal settings are unavailable")

    coordinates = (list(state.selected_chunks(grid)) if value.scope == "SELECTED_CHUNKS"
                   else [state.explicit_active_chunk(grid)])
    coordinates = [coordinate for coordinate in coordinates if coordinate is not None]
    if not coordinates:
        raise ValueError("Choose an active chunk or select chunks first")

    floor_z, ceiling_z = layers.active_bounds_z(grid)
    depsgraph = context.evaluated_depsgraph_get()
    spacing = max(0.25, value.probe_spacing)
    results = []
    for coordinate in sorted(set(coordinates)):
        minimum_x, minimum_y, maximum_x, maximum_y = contract.chunk_bounds_xy(
            coordinate, grid.origin_x, grid.origin_y, grid.chunk_size)
        steps = max(1, int(grid.chunk_size / spacing))
        for ix in range(steps):
            x = minimum_x + (ix + 0.5) * grid.chunk_size / steps
            for iy in range(steps):
                y = minimum_y + (iy + 0.5) * grid.chunk_size / steps
                if value.profile == "WALK":
                    status, position = probe_walk(scene, depsgraph, x, y, floor_z, ceiling_z, value)
                else:
                    status, position = probe_swim(scene, depsgraph, x, y, floor_z, ceiling_z, value)
                results.append((tuple(position), status))

    _results = results
    counts = analysis.summarize([status for _, status in results])
    value.last_count = counts["total"]
    failures = " ".join(f"{key}:{counts[key]}" for key in analysis.STATUSES
                        if key != analysis.OK and counts[key])
    value.report = (f"{contract.layer_name(grid.active_layer)} · {counts['total']} probes · "
                    f"{counts['pass_ratio'] * 100:.0f}% pass" + (f" · {failures}" if failures else ""))
    return counts


def clear():
    global _results
    _results = []


def _draw():
    scene = getattr(bpy.context, "scene", None)
    value = settings(scene) if scene else None
    if value is None or not _results:
        return
    size = max(0.25, value.probe_spacing) * 0.25
    grouped = {}
    for position, status in _results:
        grouped.setdefault(status, []).extend((
            (position[0] - size, position[1], position[2]), (position[0] + size, position[1], position[2]),
            (position[0], position[1] - size, position[2]), (position[0], position[1] + size, position[2]),
        ))
    shader = gpu.shader.from_builtin("UNIFORM_COLOR")
    gpu.state.blend_set("ALPHA")
    try:
        for status, vertices in grouped.items():
            batch = batch_for_shader(shader, "LINES", {"pos": vertices})
            shader.bind()
            shader.uniform_float("color", STATUS_COLORS.get(status, (1.0, 1.0, 1.0, 0.8)))
            batch.draw(shader)
    finally:
        gpu.state.blend_set("NONE")


class WB_OT_traversal_scan(Operator):
    bl_idname = "worldbuilder.traversal_scan"
    bl_label = "Scan Traversal"

    def execute(self, context):
        try:
            counts = scan(context)
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, f"{counts['total']} probes, {counts['pass_ratio'] * 100:.0f}% pass")
        return {"FINISHED"}


class WB_OT_traversal_clear(Operator):
    bl_idname = "worldbuilder.traversal_clear"
    bl_label = "Clear Traversal"

    def execute(self, context):
        clear()
        value = settings(context.scene)
        if value is not None:
            value.report = "No scan"
            value.last_count = 0
        return {"FINISHED"}


class WB_OT_traversal_cursor_to_failure(Operator):
    bl_idname = "worldbuilder.traversal_cursor_to_failure"
    bl_label = "Cursor to First Failure"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        failure = next((position for position, status in _results if status != analysis.OK), None)
        if failure is None:
            self.report({"INFO"}, "No failing probe")
            return {"CANCELLED"}
        context.scene.cursor.location = Vector(failure)
        return {"FINISHED"}


class WB_PT_traversal(Panel):
    bl_label = "Traversal Check"
    bl_idname = "WB_PT_traversal"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        value = settings(context.scene)
        if value is None:
            return
        translate = lambda key: localization.tr(key, context.scene)
        layout.prop(value, "profile", text=translate("traversal_profile"))
        column = layout.column(align=True)
        row = column.row(align=True)
        row.prop(value, "player_height", text=translate("player_height"))
        row.prop(value, "player_radius", text=translate("player_radius"))
        if value.profile == "WALK":
            column.prop(value, "maximum_slope", text=translate("max_slope"))
        column.prop(value, "probe_spacing", text=translate("probe_spacing"))
        layout.prop(value, "scope", text=translate("traversal_scope"))
        row = layout.row(align=True)
        row.operator("worldbuilder.traversal_scan", icon="VIEWZOOM")
        row.operator("worldbuilder.traversal_clear", text="", icon="X")
        layout.label(text=value.report)
        layout.operator("worldbuilder.traversal_cursor_to_failure", icon="TRACKER")
        layout.label(text="Probes use the active vertical layer band", icon="INFO")


CLASSES = (WBTraversalSettings, WB_OT_traversal_scan, WB_OT_traversal_clear,
           WB_OT_traversal_cursor_to_failure, WB_PT_traversal)


def register():
    global _handle
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_traversal = PointerProperty(type=WBTraversalSettings)
    if _handle is None:
        _handle = bpy.types.SpaceView3D.draw_handler_add(_draw, (), "WINDOW", "POST_VIEW")


def unregister():
    global _handle
    clear()
    if _handle is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_handle, "WINDOW")
        _handle = None
    if hasattr(bpy.types.Scene, "worldbuilder_traversal"):
        del bpy.types.Scene.worldbuilder_traversal
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
