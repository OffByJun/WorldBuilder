"""Biome attribute storage, validation, sampling, and cache management."""

from __future__ import annotations

import math
from dataclasses import dataclass

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import biome_contract

TARGET_KEY = "wb_biome_target"
SCHEMA_KEY = "wb_biome_schema_version"


@dataclass
class _SurfaceCache:
    bvh: BVHTree
    points: list[Vector]
    triangles: list[tuple[int, int, int]]
    signature: tuple


_surface_cache: dict[int, _SurfaceCache] = {}


def biome_definitions(scene: bpy.types.Scene):
    settings = getattr(scene, "worldbuilder_biomes", None)
    return settings.layers if settings is not None else ()


def definition_by_id(scene: bpy.types.Scene, biome_id: str):
    for definition in biome_definitions(scene):
        if definition.stable_id == biome_id:
            return definition
    return None


def mark_biome_target(obj: bpy.types.Object) -> None:
    if obj is None or obj.type != "MESH":
        raise ValueError("Biome target must be a Mesh object")
    obj[TARGET_KEY] = True
    obj[SCHEMA_KEY] = biome_contract.SCHEMA_VERSION


def is_biome_target(obj: bpy.types.Object | None) -> bool:
    return bool(obj is not None and obj.type == "MESH" and obj.get(TARGET_KEY, False))


def validate_biome_target(obj: bpy.types.Object | None) -> list[str]:
    if obj is None:
        return ["No active biome target"]
    if obj.type != "MESH":
        return ["Biome target must be a Mesh object"]
    if not obj.get(TARGET_KEY, False):
        return ["Object is not marked as a WorldBuilder biome target"]
    if int(obj.get(SCHEMA_KEY, 0)) != biome_contract.SCHEMA_VERSION:
        return ["Biome target schema version is unsupported"]
    return []


def ensure_biome_attribute(mesh: bpy.types.Mesh, definition):
    attribute = mesh.attributes.get(definition.attribute_name)
    if attribute is None:
        attribute = mesh.attributes.new(
            name=definition.attribute_name,
            type=biome_contract.ATTRIBUTE_DATA_TYPE,
            domain=biome_contract.ATTRIBUTE_DOMAIN,
        )
    if attribute.domain != biome_contract.ATTRIBUTE_DOMAIN or attribute.data_type != biome_contract.ATTRIBUTE_DATA_TYPE:
        raise ValueError(
            f"{definition.attribute_name} must be POINT/FLOAT, got {attribute.domain}/{attribute.data_type}"
        )
    return attribute


def _attribute(mesh: bpy.types.Mesh, definition_or_name):
    name = getattr(definition_or_name, "attribute_name", definition_or_name)
    attribute = mesh.attributes.get(str(name))
    if attribute is None:
        raise KeyError(f"Missing biome attribute {name}")
    return attribute


def get_biome_weight_at_vertex(mesh: bpy.types.Mesh, definition_or_name, vertex_index: int) -> float:
    attribute = _attribute(mesh, definition_or_name)
    if vertex_index < 0 or vertex_index >= len(attribute.data):
        raise IndexError(vertex_index)
    return float(attribute.data[vertex_index].value)


def set_biome_weight_at_vertex(mesh: bpy.types.Mesh, definition_or_name,
                                vertex_index: int, value: float) -> None:
    attribute = _attribute(mesh, definition_or_name)
    if vertex_index < 0 or vertex_index >= len(attribute.data):
        raise IndexError(vertex_index)
    attribute.data[vertex_index].value = biome_contract.clamp_weight(value)
    mesh.update()


def fill_biome_attribute(mesh: bpy.types.Mesh, definition_or_name, value: float) -> None:
    attribute = _attribute(mesh, definition_or_name)
    weight = biome_contract.clamp_weight(value)
    for item in attribute.data:
        item.value = weight
    mesh.update()


def normalize_vertex_weights(mesh: bpy.types.Mesh, definitions, vertex_index: int) -> None:
    enabled = [definition for definition in definitions if definition.enabled]
    values = [get_biome_weight_at_vertex(mesh, definition, vertex_index) for definition in enabled]
    for definition, value in zip(enabled, biome_contract.normalize_weights(values)):
        _attribute(mesh, definition).data[vertex_index].value = value


def normalize_all_weights(mesh: bpy.types.Mesh, definitions) -> None:
    for vertex_index in range(len(mesh.vertices)):
        normalize_vertex_weights(mesh, definitions, vertex_index)
    mesh.update()


def invalidate_biome_cache(obj: bpy.types.Object | None = None) -> None:
    if obj is None:
        _surface_cache.clear()
    else:
        _surface_cache.pop(obj.as_pointer(), None)


def _signature(obj: bpy.types.Object) -> tuple:
    return (obj.data.as_pointer(), len(obj.data.vertices), len(obj.data.polygons),
            tuple(round(value, 8) for row in obj.matrix_world for value in row))


def _surface(obj: bpy.types.Object, depsgraph=None) -> _SurfaceCache:
    key = obj.as_pointer()
    signature = _signature(obj)
    cached = _surface_cache.get(key)
    if cached is not None and cached.signature == signature:
        return cached
    depsgraph = depsgraph or bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        if len(mesh.vertices) != len(obj.data.vertices):
            raise ValueError("Topology-changing modifiers are not supported for biome sampling")
        mesh.calc_loop_triangles()
        points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        triangles = [tuple(triangle.vertices) for triangle in mesh.loop_triangles]
        bvh = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    finally:
        evaluated.to_mesh_clear()
    cached = _SurfaceCache(bvh, points, triangles, signature)
    _surface_cache[key] = cached
    return cached


def sample_biome_weight_world(obj: bpy.types.Object, biome_id: str, world_position,
                               depsgraph=None) -> float:
    issues = validate_biome_target(obj)
    if issues:
        raise ValueError(issues[0])
    definition = definition_by_id(bpy.context.scene, biome_id)
    if definition is None:
        raise KeyError(f"Unknown biome id {biome_id}")
    surface = _surface(obj, depsgraph)
    location, _normal, triangle_index, _distance = surface.bvh.find_nearest(Vector(world_position))
    if location is None or triangle_index is None:
        raise ValueError("Biome target has no sampleable surface")
    indices = surface.triangles[triangle_index]
    weights = biome_contract.barycentric_weights(
        tuple(location), *(tuple(surface.points[index]) for index in indices)
    )
    values = [get_biome_weight_at_vertex(obj.data, definition, index) for index in indices]
    return biome_contract.clamp_weight(sum(weight * value for weight, value in zip(weights, values)))


def sample_all_biomes_world(obj: bpy.types.Object, world_position, depsgraph=None) -> dict[str, float]:
    return {
        definition.stable_id: sample_biome_weight_world(obj, definition.stable_id, world_position, depsgraph)
        for definition in biome_definitions(bpy.context.scene) if definition.enabled
    }


def validate_biome_data(scene: bpy.types.Scene, obj: bpy.types.Object) -> list[str]:
    issues = validate_biome_target(obj)
    if issues:
        return issues
    seen_ids, seen_names = set(), set()
    for definition in biome_definitions(scene):
        if definition.stable_id in seen_ids:
            issues.append(f"Duplicate biome id: {definition.stable_id}")
        seen_ids.add(definition.stable_id)
        if definition.attribute_name in seen_names:
            issues.append(f"Duplicate biome attribute: {definition.attribute_name}")
        seen_names.add(definition.attribute_name)
        attribute = obj.data.attributes.get(definition.attribute_name)
        if attribute is None:
            issues.append(f"Missing attribute: {definition.attribute_name}")
            continue
        if attribute.domain != "POINT" or attribute.data_type != "FLOAT":
            issues.append(f"Invalid attribute type: {definition.attribute_name}")
            continue
        if len(attribute.data) != len(obj.data.vertices):
            issues.append(f"Attribute length mismatch: {definition.attribute_name}")
            continue
        for item in attribute.data:
            value = float(item.value)
            if not math.isfinite(value) or value < 0.0 or value > 1.0:
                issues.append(f"Invalid weight in {definition.attribute_name}")
                break
    return issues


def build_biome_manifest(scene: bpy.types.Scene, obj: bpy.types.Object) -> dict:
    layers = ({
        "stable_id": definition.stable_id,
        "name": definition.name,
        "attribute_name": definition.attribute_name,
        "export_enabled": definition.export_enabled,
    } for definition in biome_definitions(scene))
    return biome_contract.build_manifest(obj.name, layers)


def depsgraph_update_post(_scene, depsgraph) -> None:
    for update in depsgraph.updates:
        value = update.id
        if isinstance(value, bpy.types.Object):
            invalidate_biome_cache(value)
        elif isinstance(value, bpy.types.Mesh):
            mesh_pointer = value.as_pointer()
            for key, cached in list(_surface_cache.items()):
                if cached.signature[0] == mesh_pointer:
                    _surface_cache.pop(key, None)
