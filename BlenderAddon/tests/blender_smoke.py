"""Run with Blender --background --factory-startup --python blender_smoke.py."""

import json
import os
import sys

ADDON_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ADDON_ROOT)

import bpy
import worldbuilder_chunks
from worldbuilder_chunks import exporter


def main():
    output = os.environ["WB_SMOKE_OUTPUT"]
    worldbuilder_chunks.register()
    try:
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1.0
        settings = scene.worldbuilder_chunks
        settings.world_id = "SmokeWorld"
        settings.chunk_size = 128.0
        settings.chunks_per_region = 4
        settings.developer_override = True
        settings.export_root = output

        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        bpy.ops.mesh.primitive_cube_add(location=(2.0, 2.0, 1.0))
        cube = bpy.context.object
        cube.name = "SM_SmokeCube"
        cube.worldbuilder_chunk.role = "GEOMETRY"
        marker = bpy.data.objects.new("SPAWN_Smoke", None)
        scene.collection.objects.link(marker)
        marker.location = (3.0, 4.0, 2.0)
        marker.worldbuilder_chunk.role = "MARKER"
        marker.worldbuilder_chunk.marker_type = "SmokeSpawn"

        results, issues = exporter.export_dirty_chunks(bpy.context, settings, force=True)
        assert not [issue for issue in issues if issue[0] == "ERROR"], issues
        assert len(results) == 1 and results[0][1] == "EXPORTED", results
        manifest_path = results[0][2]
        with open(manifest_path, "r", encoding="utf-8") as stream:
            manifest = json.load(stream)
        assert manifest["version"] == 2
        assert manifest["worldId"] == "SmokeWorld"
        assert manifest["chunk"] == {"x": 0, "z": 0}
        assert len(manifest["contentHash"]) == 64
        assert os.path.isfile(os.path.join(os.path.dirname(manifest_path), "geometry.fbx"))
        placements_path = os.path.join(os.path.dirname(manifest_path), "placements.json")
        with open(placements_path, "r", encoding="utf-8") as stream:
            placements = json.load(stream)
        assert placements["objects"][0]["matrix"][3] == 3.0
        assert placements["objects"][0]["matrix"][7] == 2.0
        assert placements["objects"][0]["matrix"][11] == 4.0
        repeated, repeated_issues = exporter.export_dirty_chunks(bpy.context, settings, force=False)
        assert not [issue for issue in repeated_issues if issue[0] == "ERROR"]
        assert len(repeated) == 1 and repeated[0][1] == "SKIPPED", repeated
        print("WB_EXPORT_SMOKE_OK", manifest_path)
    finally:
        worldbuilder_chunks.unregister()


if __name__ == "__main__":
    main()
