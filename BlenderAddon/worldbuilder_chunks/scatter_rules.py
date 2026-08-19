"""Deterministic rule-based scatter algorithms without Blender dependencies."""

import hashlib
import math
import random


def stable_seed(*parts):
    payload = "\x1f".join(str(part) for part in parts).encode("utf-8")
    return int.from_bytes(hashlib.sha256(payload).digest()[:8], "big", signed=False)


def weighted_index(weights, seed):
    cleaned = [max(0.0, float(value)) for value in weights]
    total = sum(cleaned)
    if total <= 0.0:
        return None
    cursor = random.Random(int(seed)).random() * total
    for index, weight in enumerate(cleaned):
        cursor -= weight
        if cursor <= 0.0 and weight > 0.0:
            return index
    return len(cleaned) - 1


def slope_degrees(normal):
    length = math.sqrt(sum(float(v) * float(v) for v in normal))
    if length <= 1e-12:
        return 90.0
    return math.degrees(math.acos(max(-1.0, min(1.0, float(normal[2]) / length))))


def evaluate_candidate(candidate, rules, exclusion_weight=0.0):
    height = float(candidate["position"][2])
    slope = float(candidate.get("slope", slope_degrees(candidate.get("normal", (0, 0, 1)))))
    if not rules.get("min_height", -math.inf) <= height <= rules.get("max_height", math.inf):
        return False, "HEIGHT"
    if not rules.get("min_slope", 0.0) <= slope <= rules.get("max_slope", 180.0):
        return False, "SLOPE"
    if float(candidate.get("biome_weight", 1.0)) < rules.get("biome_min", 0.0):
        return False, "BIOME"
    if float(candidate.get("exclusion_biome_weight", 0.0)) >= rules.get("exclusion_biome_min", math.inf):
        return False, "EXCLUSION_BIOME"
    if exclusion_weight >= 1.0:
        return False, "EXCLUSION"
    return True, "ACCEPTED"


class SpatialHash:
    def __init__(self, minimum_distance):
        self.minimum_distance = max(0.0, float(minimum_distance))
        self.cell_size = max(self.minimum_distance, 1e-6)
        self.cells = {}

    def _key(self, point):
        return tuple(math.floor(float(point[i]) / self.cell_size) for i in range(3))

    def can_insert(self, point):
        if self.minimum_distance <= 0.0:
            return True
        key = self._key(point)
        squared = self.minimum_distance * self.minimum_distance
        for x in range(key[0] - 1, key[0] + 2):
            for y in range(key[1] - 1, key[1] + 2):
                for z in range(key[2] - 1, key[2] + 2):
                    for other in self.cells.get((x, y, z), ()):
                        if sum((float(point[i]) - other[i]) ** 2 for i in range(3)) < squared:
                            return False
        return True

    def insert(self, point):
        value = tuple(float(v) for v in point)
        self.cells.setdefault(self._key(value), []).append(value)


def candidate_key(layer_id, sample_index, position):
    quantized = tuple(round(float(v), 5) for v in position)
    return f"{stable_seed(layer_id, sample_index, *quantized):016x}"

