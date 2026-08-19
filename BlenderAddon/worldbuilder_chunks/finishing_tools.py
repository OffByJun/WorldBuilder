"""Asset Stamp, cached World Analysis, and configurable vertex-color bake tools."""

from __future__ import annotations

import json
import math
import os
import random
import uuid

import bpy
import gpu
from bpy.props import BoolProperty, EnumProperty, FloatProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup
from gpu_extras.batch import batch_for_shader
from mathutils import Quaternion, Vector
from mathutils.kdtree import KDTree

from . import analysis, biome, contract, exporter, layers, manifest, stamp_io, state, vertex_bake_contract

def _uuid():return uuid.uuid4().hex

class WBStampSettings(PropertyGroup):
    library_folder:StringProperty(name="Library Folder",subtype="DIR_PATH");asset_registry:StringProperty(name="Asset Registry",subtype="FILE_PATH");name:StringProperty(name="Name",default="New Stamp");category:StringProperty(name="Category",default="General");tags:StringProperty(name="Tags");active_file:StringProperty(name="Stamp File",subtype="FILE_PATH");align_to_surface:BoolProperty(name="Align to Surface",default=False);random_yaw:BoolProperty(name="Random Yaw",default=False);mirror_x:BoolProperty(name="Mirror X",default=False);scale:FloatProperty(name="Scale",default=1,min=.001);patch_target:bpy.props.PointerProperty(name="Patch Target",type=bpy.types.Object);apply_terrain_patch:BoolProperty(name="Apply Terrain Patch",default=True);apply_biome_patch:BoolProperty(name="Apply Biome Patch",default=True);patch_max_distance:FloatProperty(name="Patch Match Distance",default=2,min=.001)
class WBAnalysisSettings(PropertyGroup):
    mode:EnumProperty(name="Mode",items=((v,v.replace("_"," ").title(),"") for v in ("SLOPE","HEIGHT","OBJECT_PIVOT_DENSITY","BOUNDS_OVERLAP_DENSITY","TRIANGLE_DENSITY","COLLIDER_DENSITY","BIOME_DISTRIBUTION","TRAVERSABILITY","EMPTY_SPACE")),default="OBJECT_PIVOT_DENSITY");scope_radius:FloatProperty(name="Radius",default=512,min=1);resolution:FloatProperty(name="Cell Size",default=32,min=.1);maximum_slope:FloatProperty(name="Maximum Walkable Slope",default=40,min=0,max=90);layer_filter:EnumProperty(name="Layers",items=(("ALL","All Layers","Aggregate every vertical layer"),("ACTIVE","Active Layer","Show only the active vertical layer")),default="ACTIVE");export_path:StringProperty(name="Export JSON",subtype="FILE_PATH");last_report:StringProperty(default="No cache")
class WBVertexBakeSettings(PropertyGroup):
    target_name:StringProperty(name="Target Attribute",default="WB_ShaderData");domain:EnumProperty(name="Domain",items=(("POINT","Point",""),("CORNER","Corner","")),default="POINT")
    source_r:EnumProperty(name="R",items=((v,v.replace("_"," ").title(),"") for v in vertex_bake_contract.SOURCES),default="UP_FACING");source_g:EnumProperty(name="G",items=((v,v.replace("_"," ").title(),"") for v in vertex_bake_contract.SOURCES),default="CAVITY_APPROX");source_b:EnumProperty(name="B",items=((v,v.replace("_"," ").title(),"") for v in vertex_bake_contract.SOURCES),default="BIOME_WEIGHT");source_a:EnumProperty(name="A",items=((v,v.replace("_"," ").title(),"") for v in vertex_bake_contract.SOURCES),default="CONSTANT")
    constant:FloatProperty(name="Constant",default=1,min=0,max=1);min_height:FloatProperty(name="Min Height",default=-20);max_height:FloatProperty(name="Max Height",default=100);sea_level:FloatProperty(name="Sea Level",default=0);max_water_depth:FloatProperty(name="Max Water Depth",default=50,min=.001);last_report:StringProperty(default="Not baked")

def _stamp_payload(context):
    selected=sorted(context.selected_objects,key=lambda value:value.name);pivot=Vector(context.scene.cursor.location);objects=[];surface_patches=[]
    for obj in selected:
        asset_id=obj.worldbuilder_chunk.asset_id if hasattr(obj,"worldbuilder_chunk") else obj.get("asset_id","")
        objects.append({"name":obj.name,"assetId":asset_id or obj.name,"sourceObject":obj.name,"role":exporter.object_role(obj),"relativePosition":list(obj.location-pivot),"rotationQuaternion":list(obj.rotation_quaternion if obj.rotation_mode=="QUATERNION" else obj.rotation_euler.to_quaternion()),"scale":list(obj.scale)})
        if obj.type=="MESH" and biome.is_biome_target(obj):
            basis=bpy.data.meshes.get(obj.get("wb_terrain_basis_mesh",""));definitions=[definition for definition in biome.biome_definitions(context.scene) if definition.enabled];samples=[]
            for vertex in obj.data.vertices:
                world=obj.matrix_world@vertex.co;base_world=obj.matrix_world@(basis.vertices[vertex.index].co if basis and vertex.index<len(basis.vertices) else vertex.co);weights={definition.stable_id:float(obj.data.attributes[definition.attribute_name].data[vertex.index].value) for definition in definitions if obj.data.attributes.get(definition.attribute_name)}
                samples.append({"xy":[world.x-pivot.x,world.y-pivot.y],"deltaZ":world.z-base_world.z,"biomes":weights})
            surface_patches.append({"sourceObject":obj.name,"samples":samples})
    return stamp_io.create_stamp(context.scene.worldbuilder_stamps.name,_uuid(),context.scene.worldbuilder_stamps.category,pivot,{},objects,[tag.strip() for tag in context.scene.worldbuilder_stamps.tags.split(",") if tag.strip()],{"surface":surface_patches})

def _apply_stamp_patches(context,value,pivot,settings):
    target=settings.patch_target
    patches=value.get("patches",{}).get("surface",[])
    if target is None or target.type!="MESH" or not patches:return 0
    points=[target.matrix_world@vertex.co for vertex in target.data.vertices];tree=KDTree(len(points))
    for index,point in enumerate(points):tree.insert(point,index)
    tree.balance();inverse=target.matrix_world.inverted_safe();definitions={definition.stable_id:definition for definition in biome.biome_definitions(context.scene)};changed=set()
    for patch in patches:
        for sample in patch.get("samples",[]):
            query=Vector((pivot.x+sample["xy"][0],pivot.y+sample["xy"][1],pivot.z));location,index,distance=tree.find(query)
            if distance>settings.patch_max_distance:continue
            if settings.apply_terrain_patch and abs(float(sample.get("deltaZ",0)))>1e-12:
                world=target.matrix_world@target.data.vertices[index].co;world.z+=float(sample["deltaZ"])*settings.scale;target.data.vertices[index].co=inverse@world
            if settings.apply_biome_patch:
                for biome_id,weight in sample.get("biomes",{}).items():
                    definition=definitions.get(biome_id)
                    if definition:biome.ensure_biome_attribute(target.data,definition).data[index].value=max(0,min(1,float(weight)))
            changed.add(index)
    if changed:
        target.data.update();biome.invalidate_biome_cache(target);grid=context.scene.worldbuilder_chunks;bounds=exporter.world_bounds(target)
        if bounds:
            minimum,maximum=bounds;low=contract.chunk_coord_from_xy(minimum.x,minimum.y,grid.origin_x,grid.origin_y,grid.chunk_size);high=contract.chunk_coord_from_xy(maximum.x-1e-7,maximum.y-1e-7,grid.origin_x,grid.origin_y,grid.chunk_size);state.mark_dirty(grid,*((x,z) for x in range(low[0],high[0]+1) for z in range(low[1],high[1]+1)))
    return len(changed)

class WB_OT_stamp_save(Operator):
    bl_idname="worldbuilder.stamp_save";bl_label="Save Selection as Stamp"
    def execute(self,context):
        settings=context.scene.worldbuilder_stamps;folder=bpy.path.abspath(settings.library_folder)
        if not folder or not context.selected_objects:self.report({"ERROR"},"Choose a library folder and selection");return {"CANCELLED"}
        os.makedirs(folder,exist_ok=True);safe="".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in settings.name);path=os.path.join(folder,f"{safe}.wbstamp.json");stamp_io.save_stamp(path,_stamp_payload(context));settings.active_file=path;self.report({"INFO"},f"Saved {path}");return {"FINISHED"}
class WB_OT_stamp_registry_build(Operator):
    bl_idname="worldbuilder.stamp_registry_build";bl_label="Build Registry from Selection"
    def execute(self,context):
        path=bpy.path.abspath(context.scene.worldbuilder_stamps.asset_registry)
        if not path or not context.selected_objects:self.report({"ERROR"},"Choose a registry file and select asset source objects");return {"CANCELLED"}
        entries=[];blend_path=bpy.data.filepath
        for obj in sorted(context.selected_objects,key=lambda value:value.name):
            asset_id=obj.worldbuilder_chunk.asset_id if hasattr(obj,"worldbuilder_chunk") and obj.worldbuilder_chunk.asset_id else obj.get("asset_id") or obj.name
            entries.append({"assetId":asset_id,"blendPath":os.path.relpath(blend_path,os.path.dirname(path)) if blend_path else "","objectName":obj.name})
        try:os.makedirs(os.path.dirname(path),exist_ok=True);stamp_io.save_asset_registry(path,entries)
        except (OSError,ValueError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Registered {len(entries)} assets");return {"FINISHED"}
def _resolve_stamp_asset(settings,record):
    asset_id=record.get("assetId","")
    for obj in bpy.data.objects:
        candidate=obj.worldbuilder_chunk.asset_id if hasattr(obj,"worldbuilder_chunk") else obj.get("asset_id","")
        if candidate==asset_id:return obj
    path=bpy.path.abspath(settings.asset_registry)
    if path:
        registry=stamp_io.load_asset_registry(path);entry=registry.get(asset_id)
        if entry:
            blend_path=entry.get("blendPath","");blend_path=os.path.normpath(os.path.join(os.path.dirname(path),blend_path)) if blend_path and not os.path.isabs(blend_path) else blend_path
            if blend_path:
                with bpy.data.libraries.load(blend_path,link=True) as (source,target):
                    target.objects=[entry["objectName"]] if entry["objectName"] in source.objects else []
                return target.objects[0] if target.objects else None
            return bpy.data.objects.get(entry["objectName"])
    return bpy.data.objects.get(record.get("sourceObject"))
class WB_OT_stamp_place(Operator):
    bl_idname="worldbuilder.stamp_place";bl_label="Place Stamp";bl_options={"UNDO"}
    def execute(self,context):
        settings=context.scene.worldbuilder_stamps;path=bpy.path.abspath(settings.active_file)
        try:value=stamp_io.load_stamp(path)
        except (OSError,ValueError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        pivot=Vector(context.scene.cursor.location);surface_normal=Vector((0,0,1))
        if settings.align_to_surface:
            hit,location,normal,_index,_obj,_matrix=context.scene.ray_cast(context.evaluated_depsgraph_get(),pivot+Vector((0,0,10000)),Vector((0,0,-1)),distance=20000)
            if hit:pivot=location;surface_normal=normal.normalized()
        collection=context.collection;count=0;yaw=random.Random(int(value["stableId"][:8],16)).uniform(0,math.tau) if settings.random_yaw else 0;surface_rotation=Vector((0,0,1)).rotation_difference(surface_normal) if settings.align_to_surface else Quaternion()
        missing=[]
        for record in value["objects"]:
            try:source=_resolve_stamp_asset(settings,record)
            except (OSError,ValueError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
            if source is None:missing.append(record.get("assetId") or record.get("sourceObject"));continue
            obj=source.copy();obj.data=source.data;collection.objects.link(obj);relative=Vector(record["relativePosition"]);relative.x*=-1 if settings.mirror_x else 1;obj.location=pivot+relative;obj.rotation_mode="QUATERNION";obj.rotation_quaternion=Quaternion(surface_normal,yaw)@surface_rotation@Quaternion(record["rotationQuaternion"]);scale=Vector(record["scale"])*settings.scale;scale.x*=-1 if settings.mirror_x else 1;obj.scale=scale;obj["wb_stamp_id"]=value["stableId"];obj["wb_stamp_instance_id"]=_uuid()
            if hasattr(obj,"worldbuilder_chunk"):
                obj.worldbuilder_chunk.role=record.get("role","INSTANCE");obj.worldbuilder_chunk.asset_id=record.get("assetId","");obj.worldbuilder_chunk.stable_id=obj["wb_stamp_instance_id"]
            grid=context.scene.worldbuilder_chunks;coord=contract.chunk_coord_from_xy(obj.location.x,obj.location.y,grid.origin_x,grid.origin_y,grid.chunk_size);state.mark_dirty(grid,coord);count+=1
        patched=_apply_stamp_patches(context,value,pivot,settings)
        if missing:self.report({"WARNING"},f"Placed {count}; missing registry assets: {', '.join(missing[:3])}")
        else:self.report({"INFO"},f"Placed {count} linked objects; patched {patched} vertices")
        return {"FINISHED"}

_analysis_cells={};_analysis_handle=None
def recalculate_analysis(scene):
    global _analysis_cells
    settings=scene.worldbuilder_analysis;records=[];focus=scene.cursor.location;grid=scene.worldbuilder_chunks
    for obj in scene.objects:
        if obj.hide_viewport or (obj.matrix_world.translation.xy-focus.xy).length>settings.scope_radius:continue
        triangles=sum(max(0,len(poly.vertices)-2) for poly in obj.data.polygons) if obj.type=="MESH" else 0
        records.append({"position":tuple(obj.matrix_world.translation),"triangles":triangles,"collider":exporter.object_role(obj)=="COLLISION","layer":exporter.object_layer(obj,grid)})
    _analysis_cells={}
    for record in records:
        key=analysis.cell_coord_3d(record["position"],(grid.origin_x,grid.origin_y),settings.resolution,record["layer"])
        cell=_analysis_cells.setdefault(key,{"object_count":0,"triangle_count":0,"collider_count":0})
        cell["object_count"]+=1;cell["triangle_count"]+=max(0,int(record.get("triangles",0)));cell["collider_count"]+=1 if record.get("collider") else 0
    active_biome=None;biome_settings=getattr(scene,"worldbuilder_biomes",None)
    if biome_settings and biome_settings.layers and biome_settings.active_index<len(biome_settings.layers):active_biome=biome_settings.layers[biome_settings.active_index]
    for obj in scene.objects:
        if obj.type!="MESH" or obj.hide_viewport or (obj.matrix_world.translation.xy-focus.xy).length>settings.scope_radius:continue
        bounds=exporter.world_bounds(obj)
        if bounds:
            minimum,maximum=bounds;low=analysis.cell_coord(minimum,(grid.origin_x,grid.origin_y),settings.resolution);high=analysis.cell_coord(maximum,(grid.origin_x,grid.origin_y),settings.resolution)
            low_layer=contract.clamp_layer(contract.layer_index_for_z(minimum.z,grid.layer_base_z,grid.layer_height),grid.layer_count);high_layer=contract.clamp_layer(contract.layer_index_for_z(maximum.z,grid.layer_base_z,grid.layer_height),grid.layer_count)
            for x in range(low[0],high[0]+1):
                for y in range(low[1],high[1]+1):
                    for band in range(low_layer,high_layer+1):
                        cell=_analysis_cells.setdefault((x,y,band),{"object_count":0,"triangle_count":0,"collider_count":0});cell["bounds_overlap_count"]=cell.get("bounds_overlap_count",0)+1
        attribute=obj.data.attributes.get(active_biome.attribute_name) if active_biome else None
        for vertex in obj.data.vertices:
            world=obj.matrix_world@vertex.co;key=analysis.cell_coord_3d(world,(grid.origin_x,grid.origin_y),settings.resolution,contract.clamp_layer(contract.layer_index_for_z(world.z,grid.layer_base_z,grid.layer_height),grid.layer_count));cell=_analysis_cells.setdefault(key,{"object_count":0,"triangle_count":0,"collider_count":0});normal=(obj.matrix_world.to_3x3()@vertex.normal).normalized();slope=math.degrees(math.acos(max(-1,min(1,normal.z))));cell["sample_count"]=cell.get("sample_count",0)+1;cell["height_sum"]=cell.get("height_sum",0)+world.z;cell["slope_sum"]=cell.get("slope_sum",0)+slope;cell["traversable_count"]=cell.get("traversable_count",0)+(1 if slope<=settings.maximum_slope else 0);cell["biome_sum"]=cell.get("biome_sum",0)+(attribute.data[vertex.index].value if attribute else 0)
    for cell in _analysis_cells.values():
        count=max(1,cell.get("sample_count",0));cell["height_average"]=cell.get("height_sum",0)/count;cell["slope_average"]=cell.get("slope_sum",0)/count;cell["traversability"]=cell.get("traversable_count",0)/count;cell["biome_average"]=cell.get("biome_sum",0)/count;cell.setdefault("bounds_overlap_count",0)
    _analysis_cells=dict(sorted(_analysis_cells.items()));values=[{"coordinate":[key[0],key[1]],"layer":key[2],**value} for key,value in _analysis_cells.items()];settings.last_report=f"{len(values)} cached cells within {settings.scope_radius:g}m, explicit recalculation";return values
def _analysis_draw():
    scene=getattr(bpy.context,"scene",None)
    if not scene or not _analysis_cells:return
    settings=scene.worldbuilder_analysis;key_name={"SLOPE":"slope_average","HEIGHT":"height_average","OBJECT_PIVOT_DENSITY":"object_count","BOUNDS_OVERLAP_DENSITY":"bounds_overlap_count","TRIANGLE_DENSITY":"triangle_count","COLLIDER_DENSITY":"collider_count","BIOME_DISTRIBUTION":"biome_average","TRAVERSABILITY":"traversability","EMPTY_SPACE":"object_count"}[settings.mode];visible=[value for key,value in _analysis_cells.items() if settings.layer_filter!="ACTIVE" or key[2]==scene.worldbuilder_chunks.active_layer];minimum=min((value.get(key_name,0) for value in visible),default=0);maximum=max((value.get(key_name,0) for value in visible),default=1);span=max(maximum-minimum,1e-9);shader=gpu.shader.from_builtin("UNIFORM_COLOR")
    grid=scene.worldbuilder_chunks
    for key,value in _analysis_cells.items():
        x,y,band=key
        if settings.layer_filter=="ACTIVE" and band!=grid.active_layer:continue
        t=(value.get(key_name,0)-minimum)/span;t=1-t if settings.mode=="EMPTY_SPACE" else t;color=(t,.2,1-t,.25);size=settings.resolution;origin_x=grid.origin_x+x*size;origin_y=grid.origin_y+y*size;z=contract.layer_floor_z(band,grid.layer_base_z,grid.layer_height)+.1;vertices=((origin_x,origin_y,z),(origin_x+size,origin_y,z),(origin_x+size,origin_y+size,z),(origin_x,origin_y+size,z));batch=batch_for_shader(shader,"TRIS",{"pos":vertices},indices=((0,1,2),(0,2,3)));gpu.state.blend_set("ALPHA");shader.bind();shader.uniform_float("color",color);batch.draw(shader);gpu.state.blend_set("NONE")
class WB_OT_analysis_recalculate(Operator):
    bl_idname="worldbuilder.analysis_recalculate";bl_label="Recalculate Analysis"
    def execute(self,context):values=recalculate_analysis(context.scene);self.report({"INFO"},f"Cached {len(values)} cells");return {"FINISHED"}
class WB_OT_analysis_export(Operator):
    bl_idname="worldbuilder.analysis_export";bl_label="Export Analysis JSON"
    def execute(self,context):
        settings=context.scene.worldbuilder_analysis;path=bpy.path.abspath(settings.export_path)
        if not path:self.report({"ERROR"},"Choose export path");return {"CANCELLED"}
        values=recalculate_analysis(context.scene);manifest.atomic_write(path,{"schemaVersion":2,"mode":settings.mode,"cellSize":settings.resolution,"layerHeight":context.scene.worldbuilder_chunks.layer_height,"layerBaseZ":context.scene.worldbuilder_chunks.layer_base_z,"cells":values});return {"FINISHED"}
class WB_OT_analysis_clear(Operator):
    bl_idname="worldbuilder.analysis_clear";bl_label="Clear Analysis"
    def execute(self,context):_analysis_cells.clear();context.scene.worldbuilder_analysis.last_report="No cache";return {"FINISHED"}
class WB_OT_analysis_use_query_cell(Operator):
    bl_idname="worldbuilder.analysis_use_query_cell";bl_label="Use Query Cell Size"
    def execute(self,context):context.scene.worldbuilder_analysis.resolution=context.scene.worldbuilder_chunks.query_cell_size;return {"FINISHED"}

def _vertex_metrics(mesh):
    neighbors=[set() for _ in mesh.vertices]
    for edge in mesh.edges:a,b=edge.vertices;neighbors[a].add(b);neighbors[b].add(a)
    result={}
    for vertex in mesh.vertices:
        if not neighbors[vertex.index]:result[vertex.index]=(0.0,0.0,0.0);continue
        curvature=sum(max(0.0,1.0-vertex.normal.dot(mesh.vertices[index].normal)) for index in neighbors[vertex.index])/len(neighbors[vertex.index]);cavity=sum(max(0.0,(mesh.vertices[index].co-vertex.co).dot(vertex.normal)) for index in neighbors[vertex.index])/len(neighbors[vertex.index]);cavity=max(0.0,min(1.0,cavity));curvature=max(0.0,min(1.0,curvature));result[vertex.index]=(curvature,cavity,max(0.0,min(1.0,cavity*.75+curvature*.25)))
    return result
def _vertex_sample(obj,vertex,settings,active_biome,metrics):
    world=obj.matrix_world@vertex.co;normal=(obj.matrix_world.to_3x3()@vertex.normal).normalized();biome_weight=0.0
    if active_biome:
        attr=obj.data.attributes.get(active_biome.attribute_name)
        if attr and vertex.index<len(attr.data):biome_weight=attr.data[vertex.index].value
    vertex_seed=int(manifest.content_hash({"object":obj.name,"vertex":vertex.index})[:8],16);object_seed=int(manifest.content_hash({"object":obj.name})[:8],16);curvature,cavity,ao=metrics.get(vertex.index,(0,0,0))
    return {"height":world.z,"normal":normal,"biome_weight":biome_weight,"water_depth":max(0,settings.sea_level-world.z),"object_random":(object_seed%100000)/99999,"random_per_face":(vertex_seed%65535)/65534,"cavity_approx":cavity,"ambient_occlusion_approx":ao,"curvature":curvature}
def bake_vertex_attribute(context,clear=False):
    obj=context.object;settings=context.scene.worldbuilder_vertex_bake
    if obj is None or obj.type!="MESH":raise ValueError("Select a Mesh object")
    existing=obj.data.color_attributes.get(settings.target_name)
    if clear:
        if existing:obj.data.color_attributes.remove(existing)
        return 0
    if existing and (existing.domain!=settings.domain or existing.data_type!="FLOAT_COLOR"):raise ValueError("Existing target has a different domain/type; choose another name")
    attribute=existing or obj.data.color_attributes.new(name=settings.target_name,type="FLOAT_COLOR",domain=settings.domain);definitions=list(biome.biome_definitions(context.scene));active_settings=getattr(context.scene,"worldbuilder_biomes",None);active=definitions[active_settings.active_index] if definitions and active_settings and active_settings.active_index<len(definitions) else None;sources=(settings.source_r,settings.source_g,settings.source_b,settings.source_a);options={channel:{"constant":settings.constant,"min_height":settings.min_height,"max_height":settings.max_height,"max_depth":settings.max_water_depth} for channel in "RGBA"};metrics=_vertex_metrics(obj.data)
    if settings.domain=="POINT":
        for vertex,item in zip(obj.data.vertices,attribute.data):item.color=vertex_bake_contract.bake_rgba(_vertex_sample(obj,vertex,settings,active,metrics),sources,options)
    else:
        for loop,item in zip(obj.data.loops,attribute.data):
            sample=_vertex_sample(obj,obj.data.vertices[loop.vertex_index],settings,active,metrics);sample["random_per_face"]=(int(manifest.content_hash({"object":obj.name,"face":loop.polygon_index})[:8],16)%65535)/65534;item.color=vertex_bake_contract.bake_rgba(sample,sources,options)
    obj.data.update();obj["wb_vertex_attribute_contract"]=json.dumps({"name":settings.target_name,"domain":settings.domain,"channels":dict(zip("RGBA",sources))},sort_keys=True);settings.last_report=f"Baked {len(attribute.data)} {settings.domain} values";return len(attribute.data)
class WB_OT_vertex_bake(Operator):
    bl_idname="worldbuilder.vertex_bake";bl_label="Bake Vertex Attribute";bl_options={"UNDO"}
    def execute(self,context):
        try:count=bake_vertex_attribute(context)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Baked {count} values");return {"FINISHED"}
class WB_OT_vertex_clear(Operator):
    bl_idname="worldbuilder.vertex_clear";bl_label="Clear Vertex Attribute";bl_options={"UNDO"}
    def execute(self,context):
        try:bake_vertex_attribute(context,True)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        return {"FINISHED"}

class WB_PT_stamps(Panel):
    bl_label="Stamps";bl_idname="WB_PT_stamps";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_stamps;layout.prop(s,"library_folder");layout.prop(s,"asset_registry");layout.operator("worldbuilder.stamp_registry_build",icon="FILE_REFRESH");layout.prop(s,"name");layout.prop(s,"category");layout.prop(s,"tags");layout.operator("worldbuilder.stamp_save");layout.prop(s,"active_file");layout.prop(s,"scale");row=layout.row(align=True);row.prop(s,"align_to_surface");row.prop(s,"random_yaw");row.prop(s,"mirror_x");layout.prop(s,"patch_target");row=layout.row(align=True);row.prop(s,"apply_terrain_patch");row.prop(s,"apply_biome_patch");layout.prop(s,"patch_max_distance");layout.operator("worldbuilder.stamp_place")
class WB_PT_analysis(Panel):
    bl_label="Analysis";bl_idname="WB_PT_analysis";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_analysis;layout.prop(s,"mode");layout.prop(s,"scope_radius");row=layout.row(align=True);row.prop(s,"resolution");row.operator("worldbuilder.analysis_use_query_cell",text="Use Query Cell");layout.prop(s,"maximum_slope");layout.prop(s,"layer_filter");row=layout.row(align=True);row.operator("worldbuilder.analysis_recalculate");row.operator("worldbuilder.analysis_clear");layout.prop(s,"export_path");layout.operator("worldbuilder.analysis_export");layout.label(text=s.last_report);layout.label(text="Cells are keyed per vertical layer; use Traversal Check for player-scale probes",icon="INFO")
class WB_PT_vertex_bake(Panel):
    bl_label="Vertex Attribute Bake";bl_idname="WB_PT_vertex_bake";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_vertex_bake;layout.prop(s,"target_name");layout.prop(s,"domain");row=layout.row(align=True);row.prop(s,"source_r");row.prop(s,"source_g");row=layout.row(align=True);row.prop(s,"source_b");row.prop(s,"source_a");layout.prop(s,"constant");row=layout.row(align=True);row.operator("worldbuilder.vertex_bake");row.operator("worldbuilder.vertex_clear");layout.label(text=s.last_report);layout.label(text="AO/Cavity sources are local approximations, not ray-traced bakes",icon="INFO")

CLASSES=(WBStampSettings,WBAnalysisSettings,WBVertexBakeSettings,WB_OT_stamp_save,WB_OT_stamp_registry_build,WB_OT_stamp_place,WB_OT_analysis_recalculate,WB_OT_analysis_export,WB_OT_analysis_clear,WB_OT_analysis_use_query_cell,WB_OT_vertex_bake,WB_OT_vertex_clear,WB_PT_stamps,WB_PT_analysis,WB_PT_vertex_bake)
def register():
    global _analysis_handle
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_stamps=bpy.props.PointerProperty(type=WBStampSettings);bpy.types.Scene.worldbuilder_analysis=bpy.props.PointerProperty(type=WBAnalysisSettings);bpy.types.Scene.worldbuilder_vertex_bake=bpy.props.PointerProperty(type=WBVertexBakeSettings);_analysis_handle=bpy.types.SpaceView3D.draw_handler_add(_analysis_draw,(),"WINDOW","POST_VIEW")
def unregister():
    global _analysis_handle
    if _analysis_handle:bpy.types.SpaceView3D.draw_handler_remove(_analysis_handle,"WINDOW");_analysis_handle=None
    for name in ("worldbuilder_vertex_bake","worldbuilder_analysis","worldbuilder_stamps"):
        if hasattr(bpy.types.Scene,name):delattr(bpy.types.Scene,name)
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
