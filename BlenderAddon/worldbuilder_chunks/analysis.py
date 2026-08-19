"""Cell-based world-analysis aggregations."""

import math


def cell_coord(position, origin, cell_size):
    return (math.floor((float(position[0])-float(origin[0]))/float(cell_size)), math.floor((float(position[1])-float(origin[1]))/float(cell_size)))


def aggregate_objects(records, origin=(0,0), cell_size=32.0):
    cells = {}
    for record in records:
        key = cell_coord(record["position"], origin, cell_size)
        value = cells.setdefault(key, {"object_count": 0, "triangle_count": 0, "collider_count": 0})
        value["object_count"] += 1
        value["triangle_count"] += max(0, int(record.get("triangles", 0)))
        value["collider_count"] += 1 if record.get("collider") else 0
    return dict(sorted(cells.items()))


def traversable(slope_degrees, maximum_slope, occupied=False, profile="LAND"):
    return not occupied and float(slope_degrees) <= float(maximum_slope)


def cell_coord_3d(position, origin, cell_size, layer):
    """Cell key that keeps vertically stacked geometry apart."""
    x, y = cell_coord(position, origin, cell_size)
    return (x, y, int(layer))


OK = "OK"
NO_GROUND = "NO_GROUND"
STEEP = "STEEP"
LOW_CEILING = "LOW_CEILING"
NARROW = "NARROW"
BLOCKED = "BLOCKED"

STATUSES = (OK, NO_GROUND, STEEP, LOW_CEILING, NARROW, BLOCKED)


def slope_from_normal_z(normal_z):
    """Degrees away from level for a unit surface normal's Z component."""
    return math.degrees(math.acos(max(-1.0, min(1.0, abs(float(normal_z))))))


def walk_status(has_ground, slope_degrees, headroom, maximum_slope, player_height):
    """Classify a walk probe. Order matters: the first failing rule is reported."""
    if not has_ground:
        return NO_GROUND
    if float(slope_degrees) > float(maximum_slope):
        return STEEP
    if float(headroom) < float(player_height):
        return LOW_CEILING
    return OK


def swim_status(minimum_free_distance, player_radius):
    """Classify a swim probe by the narrowest free distance around the sample."""
    free = float(minimum_free_distance)
    if free <= 0.0:
        return BLOCKED
    return NARROW if free < float(player_radius) else OK


def summarize(statuses):
    counts = {status: 0 for status in STATUSES}
    for status in statuses:
        if status in counts:
            counts[status] += 1
    total = sum(counts.values())
    counts["total"] = total
    counts["pass_ratio"] = counts[OK] / total if total else 0.0
    return counts

