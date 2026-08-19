"""Versioned WorldBuilder spline contract."""

SCHEMA_VERSION = 1
SPLINE_TYPES = ("PATH", "RIVER", "CANYON", "CLIFF", "CAVE", "PIPE", "CREATURE_ROUTE", "STREAMING_GUIDE")
MODIFIER_TYPES = ("TERRAIN_CARVE", "TERRAIN_RAISE", "CLEAR_SCATTER", "SCATTER_ALONG_PATH", "MESH_EXTRUDE", "EXPORT_METADATA")


def validate_spline_payload(payload):
    errors = []
    if int(payload.get("schema_version", 0)) != SCHEMA_VERSION:
        errors.append("Unsupported spline schema version")
    if payload.get("type") not in SPLINE_TYPES:
        errors.append("Invalid spline type")
    if not str(payload.get("stable_id", "")).strip():
        errors.append("Missing spline stable_id")
    return errors

