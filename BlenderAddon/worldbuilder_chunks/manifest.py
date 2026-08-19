"""Deterministic WorldBuilder bake manifest serialization."""

import hashlib
import json

SCHEMA_VERSION = 1


def canonical_json(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def content_hash(value):
    return hashlib.sha256(canonical_json(value).encode("utf-8")).hexdigest()


def build_chunk_manifest(world_id, chunk, profile_hash, objects, vertex_channels=None, vertex_attributes=None):
    ordered = []
    for source in sorted(objects, key=lambda item: (str(item.get("stableId", "")), str(item.get("name", "")))):
        item = dict(source)
        if "lods" in item:
            item["lods"] = sorted(item["lods"], key=lambda lod: int(lod.get("level", 0)))
        ordered.append(item)
    result = {"schemaVersion": SCHEMA_VERSION, "worldId": str(world_id), "chunk": {"x": int(chunk[0]), "z": int(chunk[1])}, "profileHash": str(profile_hash), "objects": ordered}
    if vertex_channels:
        result["vertexAttributeChannels"] = dict(sorted(vertex_channels.items()))
    if vertex_attributes:
        result["vertexAttributes"] = sorted(vertex_attributes,key=lambda item:(item.get("name",""),item.get("domain","")))
    return result


def atomic_write(path, payload):
    import os
    temporary = f"{path}.tmp"
    with open(temporary, "w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True))
        stream.write("\n")
    os.replace(temporary, path)
