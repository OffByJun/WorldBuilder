"""Portable, versioned Asset Stamp JSON contract."""

import json
from .manifest import atomic_write, content_hash

SCHEMA_VERSION = 1
REGISTRY_SCHEMA_VERSION = 1


def create_stamp(name, stable_id, category, pivot, bounds, objects, tags=(), patches=None):
    return {"schemaVersion": SCHEMA_VERSION, "name": str(name), "stableId": str(stable_id), "category": str(category), "tags": sorted(set(str(tag) for tag in tags)), "pivot": list(map(float, pivot)), "bounds": bounds, "objects": sorted(objects, key=lambda value: (value.get("assetId", ""), value.get("name", ""))), "patches": patches or {}}


def validate_stamp(value):
    errors = []
    if int(value.get("schemaVersion", 0)) != SCHEMA_VERSION: errors.append("Unsupported stamp schema version")
    if not value.get("stableId"): errors.append("Missing stableId")
    if not isinstance(value.get("objects"), list): errors.append("objects must be an array")
    return errors


def save_stamp(path, value):
    errors = validate_stamp(value)
    if errors: raise ValueError("; ".join(errors))
    atomic_write(path, value)


def load_stamp(path):
    with open(path, "r", encoding="utf-8") as stream: value = json.load(stream)
    errors = validate_stamp(value)
    if errors: raise ValueError("; ".join(errors))
    return value

def create_asset_registry(entries):
    values=sorted(entries,key=lambda value:value.get("assetId", ""))
    return {"schemaVersion":REGISTRY_SCHEMA_VERSION,"assets":values,"contentHash":content_hash(values)}

def validate_asset_registry(value):
    errors=[]
    if int(value.get("schemaVersion",0))!=REGISTRY_SCHEMA_VERSION:errors.append("Unsupported asset registry schema version")
    assets=value.get("assets")
    if not isinstance(assets,list):return errors+["assets must be an array"]
    ids=[]
    for item in assets:
        if not isinstance(item,dict) or not item.get("assetId") or not (item.get("objectName") or item.get("collectionName")):errors.append("Every asset requires assetId and objectName or collectionName");continue
        ids.append(item["assetId"])
    if len(ids)!=len(set(ids)):errors.append("assetId values must be unique")
    return errors

def save_asset_registry(path,entries):
    value=create_asset_registry(entries);errors=validate_asset_registry(value)
    if errors:raise ValueError("; ".join(errors))
    atomic_write(path,value)

def load_asset_registry(path):
    with open(path,"r",encoding="utf-8") as stream:value=json.load(stream)
    errors=validate_asset_registry(value)
    if errors:raise ValueError("; ".join(errors))
    return {item["assetId"]:item for item in value["assets"]}
