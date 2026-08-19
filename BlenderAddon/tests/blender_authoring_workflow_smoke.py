"""Headless smoke for authoritative terrain, Sculpt Session, and structure assets."""
from pathlib import Path
import json,sys,tempfile
import bpy
from mathutils import Vector

ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import asset_library,chunk_terrain,exporter,localization,sculpt_session,state

worldbuilder_chunks.register();scene=bpy.context.scene;bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False);grid=scene.worldbuilder_chunks;grid.developer_override=True;grid.chunk_size=16;grid.query_cell_size=4;grid.world_id="AuthoringSmoke";grid.ui_language="KO";scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1
state.set_selected_chunks(grid,{(0,0),(1,0)});state.set_active_chunk(grid,(0,0));terrain_settings=scene.worldbuilder_chunk_terrain;terrain_settings.scope="SELECTED";terrain_settings.cells=8;terrain_settings.seed=9;terrain_settings.apply_palette=False
assert bpy.ops.worldbuilder.generate_chunk_terrain()=={"FINISHED"};left=chunk_terrain.terrain_for_coordinate(scene,(0,0));right=chunk_terrain.terrain_for_coordinate(scene,(1,0));assert left and right
for row in range(9):
    a=left.matrix_world@left.data.vertices[row*9+8].co;b=right.matrix_world@right.data.vertices[row*9].co;assert (a-b).length<1e-6
sculpt_settings=scene.worldbuilder_sculpt;sculpt_settings.scope="SELECTED";sculpt_settings.neighbor_ring=0;proxy=sculpt_session.begin(scene);interior=min(proxy.data.vertices,key=lambda vertex:(vertex.co-Vector((8,8,0))).xy.length);interior.co.z+=1.5;changed=sculpt_session.apply(scene);assert len(changed)==2;assert left.data.attributes.get("wb_sculpt_delta") is not None
for row in range(9):
    a=left.matrix_world@left.data.vertices[row*9+8].co;b=right.matrix_world@right.data.vertices[row*9].co;assert (a-b).length<1e-6
source_collection=bpy.data.collections.new("WB_ASSET_StoneArch");scene.collection.children.link(source_collection);bpy.ops.mesh.primitive_cube_add(size=2);source=bpy.context.object
for owner in list(source.users_collection):owner.objects.unlink(source)
source_collection.objects.link(source);library=scene.worldbuilder_structure_library;library.registry_file=str(Path(tempfile.gettempdir())/"wb_structure_smoke.registry.json");library.draft_collection=source_collection;library.draft_asset_id="environment.stone_arch.01";library.draft_name_ko="자연석 아치";library.draft_name_en="Stone Arch";library.draft_ownership="REGION"
assert bpy.ops.worldbuilder.structure_asset_register()=={"FINISHED"};assert source.worldbuilder_chunk.role=="GLOBAL",source.worldbuilder_chunk.role;scene.cursor.location=(2,2,0);assert bpy.ops.worldbuilder.structure_place_cursor()=={"FINISHED"};instance=bpy.context.object;assert instance.instance_collection==source_collection;assert instance.worldbuilder_chunk.role=="INSTANCE";assert instance.worldbuilder_chunk.asset_id=="environment.stone_arch.01";assert instance.get("wb_streaming_ownership")=="REGION";assert localization.tr("asset_library",scene)=="조형물 라이브러리"
grid.export_root=str(Path(tempfile.gettempdir())/"wb_authoring_export");results,issues=exporter.export_coordinates(bpy.context,grid,[(0,0)],force=True);assert not [issue for issue in issues if issue[0]=="ERROR"],issues;manifest_path=Path(results[0][2]);placements=json.loads((manifest_path.parent/"placements.json").read_text(encoding="utf-8"));record=next(item for item in placements["objects"] if item["assetId"]=="environment.stone_arch.01");assert {item["key"]:item["value"] for item in record["properties"]}["streaming_ownership"]=="REGION"
print("WB_AUTHORING_WORKFLOW_OK",len(left.data.vertices),instance.name);worldbuilder_chunks.unregister()
