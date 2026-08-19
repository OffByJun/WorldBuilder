"""Headless deterministic 10k linked-instance scatter benchmark."""
from pathlib import Path
import sys
import time
import bpy

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import scatter

worldbuilder_chunks.register()
scene=bpy.context.scene
scene.worldbuilder_chunks.developer_override=True
bpy.ops.mesh.primitive_grid_add(x_subdivisions=101,y_subdivisions=101,size=100)
terrain=bpy.context.object
terrain.name="ScatterPerfTerrain"
source_collection=bpy.data.collections.new("ScatterPerfAssets")
scene.collection.children.link(source_collection)
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.2)
source=bpy.context.object
for collection in list(source.users_collection):collection.objects.unlink(source)
source_collection.objects.link(source)
layer=scene.worldbuilder_scatter.layers.add();layer.stable_id="scatter-perf";layer.target_object=terrain;layer.source_collection=source_collection;layer.seed=123;layer.density=1.0;layer.minimum_distance=0;layer.max_instances=10000
started=time.perf_counter();count=scatter.generate(bpy.context,layer,True);elapsed=time.perf_counter()-started
assert count==10000,count
collection=bpy.data.collections["WB_SCATTER_scatter-perf"]
assert len(collection.all_objects)==10000
assert all(obj.data is source.data for obj in collection.all_objects)
assert elapsed<15.0,elapsed
print(f"WB_SCATTER_10K_OK count={count} seconds={elapsed:.3f}")
worldbuilder_chunks.unregister()
