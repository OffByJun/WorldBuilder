"""Non-destructive terrain carve math operating from immutable basis positions."""

import math
from .spline_sampling import nearest_point_xy


def falloff(kind, t):
    t = max(0.0, min(1.0, float(t)))
    if kind == "SHARP":
        return (1.0 - t) ** 2
    if kind == "SMOOTH":
        x = 1.0 - t
        return x * x * (3.0 - 2.0 * x)
    return 1.0 - t


def profile(kind, normalized_distance):
    x = max(0.0, min(1.0, float(normalized_distance)))
    if kind == "V":
        return 1.0 - x
    if kind == "FLAT_BOTTOM":
        return 1.0 if x <= 0.45 else max(0.0, (1.0 - x) / 0.55)
    if kind == "U":
        return math.sqrt(max(0.0, 1.0 - x*x))
    return (1.0 - x*x) ** 2


def carve_vertex(basis, polyline, width, depth, falloff_distance=0.0, profile_kind="SMOOTH", falloff_kind="SMOOTH", raise_terrain=False):
    nearest = nearest_point_xy(basis, polyline)
    if nearest is None:
        return tuple(basis)
    distance_xy, center, _, _, _ = nearest
    width = max(float(width), 1e-6)
    outer = width + max(0.0, float(falloff_distance))
    if distance_xy > outer:
        return tuple(basis)
    inner = profile(profile_kind, min(1.0, distance_xy / width)) if distance_xy <= width else 0.0
    edge = 1.0 if distance_xy <= width else falloff(falloff_kind, (distance_xy-width) / max(outer-width, 1e-6))
    delta = abs(float(depth)) * max(inner, edge * 0.001)
    if raise_terrain:
        z = basis[2] + delta * edge
    else:
        target = center[2] - delta
        z = min(float(basis[2]), target) if profile_kind == "FLAT_BOTTOM" else float(basis[2]) - delta * edge
    return (float(basis[0]), float(basis[1]), z)


def rebuild_vertices(basis_positions, polyline, settings, boundary_predicate=None):
    if len(polyline) < 2:
        return [tuple(value) for value in basis_positions]
    outer = max(float(settings.get("width", 0.0)), 1e-6) + max(0.0, float(settings.get("falloff", 0.0)))
    min_x = min(value[0] for value in polyline) - outer
    max_x = max(value[0] for value in polyline) + outer
    min_y = min(value[1] for value in polyline) - outer
    max_y = max(value[1] for value in polyline) + outer
    result = []
    for value in basis_positions:
        if settings.get("preserve_boundary") and boundary_predicate and boundary_predicate(value):
            result.append(tuple(value))
        elif value[0] < min_x or value[0] > max_x or value[1] < min_y or value[1] > max_y:
            result.append(tuple(value))
        else:
            result.append(carve_vertex(value, polyline, settings["width"], settings["depth"], settings.get("falloff", 0.0), settings.get("profile", "SMOOTH"), settings.get("falloff_kind", "SMOOTH"), settings.get("raise", False)))
    return result
