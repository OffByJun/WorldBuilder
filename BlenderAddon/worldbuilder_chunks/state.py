"""Small persistent scene-state helpers used by UI, tools, and overlays."""

from __future__ import annotations

import json
from typing import Iterable


def _decode_coords(raw: str) -> set[tuple[int, int]]:
    try:
        value = json.loads(raw or "[]")
        result = set()
        if isinstance(value, list):
            for item in value:
                if isinstance(item, list) and len(item) == 2:
                    result.add((int(item[0]), int(item[1])))
        return result
    except (TypeError, ValueError, json.JSONDecodeError):
        return set()


def _encode_coords(coords: Iterable[tuple[int, int]]) -> str:
    return json.dumps([list(coord) for coord in sorted(set(coords))], separators=(",", ":"))


def selected_chunks(settings) -> set[tuple[int, int]]:
    return _decode_coords(getattr(settings, "selected_chunks_json", "[]"))


def set_selected_chunks(settings, coords: Iterable[tuple[int, int]]) -> None:
    settings.selected_chunks_json = _encode_coords(coords)


def dirty_chunks(settings) -> set[tuple[int, int]]:
    return _decode_coords(getattr(settings, "dirty_chunks_json", "[]"))


def set_dirty_chunks(settings, coords: Iterable[tuple[int, int]]) -> None:
    settings.dirty_chunks_json = _encode_coords(coords)


def mark_dirty(settings, *coords: tuple[int, int] | None) -> None:
    value = dirty_chunks(settings)
    value.update(coord for coord in coords if coord is not None)
    set_dirty_chunks(settings, value)


def clear_dirty(settings, coords: Iterable[tuple[int, int]]) -> None:
    value = dirty_chunks(settings)
    value.difference_update(coords)
    set_dirty_chunks(settings, value)


def validation_error_names(settings) -> set[str]:
    try:
        value = json.loads(getattr(settings, "validation_error_objects_json", "[]") or "[]")
        return {str(item) for item in value} if isinstance(value, list) else set()
    except (TypeError, ValueError, json.JSONDecodeError):
        return set()


def set_validation_error_names(settings, names: Iterable[str]) -> None:
    settings.validation_error_objects_json = json.dumps(
        sorted(set(str(name) for name in names)), separators=(",", ":")
    )


def set_active_chunk(settings, coordinate: tuple[int, int]) -> None:
    settings.has_active_chunk = True
    settings.active_chunk_x = int(coordinate[0])
    settings.active_chunk_z = int(coordinate[1])


def explicit_active_chunk(settings) -> tuple[int, int] | None:
    if not getattr(settings, "has_active_chunk", False):
        return None
    return int(settings.active_chunk_x), int(settings.active_chunk_z)
