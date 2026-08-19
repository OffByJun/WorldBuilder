"""GPU-only WorldBuilder viewport overlay.

No mesh objects are created. Geometry batches are rebuilt only when the profile,
visible center chunk, display options, selection, or tracked ownership state changes.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Any

import blf
import bpy
import gpu
from bpy_extras import view3d_utils
from gpu_extras.batch import batch_for_shader
from mathutils import Vector

from . import contract, exporter, state

_DRAW_HANDLE_VIEW = None
_DRAW_HANDLE_PIXEL = None
_CACHES: dict[int, "ViewCache"] = {}
_GENERATION = 1
_OWNER_CACHE: dict[int, tuple[int, int]] = {}
_SHADER = None

COLOR_CHUNK = (0.20, 0.55, 1.00, 0.48)
COLOR_REGION = (1.00, 0.45, 0.08, 0.90)
COLOR_ACTIVE = (0.00, 0.85, 0.82, 0.18)
COLOR_SELECTED = (1.00, 0.80, 0.10, 0.12)
COLOR_OWNERSHIP = (1.00, 0.83, 0.08, 0.22)
COLOR_OVERRIDE = (0.70, 0.20, 1.00, 0.24)
COLOR_ERROR = (1.00, 0.06, 0.04, 0.95)
COLOR_DIRTY = (1.00, 0.75, 0.00, 0.92)
COLOR_QUERY = (0.18, 0.18, 0.18, 0.48)
COLOR_STREAM = (0.14, 0.85, 0.30, 0.08)
COLOR_AXIS_X = (1.00, 0.08, 0.04, 0.95)
COLOR_AXIS_Y = (0.12, 1.00, 0.15, 0.95)
COLOR_LAYER_BAND = (1.00, 0.72, 0.20, 0.85)


@dataclass
class LabelRecord:
    position: tuple[float, float, float]
    text: str
    coordinate: tuple[int, int] | None = None
    kind: str = "CHUNK"


@dataclass
class ViewCache:
    key: tuple[Any, ...] | None = None
    batches: dict[str, Any] = field(default_factory=dict)
    labels: list[LabelRecord] = field(default_factory=list)
    center_chunk: tuple[int, int] = (0, 0)
    active_chunk: tuple[int, int] = (0, 0)
    visible_bounds: tuple[int, int, int, int] = (0, 0, 0, 0)


def _uniform_shader():
    global _SHADER
    if _SHADER is None:
        _SHADER = gpu.shader.from_builtin("UNIFORM_COLOR")
    return _SHADER


def invalidate_all() -> None:
    global _GENERATION
    _GENERATION += 1
    for cache in _CACHES.values():
        cache.key = None
    tag_redraw_all()


def tag_redraw_all() -> None:
    wm = getattr(bpy.context, "window_manager", None)
    if wm is None:
        return
    for window in wm.windows:
        screen = window.screen
        if screen is None:
            continue
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.tag_redraw()


def initialize_owner_cache(scene) -> None:
    _OWNER_CACHE.clear()
    if scene is None or not hasattr(scene, "worldbuilder_chunks"):
        return
    settings = scene.worldbuilder_chunks
    if settings.chunk_size <= 0.0:
        return
    collection_map = exporter.chunk_collection_map(scene)
    for obj in scene.objects:
        if exporter.object_role(obj) == "GLOBAL":
            continue
        try:
            _OWNER_CACHE[obj.as_pointer()] = exporter.object_chunk(
                obj, settings, collection_map
            )
        except (ReferenceError, ValueError):
            continue


def mark_object_dirty(obj, scene=None, *, invalidate=True) -> None:
    scene = scene or getattr(bpy.context, "scene", None)
    if scene is None or not hasattr(scene, "worldbuilder_chunks"):
        return
    if obj is None or getattr(obj, "name", "").startswith("__WB_EXPORT_TEMP__"):
        return
    settings = scene.worldbuilder_chunks
    if settings.chunk_size <= 0.0:
        return
    old = None
    try:
        pointer = obj.as_pointer()
        old = _OWNER_CACHE.get(pointer)
        collection_map = exporter.chunk_collection_map(scene)
        new = exporter.object_chunk(obj, settings, collection_map)
        _OWNER_CACHE[pointer] = new
        state.mark_dirty(settings, old, new)
    except (ReferenceError, ValueError):
        state.mark_dirty(settings, old)
    if invalidate:
        invalidate_all()


def _refresh_owner_cache(scene, *, mark_changes: bool) -> None:
    settings = scene.worldbuilder_chunks
    collection_map = exporter.chunk_collection_map(scene)
    current_pointers = set()
    for obj in scene.objects:
        if exporter.object_role(obj) == "GLOBAL":
            continue
        try:
            pointer = obj.as_pointer()
            current_pointers.add(pointer)
            old = _OWNER_CACHE.get(pointer)
            new = exporter.object_chunk(obj, settings, collection_map)
            _OWNER_CACHE[pointer] = new
            if mark_changes and old != new:
                state.mark_dirty(settings, old, new)
        except (ReferenceError, ValueError):
            continue
    removed = set(_OWNER_CACHE).difference(current_pointers)
    for pointer in removed:
        old = _OWNER_CACHE.pop(pointer, None)
        if mark_changes:
            state.mark_dirty(settings, old)


def depsgraph_update_post(scene, depsgraph) -> None:
    if scene is None or not hasattr(scene, "worldbuilder_chunks"):
        return
    object_updates = []
    collection_changed = False
    for update in depsgraph.updates:
        datablock = update.id
        if isinstance(datablock, bpy.types.Object):
            object_updates.append(datablock)
        elif isinstance(datablock, bpy.types.Collection):
            collection_changed = True
    if collection_changed:
        _refresh_owner_cache(scene, mark_changes=True)
    else:
        for obj in object_updates:
            mark_object_dirty(obj, scene, invalidate=False)
    if collection_changed or object_updates:
        invalidate_all()


def _settings(context):
    scene = context.scene
    if scene is None or not hasattr(scene, "worldbuilder_chunks"):
        return None
    return scene.worldbuilder_chunks


def plane_z(settings) -> float:
    """Return the Z the floor overlay is drawn on, following the active layer."""
    if not getattr(settings, "layer_follow_grid", False):
        return settings.overlay_z
    return (
        contract.layer_floor_z(
            settings.active_layer, settings.layer_base_z, settings.layer_height
        )
        + settings.overlay_z
    )


def _view_plane_point(context, settings) -> Vector:
    region = context.region
    rv3d = context.region_data
    height = plane_z(settings)
    if region is None or rv3d is None:
        return Vector((settings.origin_x, settings.origin_y, height))
    center_2d = (region.width * 0.5, region.height * 0.5)
    try:
        origin = view3d_utils.region_2d_to_origin_3d(region, rv3d, center_2d)
        direction = view3d_utils.region_2d_to_vector_3d(region, rv3d, center_2d)
        if abs(direction.z) > 1e-8:
            distance = (height - origin.z) / direction.z
            if math.isfinite(distance):
                return origin + direction * distance
    except (AttributeError, ValueError):
        pass
    location = rv3d.view_location
    return Vector((location.x, location.y, height))


def grid_center_point(context, settings) -> Vector:
    mode = settings.grid_center_mode
    if mode == "ACTIVE_OBJECT" and context.active_object is not None:
        return context.active_object.matrix_world.translation.copy()
    if mode == "CURSOR":
        return context.scene.cursor.location.copy()
    if mode == "WORLD_ORIGIN":
        return Vector((settings.origin_x, settings.origin_y, plane_z(settings)))
    return _view_plane_point(context, settings)


def center_chunk(context, settings) -> tuple[int, int]:
    point = grid_center_point(context, settings)
    return contract.chunk_coord_from_xy(
        point.x, point.y, settings.origin_x, settings.origin_y, settings.chunk_size
    )


def active_chunk(context, settings, fallback=None) -> tuple[int, int]:
    active = context.active_object
    if active is not None and active.select_get() and exporter.object_role(active) != "GLOBAL":
        try:
            return exporter.object_chunk(
                active, settings, exporter.chunk_collection_map(context.scene)
            )
        except (ReferenceError, ValueError):
            pass
    explicit = state.explicit_active_chunk(settings)
    if explicit is not None:
        return explicit
    return fallback if fallback is not None else center_chunk(context, settings)


def streaming_focus_chunk(context, settings) -> tuple[int, int]:
    if settings.streaming_focus == "ACTIVE_OBJECT" and context.active_object is not None:
        point = context.active_object.matrix_world.translation
    else:
        point = context.scene.cursor.location
    return contract.chunk_coord_from_xy(
        point.x, point.y, settings.origin_x, settings.origin_y, settings.chunk_size
    )


def _line(vertices, a, b) -> None:
    vertices.extend((a, b))


def _rect_lines(vertices, bounds, z) -> None:
    min_x, min_y, max_x, max_y = bounds
    p0 = (min_x, min_y, z)
    p1 = (max_x, min_y, z)
    p2 = (max_x, max_y, z)
    p3 = (min_x, max_y, z)
    vertices.extend((p0, p1, p1, p2, p2, p3, p3, p0))


def _dashed_rect(vertices, bounds, z, segments=8) -> None:
    min_x, min_y, max_x, max_y = bounds
    edges = (
        ((min_x, min_y), (max_x, min_y)),
        ((max_x, min_y), (max_x, max_y)),
        ((max_x, max_y), (min_x, max_y)),
        ((min_x, max_y), (min_x, min_y)),
    )
    for start, end in edges:
        for index in range(0, segments, 2):
            t0 = index / segments
            t1 = min((index + 1) / segments, 1.0)
            a = (
                start[0] + (end[0] - start[0]) * t0,
                start[1] + (end[1] - start[1]) * t0,
                z,
            )
            b = (
                start[0] + (end[0] - start[0]) * t1,
                start[1] + (end[1] - start[1]) * t1,
                z,
            )
            vertices.extend((a, b))


def _quad_triangles(vertices, bounds, z) -> None:
    min_x, min_y, max_x, max_y = bounds
    p0 = (min_x, min_y, z)
    p1 = (max_x, min_y, z)
    p2 = (max_x, max_y, z)
    p3 = (min_x, max_y, z)
    vertices.extend((p0, p1, p2, p0, p2, p3))


def _aabb_lines(vertices, bounds) -> None:
    minimum, maximum = bounds
    corners = [
        (x, y, z)
        for x in (minimum.x, maximum.x)
        for y in (minimum.y, maximum.y)
        for z in (minimum.z, maximum.z)
    ]
    # Corner index bits: x=4, y=2, z=1.
    for index, point in enumerate(corners):
        for bit in (1, 2, 4):
            other = index ^ bit
            if index < other:
                vertices.extend((point, corners[other]))


def _make_batch(shader, mode, vertices):
    if not vertices:
        return None
    return batch_for_shader(shader, mode, {"pos": vertices})


def _selection_signature(context) -> tuple:
    result = []
    for obj in sorted(context.selected_objects, key=lambda value: value.name):
        properties = getattr(obj, "worldbuilder_chunk", None)
        result.append(
            (
                obj.name,
                exporter.object_role(obj),
                bool(properties.override_chunk) if properties else False,
                int(properties.chunk_x) if properties else 0,
                int(properties.chunk_z) if properties else 0,
                bool(properties.allow_cross_chunk) if properties else False,
            )
        )
    return tuple(result)


def _build_key(context, settings, center, active) -> tuple:
    return (
        _GENERATION,
        center,
        active,
        settings.chunk_size,
        settings.chunks_per_region,
        settings.query_cell_size,
        settings.origin_x,
        settings.origin_y,
        settings.grid_radius,
        settings.overlay_z,
        settings.active_layer,
        settings.layer_height,
        settings.layer_base_z,
        settings.layer_follow_grid,
        settings.show_layer_bands,
        settings.layer_isolate,
        settings.show_chunk_grid,
        settings.show_region_grid,
        settings.show_coordinates,
        settings.show_active_chunk,
        settings.show_object_ownership,
        settings.show_boundary_errors,
        settings.show_query_cells,
        settings.show_streaming_preview,
        settings.streaming_focus,
        settings.streaming_region_radius,
        settings.selected_chunks_json,
        settings.dirty_chunks_json,
        settings.validation_error_objects_json,
        _selection_signature(context),
    )


def _object_counts(scene, settings) -> dict[tuple[int, int], int]:
    result: dict[tuple[int, int], int] = {}
    collection_map = exporter.chunk_collection_map(scene)
    for obj in scene.objects:
        if obj.hide_render or exporter.object_role(obj) == "GLOBAL":
            continue
        coordinate = exporter.object_chunk(obj, settings, collection_map)
        result[coordinate] = result.get(coordinate, 0) + 1
    return result


def _build_cache(context, cache, settings, center, active) -> None:
    shader = _uniform_shader()
    radius = settings.grid_radius
    min_x, max_x = center[0] - radius, center[0] + radius
    min_z, max_z = center[1] - radius, center[1] + radius
    cache.center_chunk = center
    cache.active_chunk = active
    cache.visible_bounds = (min_x, min_z, max_x, max_z)

    z = plane_z(settings)
    band_lines: list[tuple[float, float, float]] = []
    chunk_lines: list[tuple[float, float, float]] = []
    region_lines: list[tuple[float, float, float]] = []
    active_fill: list[tuple[float, float, float]] = []
    selected_fill: list[tuple[float, float, float]] = []
    ownership_fill: list[tuple[float, float, float]] = []
    override_fill: list[tuple[float, float, float]] = []
    dirty_lines: list[tuple[float, float, float]] = []
    error_lines: list[tuple[float, float, float]] = []
    override_error_lines: list[tuple[float, float, float]] = []
    query_lines: list[tuple[float, float, float]] = []
    stream_fill: list[tuple[float, float, float]] = []
    axis_x: list[tuple[float, float, float]] = []
    axis_y: list[tuple[float, float, float]] = []

    extent_min_x = settings.origin_x + min_x * settings.chunk_size
    extent_max_x = settings.origin_x + (max_x + 1) * settings.chunk_size
    extent_min_y = settings.origin_y + min_z * settings.chunk_size
    extent_max_y = settings.origin_y + (max_z + 1) * settings.chunk_size

    if settings.show_chunk_grid or settings.show_region_grid:
        for x in range(min_x, max_x + 2):
            world_x = settings.origin_x + x * settings.chunk_size
            is_region = x % settings.chunks_per_region == 0
            if is_region and settings.show_region_grid:
                target = region_lines
            elif settings.show_chunk_grid:
                target = chunk_lines
            else:
                continue
            target.extend(((world_x, extent_min_y, z), (world_x, extent_max_y, z)))
        for chunk_z in range(min_z, max_z + 2):
            world_y = settings.origin_y + chunk_z * settings.chunk_size
            is_region = chunk_z % settings.chunks_per_region == 0
            if is_region and settings.show_region_grid:
                target = region_lines
            elif settings.show_chunk_grid:
                target = chunk_lines
            else:
                continue
            target.extend(((extent_min_x, world_y, z), (extent_max_x, world_y, z)))

    if settings.show_active_chunk:
        _quad_triangles(
            active_fill,
            contract.chunk_bounds_xy(
                active, settings.origin_x, settings.origin_y, settings.chunk_size
            ),
            z + 0.004,
        )

    if settings.show_layer_bands:
        band_min_x, band_min_y, band_max_x, band_max_y = contract.chunk_bounds_xy(
            active, settings.origin_x, settings.origin_y, settings.chunk_size
        )
        floor_z, ceiling_z = contract.layer_bounds_z(
            settings.active_layer, settings.layer_base_z, settings.layer_height
        )
        _aabb_lines(
            band_lines,
            (
                Vector((band_min_x, band_min_y, floor_z)),
                Vector((band_max_x, band_max_y, ceiling_z)),
            ),
        )

    selected = state.selected_chunks(settings)
    for coordinate in selected:
        if min_x <= coordinate[0] <= max_x and min_z <= coordinate[1] <= max_z:
            _quad_triangles(
                selected_fill,
                contract.chunk_bounds_xy(
                    coordinate,
                    settings.origin_x,
                    settings.origin_y,
                    settings.chunk_size,
                ),
                z + 0.006,
            )

    dirty = state.dirty_chunks(settings)
    for coordinate in dirty:
        if min_x <= coordinate[0] <= max_x and min_z <= coordinate[1] <= max_z:
            _dashed_rect(
                dirty_lines,
                contract.chunk_bounds_xy(
                    coordinate,
                    settings.origin_x,
                    settings.origin_y,
                    settings.chunk_size,
                ),
                z + 0.012,
            )

    collection_map = exporter.chunk_collection_map(context.scene)
    known_errors = state.validation_error_names(settings)
    ownership_coords: set[tuple[int, int]] = set()
    override_coords: set[tuple[int, int]] = set()
    objects_for_bounds = set(context.selected_objects)
    for name in known_errors:
        obj = context.scene.objects.get(name)
        if obj is not None:
            objects_for_bounds.add(obj)

    if settings.show_object_ownership:
        for obj in context.selected_objects:
            status = exporter.object_bounds_status(obj, settings, collection_map)
            if status.ownership_source == "OVERRIDE":
                override_coords.add(status.owner)
            else:
                ownership_coords.add(status.owner)

    for coordinate in ownership_coords:
        _quad_triangles(
            ownership_fill,
            contract.chunk_bounds_xy(
                coordinate, settings.origin_x, settings.origin_y, settings.chunk_size
            ),
            z + 0.008,
        )
    for coordinate in override_coords:
        _quad_triangles(
            override_fill,
            contract.chunk_bounds_xy(
                coordinate, settings.origin_x, settings.origin_y, settings.chunk_size
            ),
            z + 0.009,
        )

    if settings.show_boundary_errors:
        for obj in objects_for_bounds:
            status = exporter.object_bounds_status(obj, settings, collection_map)
            if not status.crosses_chunk or status.bounds is None:
                continue
            target = override_error_lines if status.allow_cross_chunk else error_lines
            _aabb_lines(target, status.bounds)

    if settings.show_query_cells and settings.query_cell_size > 0.0:
        bounds = contract.chunk_bounds_xy(
            active, settings.origin_x, settings.origin_y, settings.chunk_size
        )
        cell = min(settings.query_cell_size, settings.chunk_size)
        x = bounds[0] + cell
        while x < bounds[2] - 1e-6:
            query_lines.extend(((x, bounds[1], z + 0.010), (x, bounds[3], z + 0.010)))
            x += cell
        y = bounds[1] + cell
        while y < bounds[3] - 1e-6:
            query_lines.extend(((bounds[0], y, z + 0.010), (bounds[2], y, z + 0.010)))
            y += cell

    if settings.show_streaming_preview:
        focus_chunk = streaming_focus_chunk(context, settings)
        focus_region = contract.region_coord(
            focus_chunk[0], focus_chunk[1], settings.chunks_per_region
        )
        rr = settings.streaming_region_radius
        for region_x in range(focus_region[0] - rr, focus_region[0] + rr + 1):
            for region_z in range(focus_region[1] - rr, focus_region[1] + rr + 1):
                _quad_triangles(
                    stream_fill,
                    contract.region_bounds_xy(
                        (region_x, region_z),
                        settings.origin_x,
                        settings.origin_y,
                        settings.chunk_size,
                        settings.chunks_per_region,
                    ),
                    z + 0.002,
                )

    axis_length = settings.chunk_size * 0.45
    axis_x.extend(
        (
            (settings.origin_x - axis_length, settings.origin_y, z + 0.020),
            (settings.origin_x + axis_length, settings.origin_y, z + 0.020),
        )
    )
    axis_y.extend(
        (
            (settings.origin_x, settings.origin_y - axis_length, z + 0.020),
            (settings.origin_x, settings.origin_y + axis_length, z + 0.020),
        )
    )

    cache.batches = {
        "stream_fill": _make_batch(shader, "TRIS", stream_fill),
        "active_fill": _make_batch(shader, "TRIS", active_fill),
        "selected_fill": _make_batch(shader, "TRIS", selected_fill),
        "ownership_fill": _make_batch(shader, "TRIS", ownership_fill),
        "override_fill": _make_batch(shader, "TRIS", override_fill),
        "chunk_lines": _make_batch(shader, "LINES", chunk_lines),
        "region_lines": _make_batch(shader, "LINES", region_lines),
        "dirty_lines": _make_batch(shader, "LINES", dirty_lines),
        "query_lines": _make_batch(shader, "LINES", query_lines),
        "error_lines": _make_batch(shader, "LINES", error_lines),
        "override_error_lines": _make_batch(shader, "LINES", override_error_lines),
        "axis_x": _make_batch(shader, "LINES", axis_x),
        "axis_y": _make_batch(shader, "LINES", axis_y),
        "band_lines": _make_batch(shader, "LINES", band_lines),
    }

    labels: list[LabelRecord] = []
    if settings.show_coordinates:
        counts = _object_counts(context.scene, settings)
        visible_regions: set[tuple[int, int]] = set()
        for chunk_x in range(min_x, max_x + 1):
            for chunk_z in range(min_z, max_z + 1):
                coordinate = (chunk_x, chunk_z)
                region = contract.region_coord(
                    chunk_x, chunk_z, settings.chunks_per_region
                )
                visible_regions.add(region)
                center_x, center_y = contract.chunk_center_xy(
                    coordinate,
                    settings.origin_x,
                    settings.origin_y,
                    settings.chunk_size,
                )
                text = f"{contract.chunk_name(coordinate)}\n{contract.region_name(region)}"
                if coordinate in dirty:
                    text += "\nDirty"
                count = counts.get(coordinate, 0)
                text += f"\n{count} object{'s' if count != 1 else ''}"
                labels.append(
                    LabelRecord(
                        (center_x, center_y, z + 0.025),
                        text,
                        coordinate=coordinate,
                        kind="CHUNK",
                    )
                )
        if settings.show_region_grid:
            for region in visible_regions:
                bounds = contract.region_bounds_xy(
                    region,
                    settings.origin_x,
                    settings.origin_y,
                    settings.chunk_size,
                    settings.chunks_per_region,
                )
                labels.append(
                    LabelRecord(
                        (
                            (bounds[0] + bounds[2]) * 0.5,
                            (bounds[1] + bounds[3]) * 0.5,
                            z + 0.030,
                        ),
                        contract.region_name(region),
                        coordinate=region,
                        kind="REGION",
                    )
                )
    cache.labels = labels


def _cache_for_context(context, settings) -> ViewCache | None:
    if context.region is None or context.region_data is None:
        return None
    pointer = context.region.as_pointer()
    cache = _CACHES.setdefault(pointer, ViewCache())
    center = center_chunk(context, settings)
    active = active_chunk(context, settings, center)
    key = _build_key(context, settings, center, active)
    if cache.key != key:
        _build_cache(context, cache, settings, center, active)
        cache.key = key
    return cache


def _draw_batch(shader, batch, color, width=1.0) -> None:
    if batch is None:
        return
    gpu.state.line_width_set(width)
    shader.bind()
    shader.uniform_float("color", color)
    batch.draw(shader)


def draw_view() -> None:
    context = bpy.context
    if context.area is None or context.area.type != "VIEW_3D":
        return
    settings = _settings(context)
    if settings is None or settings.chunk_size <= 0.0:
        return
    cache = _cache_for_context(context, settings)
    if cache is None:
        return
    shader = _uniform_shader()
    gpu.state.blend_set("ALPHA")
    gpu.state.depth_test_set("LESS_EQUAL")
    try:
        _draw_batch(shader, cache.batches.get("stream_fill"), COLOR_STREAM)
        _draw_batch(shader, cache.batches.get("active_fill"), COLOR_ACTIVE)
        _draw_batch(shader, cache.batches.get("selected_fill"), COLOR_SELECTED)
        _draw_batch(shader, cache.batches.get("ownership_fill"), COLOR_OWNERSHIP)
        _draw_batch(shader, cache.batches.get("override_fill"), COLOR_OVERRIDE)
        if settings.show_chunk_grid:
            _draw_batch(shader, cache.batches.get("chunk_lines"), COLOR_CHUNK, 1.0)
        if settings.show_region_grid:
            _draw_batch(shader, cache.batches.get("region_lines"), COLOR_REGION, 2.5)
        _draw_batch(shader, cache.batches.get("query_lines"), COLOR_QUERY, 1.0)
        _draw_batch(shader, cache.batches.get("dirty_lines"), COLOR_DIRTY, 2.0)
        _draw_batch(shader, cache.batches.get("error_lines"), COLOR_ERROR, 3.0)
        _draw_batch(
            shader,
            cache.batches.get("override_error_lines"),
            COLOR_OVERRIDE,
            3.0,
        )
        _draw_batch(shader, cache.batches.get("axis_x"), COLOR_AXIS_X, 3.0)
        _draw_batch(shader, cache.batches.get("axis_y"), COLOR_AXIS_Y, 3.0)
        if settings.show_layer_bands:
            _draw_batch(shader, cache.batches.get("band_lines"), COLOR_LAYER_BAND, 1.5)
    finally:
        gpu.state.line_width_set(1.0)
        gpu.state.depth_test_set("NONE")
        gpu.state.blend_set("NONE")


def _font_size(font_id, size) -> None:
    try:
        blf.size(font_id, size)
    except TypeError:
        blf.size(font_id, size, 72)


def _draw_text(font_id, x, y, text, color, size=11) -> None:
    _font_size(font_id, size)
    line_height = size + 3
    lines = text.splitlines()
    for index, line in enumerate(lines):
        yy = y - index * line_height
        blf.position(font_id, x + 1, yy - 1, 0)
        blf.color(font_id, 0.0, 0.0, 0.0, 0.75)
        blf.draw(font_id, line)
        blf.position(font_id, x, yy, 0)
        blf.color(font_id, *color)
        blf.draw(font_id, line)


def draw_pixel() -> None:
    context = bpy.context
    if context.area is None or context.area.type != "VIEW_3D":
        return
    settings = _settings(context)
    if settings is None or not settings.show_coordinates:
        return
    cache = _cache_for_context(context, settings)
    if cache is None:
        return
    region = context.region
    rv3d = context.region_data
    center_world = contract.chunk_center_xy(
        cache.center_chunk, settings.origin_x, settings.origin_y, settings.chunk_size
    )
    center_2d = view3d_utils.location_3d_to_region_2d(
        region, rv3d, Vector((center_world[0], center_world[1], plane_z(settings)))
    )
    edge_2d = view3d_utils.location_3d_to_region_2d(
        region,
        rv3d,
        Vector(
            (
                center_world[0] + settings.chunk_size,
                center_world[1],
                plane_z(settings),
            )
        ),
    )
    projected_chunk = 999.0
    if center_2d is not None and edge_2d is not None:
        projected_chunk = (edge_2d - center_2d).length
    zoomed_out = projected_chunk < 45.0
    selected = state.selected_chunks(settings)

    for label in cache.labels:
        if label.kind == "CHUNK" and label.coordinate is not None:
            distance = max(
                abs(label.coordinate[0] - cache.center_chunk[0]),
                abs(label.coordinate[1] - cache.center_chunk[1]),
            )
            if distance > settings.label_radius:
                continue
            if zoomed_out and label.coordinate not in selected and label.coordinate != cache.active_chunk:
                continue
        elif label.kind == "REGION" and zoomed_out:
            continue

        screen = view3d_utils.location_3d_to_region_2d(
            region, rv3d, Vector(label.position)
        )
        if screen is None:
            continue
        if screen.x < 8 or screen.y < 8 or screen.x > region.width - 8 or screen.y > region.height - 8:
            continue
        if label.kind == "REGION":
            _draw_text(0, screen.x, screen.y, label.text, (1.0, 0.55, 0.12, 0.95), 12)
        else:
            color = (
                (0.15, 1.0, 0.95, 1.0)
                if label.coordinate == cache.active_chunk
                else (0.92, 0.95, 1.0, 0.92)
            )
            _draw_text(0, screen.x, screen.y, label.text, color, 10)


def register_handlers() -> None:
    global _DRAW_HANDLE_VIEW, _DRAW_HANDLE_PIXEL
    if _DRAW_HANDLE_VIEW is None:
        _DRAW_HANDLE_VIEW = bpy.types.SpaceView3D.draw_handler_add(
            draw_view, (), "WINDOW", "POST_VIEW"
        )
    if _DRAW_HANDLE_PIXEL is None:
        _DRAW_HANDLE_PIXEL = bpy.types.SpaceView3D.draw_handler_add(
            draw_pixel, (), "WINDOW", "POST_PIXEL"
        )


def unregister_handlers() -> None:
    global _DRAW_HANDLE_VIEW, _DRAW_HANDLE_PIXEL, _SHADER
    if _DRAW_HANDLE_VIEW is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_DRAW_HANDLE_VIEW, "WINDOW")
        _DRAW_HANDLE_VIEW = None
    if _DRAW_HANDLE_PIXEL is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_DRAW_HANDLE_PIXEL, "WINDOW")
        _DRAW_HANDLE_PIXEL = None
    _CACHES.clear()
    _OWNER_CACHE.clear()
    _SHADER = None
