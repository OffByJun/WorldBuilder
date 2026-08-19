"""Headless smoke for depth bands, bookmarks, layer-aware analysis, and traversal probes."""
from pathlib import Path
import json,sys,tempfile
import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import analysis,bookmarks,contract,finishing_tools,state,traversal,water

worldbuilder_chunks.register();scene=bpy.context.scene;bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
grid=scene.worldbuilder_chunks;grid.developer_override=True;grid.chunk_size=32;grid.query_cell_size=8;grid.world_id="CraftSmoke";grid.layer_height=10;grid.layer_base_z=0;grid.layer_count=6
scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1

# --- 4. depth bands -------------------------------------------------------
depth=scene.worldbuilder_water;depth.sea_level=50;depth.shallow_depth=20;depth.mid_depth=60;depth.deep_depth=120
assert water.depth_at(scene,50)==0.0
assert water.depth_at(scene,30)==20.0
assert water.band_name(scene,60)=="Surface",water.band_name(scene,60)
assert water.band_name(scene,30)=="Shallow",water.band_name(scene,30)
assert water.band_name(scene,0)=="Mid",water.band_name(scene,0)
assert water.band_name(scene,-100)=="Deep",water.band_name(scene,-100)
assert water.band_name(scene,-200)=="Abyss",water.band_name(scene,-200)
assert bpy.ops.worldbuilder.water_cursor_to_band(band=1)=={"FINISHED"};assert abs(scene.cursor.location.z-30.0)<1e-6,scene.cursor.location.z
assert bpy.ops.worldbuilder.water_sync_vertex_bake()=={"FINISHED"};assert scene.worldbuilder_vertex_bake.sea_level==50

# --- 5. bookmarks ---------------------------------------------------------
marks=scene.worldbuilder_bookmarks
scene.cursor.location=(70.0,40.0,25.0)
assert bpy.ops.worldbuilder.bookmark_add()=={"FINISHED"}
item=marks.items[0]
assert (item.chunk_x,item.chunk_z)==(2,1),(item.chunk_x,item.chunk_z)
assert item.layer_index==2,item.layer_index
assert item.name==contract.chunk_name((2,1)),item.name
marks.jump_chunk_x=-1;marks.jump_chunk_z=3
assert bpy.ops.worldbuilder.goto_chunk()=={"FINISHED"}
assert state.explicit_active_chunk(grid)==(-1,3),state.explicit_active_chunk(grid)
assert abs(scene.cursor.location.x-(-16.0))<1e-6 and abs(scene.cursor.location.y-112.0)<1e-6,tuple(scene.cursor.location)
grid.active_layer=0
assert bpy.ops.worldbuilder.bookmark_jump()=={"FINISHED"}
assert grid.active_layer==2,grid.active_layer
assert state.explicit_active_chunk(grid)==(2,1)
assert bpy.ops.worldbuilder.bookmark_remove()=={"FINISHED"};assert len(marks.items)==0

# --- geometry: floor at z=0 plus a low ceiling slab over half the chunk ----
bpy.ops.mesh.primitive_grid_add(size=32,x_subdivisions=8,y_subdivisions=8,location=(16,16,0));floor=bpy.context.object;floor.name="Floor"
bpy.ops.mesh.primitive_cube_add(size=2,location=(8,16,1.0));slab=bpy.context.object;slab.name="Ceiling";slab.scale=(8,16,0.1)
bpy.context.view_layer.update()

# --- 1. layer-aware analysis ---------------------------------------------
scene.cursor.location=(16.0,16.0,0.0)
settings=scene.worldbuilder_analysis;settings.scope_radius=256;settings.resolution=8;settings.mode="OBJECT_PIVOT_DENSITY";settings.layer_filter="ACTIVE"
values=finishing_tools.recalculate_analysis(scene)
cells=finishing_tools._analysis_cells
assert cells,"analysis produced no cells"
assert all(len(key)==3 for key in cells),"analysis keys must be (x, y, layer)"
assert {key[2] for key in cells}=={0},sorted({key[2] for key in cells})
assert values and all(record["layer"]==0 for record in values)
export_path=Path(tempfile.gettempdir())/"wb_analysis_layer.json";settings.export_path=str(export_path)
assert bpy.ops.worldbuilder.analysis_export()=={"FINISHED"}
document=json.loads(export_path.read_text(encoding="utf-8"))
assert document["schemaVersion"]==2,document["schemaVersion"]
assert all("layer" in record and len(record["coordinate"])==2 for record in document["cells"])

lifted=bpy.data.objects.new("Lifted",None);scene.collection.objects.link(lifted);lifted.location=(16,16,35);bpy.context.view_layer.update()
finishing_tools.recalculate_analysis(scene)
cells=finishing_tools._analysis_cells
assert 3 in {key[2] for key in cells},sorted({key[2] for key in cells})

# --- 2. traversal probes --------------------------------------------------
state.set_active_chunk(grid,(0,0));grid.active_layer=0
probe=scene.worldbuilder_traversal;probe.profile="WALK";probe.player_height=1.8;probe.probe_spacing=4;probe.maximum_slope=45;probe.scope="ACTIVE_CHUNK"
assert bpy.ops.worldbuilder.traversal_scan()=={"FINISHED"},probe.report
statuses=[status for _,status in traversal._results]
counts=analysis.summarize(statuses)
assert counts["total"]==64,counts["total"]
assert counts[analysis.OK]>0,"expected walkable probes on the open floor"
assert counts[analysis.LOW_CEILING]>0,f"expected the slab to block headroom: {counts}"
assert bpy.ops.worldbuilder.traversal_cursor_to_failure()=={"FINISHED"}

probe.player_height=0.5
assert bpy.ops.worldbuilder.traversal_scan()=={"FINISHED"}
relaxed=analysis.summarize([status for _,status in traversal._results])
assert relaxed[analysis.LOW_CEILING]==0,f"a shorter player should fit under the slab: {relaxed}"

grid.active_layer=4
assert bpy.ops.worldbuilder.traversal_scan()=={"FINISHED"}
empty=analysis.summarize([status for _,status in traversal._results])
assert empty[analysis.NO_GROUND]==empty["total"],f"empty band must report no ground: {empty}"

grid.active_layer=0;probe.profile="SWIM";probe.player_radius=4.5
assert bpy.ops.worldbuilder.traversal_scan()=={"FINISHED"}
swim=analysis.summarize([status for _,status in traversal._results])
assert swim[analysis.NARROW]+swim[analysis.BLOCKED]>0,f"the slab should narrow the swim band: {swim}"
assert bpy.ops.worldbuilder.traversal_clear()=={"FINISHED"};assert not traversal._results

print("WB_WORLDCRAFT_OK",counts["total"],relaxed[analysis.OK]);worldbuilder_chunks.unregister()
