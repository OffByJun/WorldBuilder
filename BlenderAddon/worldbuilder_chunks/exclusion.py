"""Pure exclusion volume math shared by scatter and spline corridors."""

import math


def point_inside_box(point, center, half_extents):
    return all(abs(float(point[i]) - float(center[i])) <= abs(float(half_extents[i])) for i in range(3))


def point_inside_sphere(point, center, radius):
    return sum((float(point[i]) - float(center[i])) ** 2 for i in range(3)) <= float(radius) ** 2


def point_segment_distance_xy(point, start, end):
    px, py = float(point[0]), float(point[1])
    ax, ay = float(start[0]), float(start[1])
    bx, by = float(end[0]), float(end[1])
    dx, dy = bx - ax, by - ay
    denom = dx * dx + dy * dy
    t = 0.0 if denom <= 1e-12 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / denom))
    x, y = ax + dx * t, ay + dy * t
    return math.hypot(px - x, py - y)


def distance_to_curve_xy(point, points):
    if len(points) < 2:
        return math.inf
    return min(point_segment_distance_xy(point, a, b) for a, b in zip(points, points[1:]))


def soft_weight(distance, radius, falloff):
    radius = max(0.0, float(radius))
    falloff = max(0.0, float(falloff))
    if distance <= radius:
        return 1.0
    if falloff <= 0.0 or distance >= radius + falloff:
        return 0.0
    t = (distance - radius) / falloff
    return 1.0 - t * t * (3.0 - 2.0 * t)

