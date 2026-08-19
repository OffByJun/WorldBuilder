"""Headless Blender smoke for WorldBuilder production modules."""
from pathlib import Path
import sys
import tempfile
import bpy

ADDON_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import bake as bake_module, biome, exporter, scatter, seam_ui, spline_authoring

worldbuilder_chunks.register()
scene = bpy.context.scene
grid = scene.worldbuilder_chunks
grid.developer_override = True
grid.chunk_size = 16
grid.query_cell_size = 4
grid.world_id = "SmokeWorld"

bpy.ops.mesh.primitive_grid_add(x_subdivisions=11, y_subdivisions=11, size=10)
terrain = bpy.context.object
terrain.name = "SmokeTerrain"
terrain.location = (8, 8, 0)
terrain.worldbuilder_chunk.stable_id = "terrain-smoke"
terrain["wb_terrain"] = True
terrain_vertex_count = len(terrain.data.vertices)
assert bpy.ops.worldbuilder.initialize_default_biomes() == {"FINISHED"}
biomes = scene.worldbuilder_biomes
kelp = next(layer for layer in biomes.layers if layer.name == "Kelp")
biome.fill_biome_attribute(terrain.data, kelp, 1.0)
scene.worldbuilder_biome_brush.preview_mode = "ACTIVE"
scene.worldbuilder_biome_brush.preview_mode = "OFF"

source_collection = bpy.data.collections.new("SmokeAssets")
scene.collection.children.link(source_collection)
bpy.ops.mesh.primitive_cube_add(size=1)
source = bpy.context.object
source.name = "RockAsset"
source["asset_id"] = "rock-asset"
for collection in list(source.users_collection):
    collection.objects.unlink(source)
source_collection.objects.link(source)
layer = scene.worldbuilder_scatter.layers.add()
layer.stable_id = "scatter-smoke"
layer.name = "Rocks"
layer.target_object = terrain
layer.source_collection = source_collection
layer.seed = 42
layer.density = .2
layer.max_instances = 20
layer.minimum_distance = .5
layer.biome_id = kelp.stable_id
layer.biome_min_weight = .5
count = scatter.generate(bpy.context, layer, True)
assert count > 0
generated_collection = bpy.data.collections["WB_SCATTER_scatter-smoke"]
first = sorted((obj["wb_scatter_instance_id"], tuple(round(v, 5) for v in obj.location)) for obj in generated_collection.all_objects)
assert all(obj.data is source.data for obj in generated_collection.all_objects)
scatter.generate(bpy.context, layer, True)
second = sorted((obj["wb_scatter_instance_id"], tuple(round(v, 5) for v in obj.location)) for obj in generated_collection.all_objects)
assert first == second
# Tombstones prevent an explicitly excluded deterministic candidate from returning.
excluded_id = first[0][0]
tombstone = layer.tombstones.add()
tombstone.instance_id = excluded_id
tombstone.layer_id = layer.stable_id
bpy.data.objects.remove(next(obj for obj in generated_collection.all_objects if obj["wb_scatter_instance_id"] == excluded_id), do_unlink=True)
scatter.generate(bpy.context, layer, True)
assert excluded_id not in {obj["wb_scatter_instance_id"] for obj in generated_collection.all_objects}
exclusion_obj = bpy.data.objects.new("ScatterExclusion", None)
scene.collection.objects.link(exclusion_obj)
exclusion_obj.location = terrain.location
exclusion_obj.scale = (20, 20, 20)
exclusion_obj.worldbuilder_exclusion.shape = "SPHERE"
exclusion_obj.worldbuilder_exclusion.stable_id = "exclusion-smoke"
assert scatter.generate(bpy.context, layer, True) == 0
bpy.data.objects.remove(exclusion_obj, do_unlink=True)
assert scatter.generate(bpy.context, layer, True) > 0

assert bpy.ops.worldbuilder.spline_create(type="CANYON") == {"FINISHED"}
spline = bpy.context.object
spline.name = "SmokeSpline"
spline.location = (8, 8, 0)
props = spline.worldbuilder_spline
props.sample_spacing = .5
modifier = props.modifiers.add()
modifier.stable_id = "carve-smoke"
modifier.type = "TERRAIN_CARVE"
modifier.target = terrain
modifier.width = 2
modifier.depth = 1
modifier.preserve_boundary = False
before = max(v.co.z for v in terrain.data.vertices)
assert spline_authoring.rebuild_carves(scene, spline) == 1
after = min(v.co.z for v in terrain.data.vertices)
assert after < before
for kind in ("PATH", "CLIFF", "CAVE"):
    generated = spline_authoring.generate_mesh(scene, spline, kind)
    assert generated.data.polygons
    assert generated.data.uv_layers.active is not None
assert len([obj for obj in scene.objects if obj.get("wb_generated_kind") == "CAVE_PORTAL"]) == 2
spline.location = (16, 8, 0)
spline_authoring.generate_mesh(scene, spline, "PATH")
path_chunks = {(obj.get("wb_chunk_x"), obj.get("wb_chunk_z")) for obj in scene.objects if obj.get("wb_generated_kind") == "PATH"}
assert path_chunks == {(0, 0), (1, 0)}, path_chunks
spline.location = (8, 8, 0)

bpy.context.view_layer.objects.active = terrain
terrain.select_set(True)
assert bpy.ops.worldbuilder.vertex_bake() == {"FINISHED"}
attribute = terrain.data.color_attributes.get("WB_ShaderData")
assert attribute and attribute.domain == "POINT"
assert "channels" in terrain["wb_vertex_attribute_contract"]

stamp_root = Path(tempfile.gettempdir()) / "worldbuilder_stamp_smoke"
stamp_root.mkdir(exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
terrain.select_set(True)
bpy.context.view_layer.objects.active = terrain
scene.worldbuilder_stamps.library_folder = str(stamp_root)
scene.worldbuilder_stamps.name = "TerrainStamp"
assert bpy.ops.worldbuilder.stamp_save() == {"FINISHED"}
assert Path(scene.worldbuilder_stamps.active_file).exists()
scene.cursor.location = (20, 0, 0)
assert bpy.ops.worldbuilder.stamp_place() == {"FINISHED"}
assert any(obj.get("wb_stamp_instance_id") for obj in scene.objects)

assert bpy.ops.worldbuilder.analysis_recalculate() == {"FINISHED"}
assert "cached cells" in scene.worldbuilder_analysis.last_report

# Equal-resolution adjacent chunk seam detects, stitches, and revalidates.
grid.chunk_size = 2
def make_chunk_terrain(name, coordinate, z_offset=0.0):
    mesh = bpy.data.meshes.new(name + "Mesh")
    x0 = coordinate[0] * 2
    mesh.from_pydata([(x0, 0, z_offset), (x0 + 2, 0, z_offset), (x0 + 2, 2, z_offset), (x0, 2, z_offset)], [], [(0, 1, 2, 3)])
    obj = bpy.data.objects.new(name, mesh)
    obj["wb_terrain"] = True
    obj.worldbuilder_chunk.stable_id = name
    exporter.ensure_chunk_collection(scene, coordinate).objects.link(obj)
    return obj
seam_a = make_chunk_terrain("SeamA", (30, 0), 0)
seam_b = make_chunk_terrain("SeamB", (31, 0), 1)
scene.worldbuilder_seams.scope = "ALL"
seam_ui.validate(scene)
result = next(item for item in scene.worldbuilder_seams.results if item.object_a == "SeamA" and item.object_b == "SeamB")
assert result.status == "POSITION_SEAM"
scene.worldbuilder_seams.active_index = list(scene.worldbuilder_seams.results).index(result)
assert bpy.ops.worldbuilder.seam_stitch() == {"FINISHED"}
result = next(item for item in scene.worldbuilder_seams.results if item.object_a == "SeamA" and item.object_b == "SeamB")
assert result.maximum < 1e-6
grid.chunk_size = 16

bpy.context.view_layer.objects.active = terrain
bpy.ops.object.select_all(action="DESELECT")
terrain.select_set(True)
bake_root = Path(tempfile.gettempdir()) / "worldbuilder_bake_smoke"
bake_root.mkdir(exist_ok=True)
profile = scene.worldbuilder_bake.profiles.add()
profile.stable_id = "bake-smoke"
profile.output_root = str(bake_root)
scene.worldbuilder_bake.scope = "ACTIVE_OBJECT"
assert bpy.ops.worldbuilder.bake_run() == {"FINISHED"}
assert bpy.data.collections.get("WB_BAKE_bake-smoke")
bake_manifests = list(bake_root.glob("CH_*.bake.json"))
assert bake_manifests
import json
with bake_manifests[0].open("r", encoding="utf-8") as stream:
    bake_payload = json.load(stream)
assert bake_payload["vertexAttributes"][0]["channels"]["B"] == "BIOME_WEIGHT"
assert terrain.name == "SmokeTerrain" and len(terrain.data.vertices) == terrain_vertex_count
box_profile = scene.worldbuilder_bake.profiles.add()
box_profile.stable_id = "box-collider-smoke"
box_profile.generate_lods = False
box_profile.collider_mode = "BOX"
box_outputs = bake_module.bake_object(bpy.context, terrain, box_profile)
box_collider = next(obj for obj in box_outputs if obj.get("wb_collider_type") == "BOX")
assert len(box_collider.data.vertices) == 8

path = str(Path(tempfile.gettempdir()) / "worldbuilder_production_smoke.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
bpy.ops.wm.open_mainfile(filepath=path)
assert bpy.data.objects.get("SmokeTerrain")
assert bpy.data.objects["SmokeTerrain"].data.color_attributes.get("WB_ShaderData")
print("WorldBuilder production smoke passed")
