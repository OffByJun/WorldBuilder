"""Generate a full 12-asset reef sheet under an interactive authoring budget."""
from pathlib import Path
import sys,time
import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks

worldbuilder_chunks.register();bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
settings=bpy.context.scene.worldbuilder_reef;settings.asset_kind="COMPLETE";settings.asset_id="performance.reef";settings.width=8;settings.depth=6;settings.height=7;settings.pebble_count=20;settings.decoration_density=.65
start=time.perf_counter();assert bpy.ops.worldbuilder.generate_reef_sheet()=={"FINISHED"};seconds=time.perf_counter()-start
collections=[value for value in bpy.data.collections if value.get("wb_reef_asset_id","").startswith("performance.reef.")]
triangles=0
for collection in collections:
    for obj in collection.objects:
        if obj.type=="MESH":obj.data.calc_loop_triangles();triangles+=len(obj.data.loop_triangles)
assert len(collections)==12,len(collections)
assert triangles<350000,triangles
assert seconds<15,seconds
print(f"WB_REEF_SHEET_PERF_OK assets=12 triangles={triangles} seconds={seconds:.3f}")
worldbuilder_chunks.unregister()
