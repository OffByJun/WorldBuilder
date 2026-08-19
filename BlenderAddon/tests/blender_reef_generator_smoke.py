"""Build every reference-driven reef formation and verify asset-library readiness."""
from pathlib import Path
import sys,tempfile

import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ADDON_ROOT))

import worldbuilder_chunks
from worldbuilder_chunks import reef_generator

worldbuilder_chunks.register()
bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;settings=scene.worldbuilder_reef
settings.width=10;settings.depth=8;settings.height=9;settings.rock_sides=7;settings.rock_levels=5
settings.decoration_density=.65;settings.seaweed_density=.58;settings.coral_density=.52;settings.pebble_count=18

results=[]
for index,formation in enumerate(("MOUND","TERRACES","SPIRE","ARCH")):
    settings.asset_kind="COMPLETE"
    settings.formation=formation;settings.seed=100+index;settings.asset_id=f"environment.reef.{formation.lower()}.smoke"
    collection,preview,rock_count=reef_generator.generate(bpy.context,settings)
    meshes=[obj for obj in collection.objects if obj.type=="MESH"]
    triangles=sum(len(obj.data.loop_triangles) for obj in meshes for _ in [obj.data.calc_loop_triangles()])
    names={obj.name for obj in meshes}
    assert rock_count>=7,(formation,rock_count)
    assert any(name.startswith("ReefSeaweed") for name in names),names
    assert any(name.startswith("ReefCoral") for name in names),names
    assert any(name.startswith("ReefPebbles") for name in names),names
    assert all(obj.get("wb_role")=="GLOBAL" for obj in collection.objects)
    assert preview.instance_collection==collection
    assert scene.worldbuilder_structure_library.draft_collection==collection
    assert 250<triangles<100000,(formation,triangles)
    results.append((formation,rock_count,len(meshes),triangles))

for kind,expected,forbidden in (("SEAWEED_PATCH","ReefSeaweed","ReefCoral"),("CORAL_PATCH","ReefCoral","ReefSeaweed")):
    settings.asset_kind=kind;settings.asset_id=f"environment.reef.{kind.lower()}.smoke";settings.seed+=1
    collection,preview,rock_count=reef_generator.generate(bpy.context,settings);names={obj.name for obj in collection.objects}
    assert rock_count==0
    assert any(name.startswith(expected) for name in names),names
    assert not any(name.startswith(forbidden) for name in names),names
    if kind=="SEAWEED_PATCH":
        seaweed=next(obj for obj in collection.objects if obj.name.startswith("ReefSeaweed"));assert seaweed.data.color_attributes.get("WB_Sway") is not None;assert seaweed.get("wb_shader_family")=="SEAWEED"

blend_path=Path(tempfile.gettempdir())/"worldbuilder_reef_generator_smoke.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path));bpy.ops.wm.open_mainfile(filepath=str(blend_path))
saved=bpy.data.collections.get("WB_ASSET_environment_reef_seaweed_patch_smoke");assert saved is not None and saved.asset_data is not None
saved_seaweed=next(obj for obj in saved.objects if obj.name.startswith("ReefSeaweed"));assert saved_seaweed.data.color_attributes.get("WB_Sway") is not None

print("WB_REEF_GENERATOR_OK",results)
worldbuilder_chunks.unregister()
