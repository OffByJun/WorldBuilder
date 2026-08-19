"""Configurable vertex attribute channel contract and pure source evaluation."""

import math

SOURCES = ("CONSTANT", "HEIGHT_NORMALIZED", "UP_FACING", "SLOPE", "AMBIENT_OCCLUSION_APPROX", "CAVITY_APPROX", "RANDOM_PER_FACE", "BIOME_WEIGHT", "WATER_DEPTH", "OBJECT_RANDOM", "CURVATURE")


def evaluate_source(source, sample, options=None):
    options = options or {}
    if source == "CONSTANT": return max(0.0, min(1.0, float(options.get("constant", 0.0))))
    if source == "HEIGHT_NORMALIZED":
        low, high = options.get("min_height", 0.0), options.get("max_height", 1.0)
        return max(0.0, min(1.0, (float(sample.get("height", 0.0))-low)/max(high-low, 1e-12)))
    if source == "UP_FACING": return max(0.0, min(1.0, float(sample.get("normal", (0,0,1))[2])))
    if source == "SLOPE": return 1.0 - evaluate_source("UP_FACING", sample, options)
    if source == "BIOME_WEIGHT": return max(0.0, min(1.0, float(sample.get("biome_weight", 0.0))))
    if source == "WATER_DEPTH": return max(0.0, min(1.0, float(sample.get("water_depth", 0.0))/max(float(options.get("max_depth", 1.0)), 1e-12)))
    return max(0.0, min(1.0, float(sample.get(source.lower(), 0.0))))


def bake_rgba(sample, channel_sources, channel_options=None):
    options = channel_options or {}
    return tuple(evaluate_source(channel_sources[index], sample, options.get("RGBA"[index], {})) for index in range(4))

