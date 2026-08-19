"""Run with: blender --background --factory-startup --python blender_biome_smoke.py"""

from pathlib import Path
import sys
import tempfile

import bpy

ADDON_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADDON_ROOT))

import worldbuilder_chunks
from worldbuilder_chunks import biome


worldbuilder_chunks.register()
bpy.ops.mesh.primitive_grid_add(x_subdivisions=4, y_subdivisions=4, size=4.0)
terrain = bpy.context.active_object
terrain.name = "Terrain_BiomeSmoke"
assert bpy.ops.worldbuilder.initialize_default_biomes() == {"FINISHED"}

settings = bpy.context.scene.worldbuilder_biomes
kelp_index = next(index for index, layer in enumerate(settings.layers) if layer.name == "Kelp")
settings.active_index = kelp_index
fill = bpy.ops.worldbuilder.fill_biome_layer(value=0.75)
assert fill == {"FINISHED"}
kelp = settings.layers[kelp_index]
biome.set_biome_weight_at_vertex(terrain.data, kelp, 0, 0.25)
assert bpy.ops.worldbuilder.validate_biomes() == {"FINISHED"}

sample = biome.sample_biome_weight_world(terrain, kelp.stable_id, terrain.matrix_world @ terrain.data.vertices[0].co)
assert abs(sample - 0.25) < 1e-4

linked_copy = terrain.copy()
bpy.context.scene.collection.objects.link(linked_copy)
assert linked_copy.data == terrain.data
assert linked_copy.data.attributes.get(kelp.attribute_name) is not None

terrain_settings = bpy.context.scene.nex_terrain_settings
terrain_settings.resolution = 8
terrain_settings.chunks_x = 1
terrain_settings.chunks_y = 1
assert bpy.ops.nex.generate_stylized_terrain() == {"FINISHED"}
generated = next(obj for obj in bpy.data.objects
                 if obj.get("nex_stylized_terrain_kind") == "TERRAIN")
assert generated.get("wb_biome_target") is True
assert generated.get("wb_biome_schema_version") == 1

path = str(Path(tempfile.gettempdir()) / "worldbuilder_biome_smoke.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
bpy.ops.wm.open_mainfile(filepath=path)
reloaded = bpy.data.objects["Terrain_BiomeSmoke"]
assert reloaded.get("wb_biome_target") is True
assert reloaded.data.attributes.get("WB_BIOME_KELP") is not None
print("WorldBuilder biome smoke passed")
