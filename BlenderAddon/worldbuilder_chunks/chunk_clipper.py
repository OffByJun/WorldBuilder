"""Deterministic triangle clipping against the authoritative Blender XY chunk grid."""

from __future__ import annotations

import math


def _lerp_value(left, right, t):
    if isinstance(left, (tuple, list)):
        return tuple(float(a) + (float(b) - float(a)) * t for a, b in zip(left, right))
    return float(left) + (float(right) - float(left)) * t


def interpolate_vertex(left, right, t):
    result = {}
    for key in sorted(set(left) & set(right)):
        if key == "position" or isinstance(left[key], (int, float, tuple, list)):
            result[key] = _lerp_value(left[key], right[key], t)
    return result


def _clip_plane(polygon, axis, boundary, keep_greater, epsilon=1e-9):
    if not polygon:
        return []
    output = []
    previous = polygon[-1]
    previous_distance = float(previous["position"][axis]) - boundary
    previous_inside = previous_distance >= -epsilon if keep_greater else previous_distance <= epsilon
    for current in polygon:
        current_distance = float(current["position"][axis]) - boundary
        current_inside = current_distance >= -epsilon if keep_greater else current_distance <= epsilon
        if current_inside != previous_inside:
            denominator = previous_distance - current_distance
            t = 0.0 if abs(denominator) <= epsilon else previous_distance / denominator
            intersection = interpolate_vertex(previous, current, t)
            position = list(intersection["position"])
            position[axis] = float(boundary)
            intersection["position"] = tuple(position)
            output.append(intersection)
        if current_inside:
            output.append(current)
        previous, previous_distance, previous_inside = current, current_distance, current_inside
    return output


def clip_polygon_to_rect(polygon, minimum_x, minimum_y, maximum_x, maximum_y):
    value = _clip_plane(polygon, 0, minimum_x, True)
    value = _clip_plane(value, 0, maximum_x, False)
    value = _clip_plane(value, 1, minimum_y, True)
    return _clip_plane(value, 1, maximum_y, False)


def _triangle_area(a, b, c):
    ab = tuple(float(b[i]) - float(a[i]) for i in range(3))
    ac = tuple(float(c[i]) - float(a[i]) for i in range(3))
    cross = (ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0])
    return math.sqrt(sum(value * value for value in cross)) * 0.5


def _floor_coord(value, origin, size):
    return math.floor((float(value) - float(origin)) / float(size))


def _vertex_key(vertex):
    values = []
    for key in ("position", "uv", "normal"):
        if key in vertex:
            values.extend(round(float(value), 8) for value in vertex[key])
        values.append(key)
    return tuple(values)


def clip_triangles(triangles, origin=(0.0, 0.0), chunk_size=128.0, epsilon=1e-8):
    """Return coord -> {vertices, faces, materials}; triangle corners carry interpolated attributes."""
    if chunk_size <= 0.0:
        raise ValueError("chunk_size must be positive")
    outputs = {}
    for triangle in triangles:
        corners = triangle["vertices"]
        if len(corners) != 3:
            raise ValueError("Only triangles are accepted")
        xs = [vertex["position"][0] for vertex in corners]
        ys = [vertex["position"][1] for vertex in corners]
        minimum_chunk = (_floor_coord(min(xs), origin[0], chunk_size), _floor_coord(min(ys), origin[1], chunk_size))
        maximum_chunk = (_floor_coord(max(xs) - epsilon, origin[0], chunk_size), _floor_coord(max(ys) - epsilon, origin[1], chunk_size))
        for chunk_x in range(minimum_chunk[0], maximum_chunk[0] + 1):
            for chunk_z in range(minimum_chunk[1], maximum_chunk[1] + 1):
                minimum_x = origin[0] + chunk_x * chunk_size
                minimum_y = origin[1] + chunk_z * chunk_size
                polygon = clip_polygon_to_rect(corners, minimum_x, minimum_y, minimum_x + chunk_size, minimum_y + chunk_size)
                if len(polygon) < 3:
                    continue
                output = outputs.setdefault((chunk_x, chunk_z), {"vertices": [], "faces": [], "materials": [], "_indices": {}})
                for index in range(1, len(polygon) - 1):
                    clipped = (polygon[0], polygon[index], polygon[index + 1])
                    if _triangle_area(*(vertex["position"] for vertex in clipped)) <= epsilon:
                        continue
                    face = []
                    for vertex in clipped:
                        key = _vertex_key(vertex)
                        vertex_index = output["_indices"].get(key)
                        if vertex_index is None:
                            vertex_index = len(output["vertices"])
                            output["_indices"][key] = vertex_index
                            output["vertices"].append(vertex)
                        face.append(vertex_index)
                    output["faces"].append(tuple(face))
                    output["materials"].append(int(triangle.get("material", 0)))
    for value in outputs.values():
        value.pop("_indices", None)
    return dict(sorted(outputs.items()))

