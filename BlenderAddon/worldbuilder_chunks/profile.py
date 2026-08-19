"""Unity-authored WorldGrid.profile.json loading and synchronization state."""

from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass
from typing import Any

from . import contract

_APPLYING = False


@dataclass(frozen=True)
class ProfileResult:
    valid: bool
    data: dict[str, Any] | None
    error: str = ""
    source_hash: str = ""
    mtime_ns: int = 0


def is_applying() -> bool:
    return _APPLYING


def _number(value: Any, field: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{field} must be a number")
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{field} must be finite")
    return result


def validate_document(document: Any) -> dict[str, Any]:
    if not isinstance(document, dict):
        raise ValueError("Profile root must be a JSON object")
    if document.get("version") != contract.PROFILE_VERSION:
        raise ValueError(f"Unsupported profile version: {document.get('version')!r}")

    world_id = str(document.get("worldId", "")).strip()
    if not world_id:
        raise ValueError("worldId is required")

    chunk_size = _number(document.get("chunkSize"), "chunkSize")
    if chunk_size <= 0.0:
        raise ValueError("chunkSize must be greater than zero")

    chunks_per_region = document.get("chunksPerRegion")
    if isinstance(chunks_per_region, bool) or not isinstance(chunks_per_region, int):
        raise ValueError("chunksPerRegion must be an integer")
    if chunks_per_region <= 0:
        raise ValueError("chunksPerRegion must be greater than zero")

    query_cell_size = _number(document.get("queryCellSize"), "queryCellSize")
    if query_cell_size <= 0.0:
        raise ValueError("queryCellSize must be greater than zero")

    world_origin = document.get("worldOrigin")
    if not isinstance(world_origin, dict):
        raise ValueError("worldOrigin must be an object")
    origin_x = _number(world_origin.get("x"), "worldOrigin.x")
    origin_z = _number(world_origin.get("z"), "worldOrigin.z")

    coordinate_system = document.get("coordinateSystem")
    if not isinstance(coordinate_system, dict):
        raise ValueError("coordinateSystem must be an object")
    expected = {
        "blenderPlane": "XY",
        "unityPlane": "XZ",
        "vectorMapping": "XZY",
    }
    for key, expected_value in expected.items():
        actual = coordinate_system.get(key)
        if actual != expected_value:
            raise ValueError(
                f"coordinateSystem.{key} must be {expected_value!r}, got {actual!r}"
            )

    return {
        "version": contract.PROFILE_VERSION,
        "worldId": world_id,
        "chunkSize": chunk_size,
        "chunksPerRegion": chunks_per_region,
        "queryCellSize": query_cell_size,
        "worldOrigin": {"x": origin_x, "z": origin_z},
        "coordinateSystem": expected,
    }


def load_file(path: str) -> ProfileResult:
    resolved = os.path.abspath(path)
    try:
        with open(resolved, "rb") as stream:
            raw = stream.read()
        document = json.loads(raw.decode("utf-8-sig"))
        normalized = validate_document(document)
        return ProfileResult(
            valid=True,
            data=normalized,
            source_hash=contract.sha256_bytes(raw),
            mtime_ns=os.stat(resolved).st_mtime_ns,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        return ProfileResult(valid=False, data=None, error=str(exc))


def settings_snapshot(settings) -> dict[str, Any]:
    return {
        "version": contract.PROFILE_VERSION,
        "worldId": str(settings.world_id),
        "chunkSize": contract.normalized_float(settings.chunk_size),
        "chunksPerRegion": int(settings.chunks_per_region),
        "queryCellSize": contract.normalized_float(settings.query_cell_size),
        "worldOrigin": {
            "x": contract.normalized_float(settings.origin_x),
            "z": contract.normalized_float(settings.origin_y),
        },
        "coordinateSystem": {
            "blenderPlane": "XY",
            "unityPlane": "XZ",
            "vectorMapping": "XZY",
        },
    }


def snapshot_json(settings) -> str:
    return contract.canonical_json(settings_snapshot(settings), pretty=False)


def apply_to_settings(settings, result: ProfileResult) -> None:
    global _APPLYING
    if not result.valid or result.data is None:
        settings.profile_status = "INVALID"
        settings.profile_message = result.error or "Invalid profile"
        return

    data = result.data
    _APPLYING = True
    try:
        settings.world_id = data["worldId"]
        settings.chunk_size = data["chunkSize"]
        settings.chunks_per_region = data["chunksPerRegion"]
        settings.query_cell_size = data["queryCellSize"]
        settings.origin_x = data["worldOrigin"]["x"]
        settings.origin_y = data["worldOrigin"]["z"]
        settings.profile_snapshot_json = contract.canonical_json(data, pretty=False)
        settings.profile_hash = result.source_hash
        settings.profile_mtime_ns = str(result.mtime_ns)
        settings.profile_status = "SYNCED"
        settings.profile_message = "Unity grid profile loaded"
    finally:
        _APPLYING = False


def mark_modified(settings, message: str = "Blender grid values differ from the loaded profile") -> None:
    if _APPLYING:
        return
    if getattr(settings, "profile_status", "INVALID") != "INVALID":
        settings.profile_status = "MODIFIED"
        settings.profile_message = message


def current_status(settings, resolved_path: str | None = None) -> tuple[str, str]:
    status = getattr(settings, "profile_status", "INVALID")
    message = getattr(settings, "profile_message", "")

    stored_snapshot = getattr(settings, "profile_snapshot_json", "")
    if stored_snapshot and snapshot_json(settings) != stored_snapshot:
        status = "MODIFIED"
        message = "Blender grid values differ from the loaded profile"

    path = resolved_path
    if path and status != "INVALID" and os.path.isfile(path):
        try:
            stored_mtime = int(getattr(settings, "profile_mtime_ns", "0") or 0)
            if stored_mtime and os.stat(path).st_mtime_ns != stored_mtime:
                status = "MODIFIED"
                message = "Profile file changed on disk; reload before export"
        except (OSError, ValueError):
            status = "INVALID"
            message = "Profile file is not accessible"
    elif path and not os.path.isfile(path):
        status = "INVALID"
        message = "Profile file does not exist"

    return status, message


def export_allowed(settings, resolved_path: str | None = None) -> tuple[bool, str]:
    status, message = current_status(settings, resolved_path)
    if status == "SYNCED":
        return True, ""
    if getattr(settings, "developer_override", False):
        return True, f"Developer Override enabled while profile status is {status}: {message}"
    return False, f"Grid profile is {status}. Reload the Unity profile or enable Developer Override. {message}"
