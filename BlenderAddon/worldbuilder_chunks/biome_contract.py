"""Blender-independent biome schema and weight helpers."""

from __future__ import annotations

import math
import re
from typing import Iterable, Sequence

SCHEMA_VERSION = 1
ATTRIBUTE_PREFIX = "WB_BIOME_"
ATTRIBUTE_DOMAIN = "POINT"
ATTRIBUTE_DATA_TYPE = "FLOAT"
DEFAULT_BIOMES = (
    ("Sand", (0.76, 0.64, 0.38, 1.0)),
    ("Rock", (0.32, 0.34, 0.38, 1.0)),
    ("Kelp", (0.12, 0.48, 0.20, 1.0)),
    ("Coral", (0.92, 0.32, 0.48, 1.0)),
    ("DeepSea", (0.05, 0.18, 0.32, 1.0)),
    ("Cave", (0.22, 0.18, 0.28, 1.0)),
    ("Ruins", (0.55, 0.48, 0.38, 1.0)),
)


def normalize_name(value: str) -> str:
    text = re.sub(r"[^A-Z0-9]+", "_", (value or "").strip().upper()).strip("_")
    if not text:
        raise ValueError("Biome name must contain at least one letter or number")
    return text


def attribute_name(value: str) -> str:
    return ATTRIBUTE_PREFIX + normalize_name(value)


def clamp_weight(value: float) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Biome weight must be finite")
    return min(1.0, max(0.0, number))


def normalize_weights(values: Sequence[float]) -> list[float]:
    clamped = [clamp_weight(value) for value in values]
    total = sum(clamped)
    if total <= 1.0 or total <= 1e-12:
        return clamped
    return [value / total for value in clamped]


def barycentric_weights(point: Sequence[float], a: Sequence[float], b: Sequence[float],
                        c: Sequence[float]) -> tuple[float, float, float]:
    v0 = tuple(b[i] - a[i] for i in range(3))
    v1 = tuple(c[i] - a[i] for i in range(3))
    v2 = tuple(point[i] - a[i] for i in range(3))
    d00 = sum(value * value for value in v0)
    d01 = sum(v0[i] * v1[i] for i in range(3))
    d11 = sum(value * value for value in v1)
    d20 = sum(v2[i] * v0[i] for i in range(3))
    d21 = sum(v2[i] * v1[i] for i in range(3))
    denominator = d00 * d11 - d01 * d01
    if abs(denominator) <= 1e-12:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / denominator
    w = (d00 * d21 - d01 * d20) / denominator
    u = 1.0 - v - w
    result = [max(0.0, u), max(0.0, v), max(0.0, w)]
    total = sum(result)
    return tuple(value / total for value in result)


def migrate_document(document: dict) -> dict:
    version = int(document.get("schemaVersion", 1))
    if version != SCHEMA_VERSION:
        raise ValueError(f"Unsupported biome schema version {version}")
    result = dict(document)
    result["schemaVersion"] = SCHEMA_VERSION
    result.setdefault("layers", [])
    return result


def build_manifest(object_name: str, layers: Iterable[dict]) -> dict:
    records = []
    for layer in layers:
        if not layer.get("export_enabled", True):
            continue
        records.append({
            "id": str(layer["stable_id"]),
            "name": str(layer["name"]),
            "attribute": str(layer["attribute_name"]),
            "domain": ATTRIBUTE_DOMAIN,
            "dataType": ATTRIBUTE_DATA_TYPE,
        })
    records.sort(key=lambda item: (item["name"].casefold(), item["id"]))
    return {"schemaVersion": SCHEMA_VERSION, "object": object_name, "layers": records}
