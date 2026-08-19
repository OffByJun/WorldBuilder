"""Panel-facing chunk queries must never write IDs to Blender data-blocks."""
from pathlib import Path
import sys

import bpy

ADDON_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADDON_ROOT))

import worldbuilder_chunks
from worldbuilder_chunks import exporter

worldbuilder_chunks.register()
scene = bpy.context.scene
scene.worldbuilder_chunks.developer_override = True
mesh = bpy.data.meshes.new("ReadonlyQueryMesh")
obj = bpy.data.objects.new("ReadonlyQueryObject", mesh)
scene.collection.objects.link(obj)
assert obj.worldbuilder_chunk.stable_id == ""
found = exporter.objects_in_chunk(scene, scene.worldbuilder_chunks, (0, 0))
assert obj in found
assert obj.worldbuilder_chunk.stable_id == ""
worldbuilder_chunks.unregister()
print("WB_READONLY_CHUNK_QUERY_OK")
