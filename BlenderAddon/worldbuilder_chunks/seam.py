"""Chunk seam matching and safe position stitching."""

import math

EDGES = ("NORTH", "SOUTH", "EAST", "WEST")


def neighbor(coord, edge):
    x, z = coord
    return {"NORTH": (x, z+1), "SOUTH": (x, z-1), "EAST": (x+1, z), "WEST": (x-1, z)}[edge]


def edge_parameter(point, edge):
    return float(point[0] if edge in {"NORTH", "SOUTH"} else point[1])


def match_edges(points_a, points_b, edge, xy_tolerance=1e-4):
    a = sorted(enumerate(points_a), key=lambda item: edge_parameter(item[1], edge))
    b = sorted(enumerate(points_b), key=lambda item: edge_parameter(item[1], edge))
    if len(a) != len(b):
        return [], "DIFFERENT_EDGE_RESOLUTION"
    pairs = []
    for left, right in zip(a, b):
        if abs(edge_parameter(left[1], edge) - edge_parameter(right[1], edge)) > xy_tolerance:
            return pairs, "CORRESPONDENCE_FAILED"
        pairs.append((left[0], right[0]))
    return pairs, "OK"


def position_errors(points_a, points_b, pairs):
    values = [math.sqrt(sum((float(points_a[a][i])-float(points_b[b][i]))**2 for i in range(3))) for a,b in pairs]
    return {"maximum": max(values, default=0.0), "average": sum(values)/len(values) if values else 0.0, "affected": sum(value > 1e-8 for value in values)}


def stitched_positions(points_a, points_b, pairs, mode="AVERAGE"):
    left, right = [list(map(tuple, values)) for values in (points_a, points_b)]
    for a,b in pairs:
        if mode == "A_TO_B": left[a] = right[b]
        elif mode == "B_TO_A": right[b] = left[a]
        else:
            value = tuple((left[a][i]+right[b][i])*0.5 for i in range(3))
            left[a] = right[b] = value
    return left, right

