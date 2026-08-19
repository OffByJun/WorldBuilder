"""Pure biome brush math. This module intentionally has no bpy dependency."""

import math


def clamp01(value):
    return max(0.0, min(1.0, float(value)))


def falloff(kind, normalized_distance):
    t = clamp01(normalized_distance)
    if kind == "CONSTANT":
        return 1.0 if t < 1.0 else 0.0
    if kind == "SHARP":
        return (1.0 - t) ** 2
    if kind == "SMOOTH":
        x = 1.0 - t
        return x * x * (3.0 - 2.0 * x)
    return 1.0 - t


def blend(old, target, strength, influence=1.0):
    amount = clamp01(strength) * clamp01(influence)
    return clamp01(float(old) + (float(target) - float(old)) * amount)


def paint(old, strength, influence=1.0, mode="ADD"):
    target = 1.0
    if mode == "ADD":
        return clamp01(float(old) + clamp01(strength) * clamp01(influence))
    return blend(old, target, strength, influence)


def erase(old, strength, influence=1.0):
    return blend(old, 0.0, strength, influence)


def smooth(old, neighbor_values, strength, influence=1.0):
    values = [float(value) for value in neighbor_values]
    target = sum(values) / len(values) if values else float(old)
    return blend(old, target, strength, influence)


def auto_normalize(weights, active_index, active_value):
    """Set active value and proportionally fit other weights into remaining capacity."""
    result = [clamp01(value) for value in weights]
    if not result:
        return result
    active_index = int(active_index)
    active_value = clamp01(active_value)
    result[active_index] = active_value
    remaining = 1.0 - active_value
    other_sum = sum(value for index, value in enumerate(result) if index != active_index)
    if other_sum > remaining and other_sum > 1e-12:
        scale = remaining / other_sum
        for index in range(len(result)):
            if index != active_index:
                result[index] *= scale
    return result


def brush_influence(distance, radius, strength, kind="SMOOTH"):
    if radius <= 0.0 or distance > radius:
        return 0.0
    return clamp01(strength) * falloff(kind, distance / radius)

