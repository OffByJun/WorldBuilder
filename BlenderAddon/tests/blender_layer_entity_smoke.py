"""Headless smoke for vertical authoring layers and DOTS entity placement export."""
from pathlib import Path
import json,sys,tempfile
import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import asset_library,contract,exporter,layers,overlay,state

worldbuilder_chunks.register();scene=bpy.context.scene;bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
grid=scene.worldbuilder_chunks;grid.developer_override=True;grid.chunk_size=16;grid.query_cell_size=4;grid.world_id="LayerSmoke";grid.layer_height=8;grid.layer_base_z=0;grid.layer_count=6;scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1

assert layers.active_floor_z(grid)==0.0
assert bpy.ops.worldbuilder.layer_step(delta=2)=={"FINISHED"};assert grid.active_layer==2;assert layers.active_bounds_z(grid)==(16.0,24.0)
assert bpy.ops.worldbuilder.layer_step(delta=99)=={"FINISHED"};assert grid.active_layer==5,grid.active_layer
grid.active_layer=2;assert overlay.plane_z(grid)==16.0+grid.overlay_z;grid.layer_follow_grid=False;assert overlay.plane_z(grid)==grid.overlay_z;grid.layer_follow_grid=True

source_collection=bpy.data.collections.new("WB_ASSET_Kelp");scene.collection.children.link(source_collection);bpy.ops.mesh.primitive_cube_add(size=1);source=bpy.context.object
for owner in list(source.users_collection):owner.objects.unlink(source)
source_collection.objects.link(source)
library=scene.worldbuilder_structure_library;library.registry_file=str(Path(tempfile.gettempdir())/"wb_entity_smoke.registry.json");library.draft_collection=source_collection;library.draft_asset_id="creature.kelp_crab.01";library.draft_name_ko="켈프 크랩";library.draft_name_en="Kelp Crab";library.draft_placement_kind="ENTITY";library.draft_entity_prefab_id=12;library.draft_entity_kind="Creature";library.draft_entity_persistent=True;library.draft_entity_region_streamed=True;library.draft_entity_lifetime=0
assert bpy.ops.worldbuilder.structure_asset_register()=={"FINISHED"}
scene.cursor.location=(4,4,18);assert bpy.ops.worldbuilder.structure_place_cursor()=={"FINISHED"};instance=bpy.context.object
assert instance.name.startswith("ENT_"),instance.name
assert instance.worldbuilder_chunk.role=="ENTITY"
assert instance.worldbuilder_chunk.entity_prefab_id==12
assert instance.worldbuilder_chunk.entity_kind=="Creature"
assert exporter.object_layer(instance,grid)==2,exporter.object_layer(instance,grid)

grid.layer_lock_placement=True;scene.cursor.location=(5,5,999);assert bpy.ops.worldbuilder.structure_place_cursor()=={"FINISHED"};clamped=bpy.context.object;assert clamped.location.z==24.0,clamped.location.z;bpy.data.objects.remove(clamped,do_unlink=True);grid.layer_lock_placement=False

grid.layer_isolate="ACTIVE";grid.active_layer=0;assert instance.hide_viewport is True
grid.active_layer=2;assert instance.hide_viewport is False
grid.layer_isolate="OFF";assert layers.HIDDEN_MARKER not in instance.keys()

instance.select_set(True);bpy.context.view_layer.objects.active=instance;assert bpy.ops.worldbuilder.layer_snap_selection()=={"FINISHED"};assert abs(instance.matrix_world.translation.z-16.0)<1e-6
assert bpy.ops.worldbuilder.layer_move_selection(delta=1)=={"FINISHED"};assert abs(instance.matrix_world.translation.z-24.0)<1e-6;assert exporter.object_layer(instance,grid)==3

grid.export_root=str(Path(tempfile.gettempdir())/"wb_layer_entity_export");state.set_active_chunk(grid,(0,0));results,issues=exporter.export_coordinates(bpy.context,grid,[(0,0)],force=True)
assert not [issue for issue in issues if issue[0]=="ERROR"],issues
manifest_path=Path(results[0][2]);placements=json.loads((manifest_path.parent/"placements.json").read_text(encoding="utf-8"))
record=next(item for item in placements["objects"] if item["role"]=="ENTITY")
assert record["assetId"]=="creature.kelp_crab.01"
assert record["layer"]==3,record["layer"]
assert record["entity"]=={"prefabId":12,"kind":"Creature","flags":["Persistent","RegionStreamed"],"lifetimeSeconds":0.0},record["entity"]
assert contract.layer_name(record["layer"])=="LV_+0003"

print("WB_LAYER_ENTITY_OK",record["stableId"]);worldbuilder_chunks.unregister()
