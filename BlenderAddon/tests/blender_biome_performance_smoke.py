"""Headless cache benchmark for a roughly 100k-vertex biome target."""
from pathlib import Path
import sys
import time
import bpy

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import biome_painter

worldbuilder_chunks.register()
bpy.ops.mesh.primitive_grid_add(x_subdivisions=316,y_subdivisions=316,size=256)
obj=bpy.context.object
assert bpy.ops.worldbuilder.initialize_default_biomes()=={"FINISHED"}
started=time.perf_counter()
cache=biome_painter._TargetCache(obj,bpy.context.evaluated_depsgraph_get())
build_seconds=time.perf_counter()-started
started=time.perf_counter()
hits=list(cache.kdtree.find_range(obj.matrix_world@obj.data.vertices[len(obj.data.vertices)//2].co,5.0))
query_seconds=time.perf_counter()-started
assert len(obj.data.vertices)>=100000 and hits
assert query_seconds<.1,(query_seconds,len(hits))
print(f"WB_BIOME_100K_CACHE_OK vertices={len(obj.data.vertices)} build={build_seconds:.3f}s query={query_seconds:.6f}s hits={len(hits)}")
worldbuilder_chunks.unregister()
