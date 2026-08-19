"""Render the four reef formation presets for visual regression review."""
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ADDON_ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks
from worldbuilder_chunks import reef_generator

worldbuilder_chunks.register();bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;settings=scene.worldbuilder_reef
settings.width=8;settings.depth=6;settings.height=8;settings.decoration_density=.72;settings.seaweed_density=.62;settings.coral_density=.58;settings.pebble_count=28
for index,(formation,x) in enumerate(zip(("MOUND","TERRACES","SPIRE","ARCH"),(-14,-5,5,14))):
    scene.cursor.location=(x,0,0);settings.formation=formation;settings.seed=700+index;settings.asset_id=f"visual.reef.{formation.lower()}"
    settings.height=10 if formation=="SPIRE" else 8
    reef_generator.generate(bpy.context,settings)

def point_at(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()
camera_data=bpy.data.cameras.new("ReefPreviewCamera");camera=bpy.data.objects.new("ReefPreviewCamera",camera_data);scene.collection.objects.link(camera);camera.location=(0,-38,15);point_at(camera,(0,0,4));camera_data.lens=52;scene.camera=camera
for name,location,energy,size,color in (("Key",(-12,-12,20),1800,10,(1.0,.84,.64)),("Fill",(14,-6,13),1400,9,(.28,.62,1.0)),("Rim",(0,10,17),1700,8,(.28,1.0,.72))):
    data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size;data.color=color;obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location;point_at(obj,(0,0,4))
scene.world.color=(.035,.055,.075);scene.render.engine="BLENDER_EEVEE";scene.render.resolution_x=1400;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.render.image_settings.file_format="PNG";scene.render.filepath=r"C:\Projects\Survival\.codex\artifacts\reef-generator-visual.png";scene.render.film_transparent=False
bpy.ops.render.render(write_still=True);print("WB_REEF_VISUAL_OK",scene.render.filepath)
worldbuilder_chunks.unregister()
