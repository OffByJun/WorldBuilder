"""Blender-independent WorldBuilder chunk exchange contract utilities.

The coordinate contract is authoritative for both viewport visualization and export:
Blender X/Y -> Unity X/Z, with floor-based [min, max) chunk ownership.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
from typing import Iterable, Sequence

MANIFEST_VERSION = 2
PLACEMENTS_VERSION = 1
PROFILE_VERSION = 1
ADDON_VERSION = "1.1.0"
CHUNK_NAME_RE = re.compile(r"^CH_([+-]?\d+)_([+-]?\d+)$", re.IGNORECASE)
REGION_NAME_RE = re.compile(r"^RG_([+-]?\d+)_([+-]?\d+)$", re.IGNORECASE)
LAYER_NAME_RE = re.compile(r"^LV_([+-]?\d+)$", re.IGNORECASE)

ENTITY_KINDS = (
    "Generic",
    "Creature",
    "Resource",
    "DroppedItem",
    "Projectile",
    "Effect",
)
ENTITY_FLAGS = ("Persistent", "RegionStreamed", "Replicated")


def floor_coord(value: float, origin: float, size: float) -> int:
    """Return the owner cell using the floor-based [min, max) rule."""
    if size <= 0.0:
        raise ValueError("chunk size must be positive")
    return math.floor((value - origin) / size)


def chunk_coord_from_xy(
    x: float,
    y: float,
    origin_x: float,
    origin_y: float,
    size: float,
) -> tuple[int, int]:
    """Map Blender world X/Y to Unity-style chunk X/Z."""
    return floor_coord(x, origin_x, size), floor_coord(y, origin_y, size)


def region_coord(chunk_x: int, chunk_z: int, chunks_per_region: int) -> tuple[int, int]:
    """Return region coordinates. Python floor division intentionally handles negatives."""
    if chunks_per_region <= 0:
        raise ValueError("chunks_per_region must be positive")
    return chunk_x // chunks_per_region, chunk_z // chunks_per_region


def layer_floor_z(index: int, base_z: float, height: float) -> float:
    """Return the Blender Z of an authoring layer floor."""
    if height <= 0.0:
        raise ValueError("layer height must be positive")
    return base_z + index * height


def layer_bounds_z(index: int, base_z: float, height: float) -> tuple[float, float]:
    floor = layer_floor_z(index, base_z, height)
    return floor, floor + height


def layer_index_for_z(z: float, base_z: float, height: float) -> int:
    """Map a Blender Z to its owning layer using the same [min, max) rule as chunks."""
    return floor_coord(z, base_z, height)


def clamp_layer(index: int, count: int) -> int:
    if count <= 0:
        raise ValueError("layer count must be positive")
    return max(0, min(int(index), count - 1))


def layer_name(index: int) -> str:
    return f"LV_{signed(index)}"


def parse_layer_name(value: str) -> int | None:
    match = LAYER_NAME_RE.match(value or "")
    return int(match.group(1)) if match else None


DEPTH_BANDS = ("Surface", "Shallow", "Mid", "Deep", "Abyss")


def depth_below(sea_level: float, z: float) -> float:
    """Return metres of water above a point. Zero at or above the surface."""
    return max(0.0, float(sea_level) - float(z))


def depth_band_index(sea_level: float, z: float, shallow: float, mid: float, deep: float) -> int:
    """Classify a Z into a gameplay depth band using cumulative band thicknesses."""
    if min(shallow, mid, deep) < 0.0:
        raise ValueError("band thicknesses must not be negative")
    depth = depth_below(sea_level, z)
    if depth <= 0.0:
        return 0
    if depth <= shallow:
        return 1
    if depth <= shallow + mid:
        return 2
    if depth <= shallow + mid + deep:
        return 3
    return 4


def depth_band_name(index: int) -> str:
    return DEPTH_BANDS[max(0, min(int(index), len(DEPTH_BANDS) - 1))]


def depth_band_boundaries(sea_level: float, shallow: float, mid: float, deep: float) -> list[float]:
    """Return the Z of each band floor, surface first."""
    return [
        float(sea_level),
        float(sea_level) - shallow,
        float(sea_level) - shallow - mid,
        float(sea_level) - shallow - mid - deep,
    ]


def entity_flag_names(persistent: bool, region_streamed: bool, replicated: bool) -> list[str]:
    flags = []
    if persistent:
        flags.append("Persistent")
    if region_streamed:
        flags.append("RegionStreamed")
    if replicated:
        flags.append("Replicated")
    return flags


def chunk_bounds_xy(
    coordinate: tuple[int, int],
    origin_x: float,
    origin_y: float,
    size: float,
) -> tuple[float, float, float, float]:
    """Return Blender XY bounds as (min_x, min_y, max_x, max_y)."""
    if size <= 0.0:
        raise ValueError("chunk size must be positive")
    minimum_x = origin_x + coordinate[0] * size
    minimum_y = origin_y + coordinate[1] * size
    return minimum_x, minimum_y, minimum_x + size, minimum_y + size


def chunk_center_xy(
    coordinate: tuple[int, int],
    origin_x: float,
    origin_y: float,
    size: float,
) -> tuple[float, float]:
    minimum_x, minimum_y, maximum_x, maximum_y = chunk_bounds_xy(
        coordinate, origin_x, origin_y, size
    )
    return (minimum_x + maximum_x) * 0.5, (minimum_y + maximum_y) * 0.5


def region_bounds_xy(
    region: tuple[int, int],
    origin_x: float,
    origin_y: float,
    chunk_size: float,
    chunks_per_region: int,
) -> tuple[float, float, float, float]:
    if chunks_per_region <= 0:
        raise ValueError("chunks_per_region must be positive")
    region_size = chunk_size * chunks_per_region
    minimum_x = origin_x + region[0] * region_size
    minimum_y = origin_y + region[1] * region_size
    return minimum_x, minimum_y, minimum_x + region_size, minimum_y + region_size


def bounds_cross_chunk_xy(
    minimum_x: float,
    minimum_y: float,
    maximum_x: float,
    maximum_y: float,
    coordinate: tuple[int, int],
    origin_x: float,
    origin_y: float,
    size: float,
    epsilon: float = 0.01,
) -> bool:
    """Return True only when bounds intrude into another chunk.

    Touching the boundary is valid. The epsilon only absorbs floating-point noise;
    ownership itself still follows [min, max).
    """
    chunk_min_x, chunk_min_y, chunk_max_x, chunk_max_y = chunk_bounds_xy(
        coordinate, origin_x, origin_y, size
    )
    return (
        minimum_x < chunk_min_x - epsilon
        or minimum_y < chunk_min_y - epsilon
        or maximum_x > chunk_max_x + epsilon
        or maximum_y > chunk_max_y + epsilon
    )


def signed(value: int) -> str:
    return f"{value:+05d}"


def chunk_name(coordinate: tuple[int, int]) -> str:
    return f"CH_{signed(coordinate[0])}_{signed(coordinate[1])}"


def region_name(coordinate: tuple[int, int]) -> str:
    return f"RG_{signed(coordinate[0])}_{signed(coordinate[1])}"


def parse_chunk_name(value: str) -> tuple[int, int] | None:
    match = CHUNK_NAME_RE.match(value or "")
    return (int(match.group(1)), int(match.group(2))) if match else None


def parse_region_name(value: str) -> tuple[int, int] | None:
    match = REGION_NAME_RE.match(value or "")
    return (int(match.group(1)), int(match.group(2))) if match else None


def canonical_json(value: object, pretty: bool = True) -> str:
    if pretty:
        return json.dumps(
            value, sort_keys=True, ensure_ascii=False, indent=2, allow_nan=False
        ) + "\n"
    return json.dumps(
        value,
        sort_keys=True,
        ensure_ascii=False,
        separators=(",", ":"),
        allow_nan=False,
    )


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def normalized_float(value: float) -> float:
    result = round(float(value), 8)
    return 0.0 if result == -0.0 else result


def normalized_matrix(matrix: Sequence[Sequence[float]]) -> list[list[float]]:
    if len(matrix) != 4 or any(len(row) != 4 for row in matrix):
        raise ValueError("matrix must be 4x4")
    return [
        [normalized_float(matrix[row][column]) for column in range(4)]
        for row in range(4)
    ]


def matrix_multiply(
    left: Sequence[Sequence[float]], right: Sequence[Sequence[float]]
) -> list[list[float]]:
    return [
        [
            sum(left[row][k] * right[k][column] for k in range(4))
            for column in range(4)
        ]
        for row in range(4)
    ]


def blender_matrix_to_unity_row_major(
    matrix: Sequence[Sequence[float]], chunk_origin_x: float, chunk_origin_y: float
) -> list[float]:
    """Map Blender X/Y/Z into chunk-local Unity X/Z/Y using a basis transform."""
    local = [[float(matrix[row][column]) for column in range(4)] for row in range(4)]
    local[0][3] -= chunk_origin_x
    local[1][3] -= chunk_origin_y
    basis = [
        [1.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 1.0, 0.0],
        [0.0, 1.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ]
    converted = matrix_multiply(matrix_multiply(basis, local), basis)
    return [
        normalized_float(converted[row][column])
        for row in range(4)
        for column in range(4)
    ]


def file_reference(path: str, relative_to: str) -> dict[str, object]:
    if not path or not os.path.isfile(path):
        return {"path": "", "sha256": "", "bytes": 0}
    return {
        "path": os.path.relpath(path, relative_to).replace("\\", "/"),
        "sha256": sha256_file(path),
        "bytes": os.path.getsize(path),
    }


def content_hash(
    authoring_hash: str, references: Iterable[dict[str, object]]
) -> str:
    payload = {"authoringHash": authoring_hash, "files": list(references)}
    return sha256_bytes(canonical_json(payload, pretty=False).encode("utf-8"))


def safe_world_id(value: str) -> str:
    sanitized = re.sub(r"[^A-Za-z0-9_-]", "_", (value or "World_01").strip())
    return sanitized or "World_01"
