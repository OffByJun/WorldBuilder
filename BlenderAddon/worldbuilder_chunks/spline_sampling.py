"""Polyline sampling and nearest-point routines; coordinate system is Blender XYZ."""

import math


def _lerp(a, b, t):
    return tuple(float(a[i]) + (float(b[i]) - float(a[i])) * t for i in range(3))


def distance(a, b):
    return math.sqrt(sum((float(a[i]) - float(b[i])) ** 2 for i in range(3)))


def sample_polyline(points, spacing, cyclic=False, max_samples=100000):
    values = [tuple(float(v) for v in point) for point in points]
    if cyclic and len(values) > 2:
        values.append(values[0])
    if len(values) < 2:
        return []
    spacing = max(float(spacing), 1e-5)
    lengths = [distance(a, b) for a, b in zip(values, values[1:])]
    total = sum(lengths)
    if total <= 1e-12:
        return []
    result = []
    count = min(int(math.floor(total / spacing)) + 1, int(max_samples))
    segment = 0
    cumulative = 0.0
    for index in range(count):
        target = min(index * spacing, total)
        while segment < len(lengths) - 1 and cumulative + lengths[segment] < target:
            cumulative += lengths[segment]
            segment += 1
        length = lengths[segment]
        t = 0.0 if length <= 1e-12 else (target - cumulative) / length
        position = _lerp(values[segment], values[segment + 1], t)
        tangent = tuple((values[segment + 1][i] - values[segment][i]) / max(length, 1e-12) for i in range(3))
        result.append({"position": position, "tangent": tangent, "cumulative_distance": target, "normalized_distance": target / total})
    if not cyclic and distance(result[-1]["position"], values[-1]) > 1e-6 and len(result) < max_samples:
        tangent = result[-1]["tangent"]
        result.append({"position": values[-1], "tangent": tangent, "cumulative_distance": total, "normalized_distance": 1.0})
    return result


def nearest_point_xy(point, polyline):
    from .exclusion import point_segment_distance_xy
    best = None
    cumulative = 0.0
    for index, (a, b) in enumerate(zip(polyline, polyline[1:])):
        ax, ay = a[0], a[1]
        bx, by = b[0], b[1]
        dx, dy = bx - ax, by - ay
        denominator = dx * dx + dy * dy
        t = 0.0 if denominator <= 1e-12 else max(0.0, min(1.0, ((point[0]-ax)*dx + (point[1]-ay)*dy) / denominator))
        candidate = (ax + dx*t, ay + dy*t, a[2] + (b[2]-a[2])*t)
        current = point_segment_distance_xy(point, a, b)
        if best is None or current < best[0]:
            best = (current, candidate, index, t, cumulative + distance(a, b) * t)
        cumulative += distance(a, b)
    return best

