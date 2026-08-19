"""Headless smoke for entity scatter and the Unity entity catalog mirror."""
from pathlib import Path
import json,sys,tempfile
import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import asset_library,entity_catalog,exporter,scatter,state

worldbuilder_chunks.register();scene=bpy.context.scene;bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
grid=scene.worldbuilder_chunks;grid.developer_override=True;grid.chunk_size=32;grid.query_cell_size=8;grid.world_id="ScatterSmoke";grid.layer_height=8;grid.layer_count=8;scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1

temp=Path(tempfile.gettempdir())
catalog_path=temp/"wb_entity_catalog_smoke.json"
catalog_path.write_text(json.dumps({"schemaVersion":1,"entities":[
    {"prefabId":7,"name":"KelpCrab","kind":"Creature","flags":["RegionStreamed"],"lifetimeSeconds":0},
    {"prefabId":12,"name":"IronNode","kind":"Resource","flags":["Persistent","RegionStreamed"],"lifetimeSeconds":0},
]}),encoding="utf-8")
catalog=scene.worldbuilder_entity_catalog;catalog.catalog_file=str(catalog_path)
assert bpy.ops.worldbuilder.entity_catalog_reload()=={"FINISHED"}
assert len(catalog.items)==2,len(catalog.items)
assert entity_catalog.is_loaded(scene)
assert entity_catalog.is_known(scene,12) and not entity_catalog.is_known(scene,99)
assert [identifier for identifier,_,_ in entity_catalog.enum_items(None,bpy.context)]==["7","12"]

source_collection=bpy.data.collections.new("WB_ASSET_IronNode");scene.collection.children.link(source_collection);bpy.ops.mesh.primitive_cube_add(size=1);source=bpy.context.object
for owner in list(source.users_collection):owner.objects.unlink(source)
source_collection.objects.link(source)
library=scene.worldbuilder_structure_library;library.registry_file=str(temp/"wb_entity_scatter.registry.json");library.draft_collection=source_collection;library.draft_asset_id="resource.iron_node.01";library.draft_name_en="Iron Node";library.draft_placement_kind="ENTITY"
library.draft_entity_catalog_pick="12"
assert library.draft_entity_prefab_id==12,library.draft_entity_prefab_id
assert library.draft_entity_kind=="Resource",library.draft_entity_kind
assert bpy.ops.worldbuilder.structure_asset_register()=={"FINISHED"}
registered=asset_library.find_asset(scene,"resource.iron_node.01");assert registered is not None and registered.placement_kind=="ENTITY"

bpy.ops.mesh.primitive_grid_add(size=32,x_subdivisions=16,y_subdivisions=16,location=(16,16,0));terrain=bpy.context.object;terrain.name="TerrainSmoke"
settings=scene.worldbuilder_scatter;assert bpy.ops.worldbuilder.scatter_add()=={"FINISHED"};layer=settings.layers[settings.active_index]
layer.target_object=terrain;layer.source_collection=source_collection;layer.density=0.05;layer.minimum_distance=1.0;layer.seed=5;layer.max_instances=200
assert bpy.ops.worldbuilder.scatter_apply()=={"FINISHED"},layer.statistics
entry=layer.assets[0];entry.asset_id="resource.iron_node.01"
role,item=scatter.resolve_placement(scene,entry);assert role=="ENTITY",role;assert item is not None
assert bpy.ops.worldbuilder.scatter_apply()=={"FINISHED"},layer.statistics

collection=bpy.data.collections.get(f"WB_SCATTER_{layer.stable_id}");instances=[obj for obj in collection.all_objects if obj.get("wb_scatter_instance_id")]
assert instances,"scatter produced no instances"
for obj in instances:
    assert obj.worldbuilder_chunk.role=="ENTITY",obj.worldbuilder_chunk.role
    assert obj.worldbuilder_chunk.entity_prefab_id==12
    assert obj.worldbuilder_chunk.entity_kind=="Resource"
    assert obj.worldbuilder_chunk.entity_persistent and obj.worldbuilder_chunk.entity_region_streamed
    assert exporter.object_role(obj)=="ENTITY"

entry.role="INSTANCE";assert scatter.resolve_placement(scene,entry)[0]=="INSTANCE"
entry.role="AUTO"

grid.export_root=str(temp/"wb_entity_scatter_export");state.set_active_chunk(grid,(0,0));results,issues=exporter.export_coordinates(bpy.context,grid,[(0,0)],force=True)
assert not [issue for issue in issues if issue[0]=="ERROR"],issues
manifest_path=Path(results[0][2]);placements=json.loads((manifest_path.parent/"placements.json").read_text(encoding="utf-8"))
entities=[record for record in placements["objects"] if record["role"]=="ENTITY"]
assert entities,"no entity placements exported"
assert all(record["entity"]["prefabId"]==12 for record in entities)
assert all(record["entity"]["flags"]==["Persistent","RegionStreamed"] for record in entities)

instances[0].worldbuilder_chunk.entity_prefab_id=99
warnings=[issue for issue in exporter.validate_scene(scene,grid,[instances[0]]) if issue[0]=="WARNING" and "not in the loaded Unity catalog" in issue[1]]
assert warnings,"unknown catalog id should warn"

print("WB_ENTITY_SCATTER_OK",len(entities));worldbuilder_chunks.unregister()
