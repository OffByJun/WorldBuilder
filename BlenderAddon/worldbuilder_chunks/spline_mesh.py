"""Pure stable-frame sweep mesh generator."""

import math


def _normalize(v):
    length = math.sqrt(sum(x*x for x in v))
    return tuple(x / length for x in v) if length > 1e-12 else (0.0, 0.0, 1.0)


def _cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def sweep(samples, radius=1.0, radial_segments=8, flip_normals=False, flat_bottom=0.0, cap_start=False, cap_end=False):
    radial_segments = max(3, int(radial_segments))
    vertices, faces, uvs = [], [], []
    previous_side = None
    for row, sample in enumerate(samples):
        row_radius=float(radius[row] if isinstance(radius,(list,tuple)) else radius)
        tangent = _normalize(tuple(sample["tangent"]))
        reference = previous_side or ((1.0, 0.0, 0.0) if abs(tangent[2]) > 0.9 else (0.0, 0.0, 1.0))
        side = _normalize(_cross(reference, tangent))
        if previous_side is not None and sum(a*b for a,b in zip(side, previous_side)) < 0.0:
            side = tuple(-v for v in side)
        up = _normalize(_cross(tangent, side))
        previous_side = side
        center = sample["position"]
        for column in range(radial_segments):
            angle = math.tau * column / radial_segments
            sy = math.sin(angle)
            if flat_bottom > 0.0 and sy < -1.0 + flat_bottom:
                sy = -1.0 + flat_bottom
            offset = tuple(row_radius * (math.cos(angle)*side[i] + sy*up[i]) for i in range(3))
            vertices.append(tuple(center[i] + offset[i] for i in range(3)))
            uvs.append((column / radial_segments, sample.get("normalized_distance", 0.0)))
    for row in range(len(samples)-1):
        for column in range(radial_segments):
            a = row*radial_segments+column
            b = row*radial_segments+(column+1)%radial_segments
            c = (row+1)*radial_segments+(column+1)%radial_segments
            d = (row+1)*radial_segments+column
            faces.append((a,d,c,b) if flip_normals else (a,b,c,d))
    if cap_start:
        face=tuple(range(radial_segments-1,-1,-1))
        faces.append(tuple(reversed(face)) if flip_normals else face)
    if cap_end:
        offset=(len(samples)-1)*radial_segments;face=tuple(offset+i for i in range(radial_segments))
        faces.append(tuple(reversed(face)) if flip_normals else face)
    return {"vertices": vertices, "faces": faces, "uvs": uvs}
