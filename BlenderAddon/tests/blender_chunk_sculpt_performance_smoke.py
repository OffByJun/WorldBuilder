"""Generate and weld a 3x3 64-cell terrain scope under practical authoring budgets."""
from pathlib import Path
import sys,time
import bpy
ADDON_ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import chunk_terrain,sculpt_session,state
worldbuilder_chunks.register();scene=bpy.context.scene;bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False);grid=scene.worldbuilder_chunks;grid.developer_override=True;grid.chunk_size=128;scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1
coords={(x,z) for x in range(-1,2) for z in range(-1,2)};state.set_selected_chunks(grid,coords);settings=scene.worldbuilder_chunk_terrain;settings.scope="SELECTED";settings.cells=64;settings.apply_palette=False
start=time.perf_counter();assert bpy.ops.worldbuilder.generate_chunk_terrain()=={"FINISHED"};generate_seconds=time.perf_counter()-start
scene.worldbuilder_sculpt.scope="SELECTED";scene.worldbuilder_sculpt.neighbor_ring=0;start=time.perf_counter();proxy=sculpt_session.begin(scene);proxy_seconds=time.perf_counter()-start
proxy_vertex_count=len(proxy.data.vertices)
start=time.perf_counter();changed=sculpt_session.apply(scene);apply_seconds=time.perf_counter()-start
assert len(changed)==9
assert proxy_vertex_count==(64*3+1)**2
assert generate_seconds<10 and proxy_seconds<10 and apply_seconds<10,(generate_seconds,proxy_seconds,apply_seconds)
print(f"WB_CHUNK_SCULPT_PERF_OK chunks=9 cells=64 generate={generate_seconds:.3f}s proxy={proxy_seconds:.3f}s apply={apply_seconds:.3f}s");worldbuilder_chunks.unregister()
