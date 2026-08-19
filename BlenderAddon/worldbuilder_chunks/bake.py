"""Non-destructive LOD/collider bake pipeline and deterministic chunk manifests."""

from __future__ import annotations

import json
import os
import uuid

import bpy
import bmesh
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, IntProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from mathutils import Vector

from . import contract, exporter, manifest, state

def _uuid():return uuid.uuid4().hex
def _source_stable_id(source):
    return source.worldbuilder_chunk.stable_id if hasattr(source,"worldbuilder_chunk") and source.worldbuilder_chunk.stable_id else source.get("stable_id") or source.name

class WBBakeProfile(PropertyGroup):
    stable_id:StringProperty(default="");name:StringProperty(name="Name",default="Default")
    output_root:StringProperty(name="Output Root",subtype="DIR_PATH")
    generate_lods:BoolProperty(name="Generate LODs",default=True);lod1:FloatProperty(name="LOD1",default=.55,min=.01,max=1);lod2:FloatProperty(name="LOD2",default=.25,min=.01,max=1);lod3:FloatProperty(name="LOD3",default=.1,min=.01,max=1)
    generate_colliders:BoolProperty(name="Generate Colliders",default=True);collider_mode:EnumProperty(name="Collider",items=((v,v.replace("_"," ").title(),"") for v in ("NONE","COPY_VISUAL","DECIMATED_MESH","CONVEX_HULL","BOX","SPHERE","CAPSULE")),default="DECIMATED_MESH")
    preserve_chunk_boundaries:BoolProperty(name="Preserve Chunk Boundaries",default=True);preserve_material_boundaries:BoolProperty(name="Preserve Material Boundaries",default=True);apply_modifiers:BoolProperty(name="Apply Modifiers",default=True);triangulate:BoolProperty(name="Triangulate",default=True);export_manifest:BoolProperty(name="Export Manifest",default=True);validation_required:BoolProperty(name="Validation Required",default=True);overwrite_policy:EnumProperty(name="Overwrite",items=(("REUSE","Reuse Same Hash",""),("REPLACE","Replace",""),("FAIL","Fail","")),default="REUSE")
class WBBakeSettings(PropertyGroup):
    profiles:CollectionProperty(type=WBBakeProfile);active_index:IntProperty(default=0);scope:EnumProperty(name="Scope",items=(("ACTIVE_OBJECT","Active Object",""),("ACTIVE_CHUNK","Active Chunk",""),("DIRTY_CHUNKS","Dirty Chunks",""),("SELECTED","Selected Objects","")),default="ACTIVE_OBJECT");last_report:StringProperty(default="Not baked")

def _profile(scene):
    settings=scene.worldbuilder_bake
    if not settings.profiles:
        item=settings.profiles.add();item.stable_id=_uuid()
    return settings.profiles[min(settings.active_index,len(settings.profiles)-1)]
def _source_hash(obj,profile):
    data={"name":obj.name,"matrix":[round(v,7) for row in obj.matrix_world for v in row],"vertices":[[round(v,6) for v in vertex.co] for vertex in obj.data.vertices],"polygons":[list(poly.vertices) for poly in obj.data.polygons],"profile":{"lods":[profile.lod1,profile.lod2,profile.lod3],"collider":profile.collider_mode,"triangulate":profile.triangulate}}
    return manifest.content_hash(data)
def _root(scene,profile):
    name=f"WB_BAKE_{profile.stable_id}";collection=bpy.data.collections.get(name)
    if collection is None:collection=bpy.data.collections.new(name);scene.collection.children.link(collection)
    return collection
def clear_profile(scene,profile):
    collection=bpy.data.collections.get(f"WB_BAKE_{profile.stable_id}")
    if collection:
        for obj in list(collection.all_objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(collection)
class _BakeTransaction:
    def __init__(self,scene,profile):
        self.root=_root(scene,profile);self.backup=bpy.data.collections.new(f"__WB_BAKE_BACKUP_{uuid.uuid4().hex[:8]}");self.closed=False
        for source in list(self.root.all_objects):
            clone=source.copy();clone.data=source.data.copy() if getattr(source,"data",None) else None;clone.name=source.name;self.backup.objects.link(clone)
    def commit(self):
        if self.closed:return
        for obj in list(self.backup.all_objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(self.backup);self.closed=True
    def rollback(self):
        if self.closed:return
        for obj in list(self.root.all_objects):bpy.data.objects.remove(obj,do_unlink=True)
        for obj in list(self.backup.all_objects):
            self.backup.objects.unlink(obj);self.root.objects.link(obj)
        bpy.data.collections.remove(self.backup);self.closed=True
def _duplicate(source,collection,name):
    obj=source.copy();obj.data=source.data.copy();obj.animation_data_clear();obj.name=name;collection.objects.link(obj);return obj
def _triangles(obj):return sum(max(0,len(poly.vertices)-2) for poly in obj.data.polygons)
def _boundary_points(obj,grid):
    result=[];epsilon=1e-5
    for vertex in obj.data.vertices:
        point=obj.matrix_world@vertex.co;rx=(point.x-grid.origin_x)/grid.chunk_size;ry=(point.y-grid.origin_y)/grid.chunk_size
        if abs(rx-round(rx))<=epsilon or abs(ry-round(ry))<=epsilon:result.append((vertex.index,tuple(point)))
    return result
def _boundary_error(source_points,obj):
    if not source_points:return 0
    output={tuple(round(value,5) for value in (obj.matrix_world@vertex.co)) for vertex in obj.data.vertices}
    return sum(tuple(round(value,5) for value in point) not in output for _index,point in source_points)
def _primitive_collider(source,collection,name,mode):
    mesh=bpy.data.meshes.new(name+"Mesh");bm=bmesh.new();minimum=Vector((min(v[i] for v in source.bound_box) for i in range(3)));maximum=Vector((max(v[i] for v in source.bound_box) for i in range(3)));center=(minimum+maximum)*.5;extent=(maximum-minimum)*.5
    if mode=="BOX":bmesh.ops.create_cube(bm,size=2);bmesh.ops.scale(bm,vec=extent,verts=bm.verts);bmesh.ops.translate(bm,vec=center,verts=bm.verts)
    elif mode=="SPHERE":bmesh.ops.create_icosphere(bm,subdivisions=2,radius=max(extent));bmesh.ops.translate(bm,vec=center,verts=bm.verts)
    else:
        radius=max(extent.x,extent.y);bmesh.ops.create_uvsphere(bm,u_segments=12,v_segments=8,radius=1);bmesh.ops.scale(bm,vec=Vector((radius,radius,max(extent.z,radius))),verts=bm.verts);bmesh.ops.translate(bm,vec=center,verts=bm.verts)
    bm.to_mesh(mesh);bm.free();obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj);obj.matrix_world=source.matrix_world.copy();return obj
def _convex_hull(obj):
    bm=bmesh.new();bm.from_mesh(obj.data);result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    if result.get("geom_unused"):bmesh.ops.delete(bm,geom=result["geom_unused"],context="VERTS")
    bm.to_mesh(obj.data);bm.free();obj.data.update()
def _tag(obj,source,profile,hash_value,lod=-1,collider=""):
    stable=_source_stable_id(source)
    obj["wb_source_stable_id"]=stable;obj["wb_bake_profile_id"]=profile.stable_id;obj["wb_bake_hash"]=hash_value;obj["wb_lod_level"]=lod;obj["wb_collider_type"]=collider;obj["wb_source_revision"]=1
def bake_object(context,source,profile):
    if source.type!="MESH":return []
    root=_root(context.scene,profile);hash_value=_source_hash(source,profile);source_id=_source_stable_id(source);existing=[obj for obj in root.all_objects if obj.get("wb_source_stable_id")==source_id and obj.get("wb_bake_hash")==hash_value]
    if existing and profile.overwrite_policy=="REUSE":return existing
    for obj in list(root.all_objects):
        if obj.get("wb_source_stable_id")==source_id:bpy.data.objects.remove(obj,do_unlink=True)
    outputs=[];ratios=[1.0,profile.lod1,profile.lod2,profile.lod3] if profile.generate_lods else [1.0];source_triangles=max(1,_triangles(source));source_bounds=Vector(source.dimensions);boundary_points=_boundary_points(source,context.scene.worldbuilder_chunks) if profile.preserve_chunk_boundaries else []
    for level,ratio in enumerate(ratios):
        obj=_duplicate(source,root,f"{source.name}_LOD{level}")
        effective_ratio=1.0 if boundary_points and profile.preserve_chunk_boundaries else ratio
        if effective_ratio<.999:
            modifier=obj.modifiers.new("WorldBuilder LOD","DECIMATE");modifier.ratio=effective_ratio;modifier.use_collapse_triangulate=profile.triangulate
            context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.modifier_apply(modifier=modifier.name);obj.select_set(False)
        if profile.triangulate:
            modifier=obj.modifiers.new("WorldBuilder Triangulate","TRIANGULATE");context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.modifier_apply(modifier=modifier.name);obj.select_set(False)
        _tag(obj,source,profile,hash_value,level);obj["wb_triangle_count"]=_triangles(obj);obj["wb_reduction_ratio"]=obj["wb_triangle_count"]/source_triangles;obj["wb_bounds_difference"]=(Vector(obj.dimensions)-source_bounds).length;obj["wb_lod_boundary_error"]=_boundary_error(boundary_points,obj);obj["wb_lod_boundary_fallback"]=bool(boundary_points and ratio<.999);outputs.append(obj)
        if hasattr(obj,"worldbuilder_chunk"):obj.worldbuilder_chunk.role="GEOMETRY";obj.worldbuilder_chunk.stable_id=f"{source.name}_lod{level}"
    if profile.generate_colliders and profile.collider_mode!="NONE":
        collider=_primitive_collider(source,root,f"{source.name}_COL",profile.collider_mode) if profile.collider_mode in {"BOX","SPHERE","CAPSULE"} else _duplicate(source,root,f"{source.name}_COL")
        if profile.collider_mode=="DECIMATED_MESH":
            modifier=collider.modifiers.new("WorldBuilder Collider","DECIMATE");modifier.ratio=.18;context.view_layer.objects.active=collider;collider.select_set(True);bpy.ops.object.modifier_apply(modifier=modifier.name);collider.select_set(False)
        elif profile.collider_mode=="CONVEX_HULL":_convex_hull(collider)
        _tag(collider,source,profile,hash_value,-1,profile.collider_mode);collider.hide_render=True
        if hasattr(collider,"worldbuilder_chunk"):collider.worldbuilder_chunk.role="COLLISION";collider.worldbuilder_chunk.stable_id=f"{source.name}_col"
        outputs.append(collider)
    return outputs
def _scope_objects(scene,context):
    settings=scene.worldbuilder_bake;grid=scene.worldbuilder_chunks;mapping=exporter.chunk_collection_map(scene)
    if settings.scope=="ACTIVE_OBJECT":return [context.object] if context.object else []
    if settings.scope=="SELECTED":return list(context.selected_objects)
    coords={state.explicit_active_chunk(grid) or (0,0)} if settings.scope=="ACTIVE_CHUNK" else state.dirty_chunks(grid)
    return [obj for obj in scene.objects if exporter.object_chunk(obj,grid,mapping) in coords]
def validate(scene,objects,profile):
    issues=[];grid=scene.worldbuilder_chunks
    if profile.validation_required and grid.profile_status!="SYNCED" and not grid.developer_override:issues.append(("ERROR","Grid profile must be Synced"))
    for obj in objects:
        if obj is None or obj.type!="MESH":continue
        props=getattr(obj,"worldbuilder_chunk",None)
        if not (props and props.stable_id) and not obj.get("stable_id"):issues.append(("ERROR",f"{obj.name}: missing stable ID"))
        if obj.get("wb_chunk_split_supported") is False and exporter.object_bounds_status(obj,grid,exporter.chunk_collection_map(scene)).crosses_chunk:issues.append(("ERROR",f"{obj.name}: cross-chunk generated mesh requires clipping before export"))
        if obj.get("wb_generated_kind")=="CAVE" and profile.collider_mode=="CONVEX_HULL":issues.append(("ERROR",f"{obj.name}: a cave cannot use one convex hull collider"))
    for severity,message,obj in exporter.validate_scene(scene,grid,[value for value in objects if value is not None]):
        if severity=="ERROR":issues.append((severity,f"{obj.name + ': ' if obj else ''}{message}"))
    return issues
def write_manifests(scene,profile,outputs):
    if not profile.output_root:return []
    root=bpy.path.abspath(profile.output_root);os.makedirs(root,exist_ok=True);grid=scene.worldbuilder_chunks;mapping=exporter.chunk_collection_map(scene);grouped={}
    for obj in outputs:
        coord=exporter.object_chunk(obj,grid,mapping);grouped.setdefault(coord,[]).append(obj)
    paths=[]
    for coord,objects in sorted(grouped.items()):
        entries=[]
        sources={obj.get("wb_source_stable_id") for obj in objects}
        for source_id in sorted(sources):
            group=[obj for obj in objects if obj.get("wb_source_stable_id")==source_id];lods=[{"level":obj.get("wb_lod_level"),"fileObject":obj.name,"triangles":obj.get("wb_triangle_count",0)} for obj in group if obj.get("wb_lod_level",-1)>=0];collider=next((obj for obj in group if obj.get("wb_collider_type")),None);entry={"stableId":source_id,"name":source_id,"role":"GEOMETRY","assetId":"","lods":lods}
            if collider:entry["collider"]={"type":collider.get("wb_collider_type"),"fileObject":collider.name}
            entries.append(entry)
        attributes=[]
        for obj in objects:
            raw=obj.get("wb_vertex_attribute_contract")
            if raw:
                try:
                    contract_value=json.loads(raw)
                    if contract_value not in attributes:attributes.append(contract_value)
                except (TypeError,json.JSONDecodeError):pass
        payload=manifest.build_chunk_manifest(grid.world_id,coord,manifest.content_hash({"profile":profile.stable_id}),entries,vertex_attributes=attributes);path=os.path.join(root,f"{contract.chunk_name(coord)}.bake.json");manifest.atomic_write(path,payload);paths.append(path)
    return paths

class WB_UL_bake_profiles(UIList):
    def draw_item(self,_c,layout,_d,item,_i,_ad,_ap,_ix):layout.prop(item,"name",text="",emboss=False);layout.label(text=item.stable_id[:8])
class WB_OT_bake_profile_add(Operator):
    bl_idname="worldbuilder.bake_profile_add";bl_label="Add Bake Profile"
    def execute(self,context):item=context.scene.worldbuilder_bake.profiles.add();item.stable_id=_uuid();return {"FINISHED"}
class WB_OT_bake_validate(Operator):
    bl_idname="worldbuilder.bake_validate";bl_label="Validate Bake"
    def execute(self,context):
        profile=_profile(context.scene);issues=validate(context.scene,_scope_objects(context.scene,context),profile)
        for severity,message in issues:print(f"[WorldBuilder Bake][{severity}] {message}")
        self.report({"ERROR"} if any(v[0]=="ERROR" for v in issues) else {"INFO"},f"{len(issues)} issue(s)");return {"CANCELLED"} if any(v[0]=="ERROR" for v in issues) else {"FINISHED"}
class WB_OT_bake_run(Operator):
    bl_idname="worldbuilder.bake_run";bl_label="Bake";bl_options={"UNDO","BLOCKING"}
    _timer=None;_transaction=None;_sources=None;_outputs=None;_index=0;_profile_value=None
    def _start(self,context):
        self._profile_value=_profile(context.scene);self._sources=_scope_objects(context.scene,context);issues=validate(context.scene,self._sources,self._profile_value)
        if any(v[0]=="ERROR" for v in issues):self.report({"ERROR"},issues[0][1]);return False
        self._transaction=_BakeTransaction(context.scene,self._profile_value);self._outputs=[];self._index=0;context.window_manager.progress_begin(0,max(1,len(self._sources)));return True
    def _cleanup(self,context):
        context.window_manager.progress_end()
        if self._timer is not None:context.window_manager.event_timer_remove(self._timer);self._timer=None
    def _abort(self,context,error):
        if self._transaction:self._transaction.rollback()
        self._cleanup(context);self.report({"ERROR"},f"Bake rolled back atomically: {error}");return {"CANCELLED"}
    def _finish(self,context):
        try:
            boundary_errors=[obj for obj in self._outputs if obj.get("wb_lod_boundary_error",0)>0]
            if boundary_errors:raise RuntimeError(f"LOD chunk-boundary preservation failed on {len(boundary_errors)} output(s); export blocked")
            paths=write_manifests(context.scene,self._profile_value,self._outputs) if self._profile_value.export_manifest else []
            self._transaction.commit()
        except Exception as error:
            return self._abort(context,error)
        fallback_count=sum(1 for obj in self._outputs if obj.get("wb_lod_boundary_fallback"));context.scene.worldbuilder_bake.last_report=f"{len(self._sources)} source(s), {len(self._outputs)} output(s), {len(paths)} manifest(s), {fallback_count} boundary-safe LOD fallback(s)";self._cleanup(context);self.report({"INFO"},context.scene.worldbuilder_bake.last_report);return {"FINISHED"}
    def execute(self,context):
        if not self._start(context):return {"CANCELLED"}
        try:
            for self._index,source in enumerate(self._sources,1):self._outputs.extend(bake_object(context,source,self._profile_value));context.window_manager.progress_update(self._index)
        except Exception as error:return self._abort(context,error)
        return self._finish(context)
    def invoke(self,context,event):
        if bpy.app.background:return self.execute(context)
        if not self._start(context):return {"CANCELLED"}
        self._timer=context.window_manager.event_timer_add(.01,window=context.window);context.window_manager.modal_handler_add(self);return {"RUNNING_MODAL"}
    def modal(self,context,event):
        if event.type=="ESC":return self._abort(context,"Cancelled by user")
        if event.type!="TIMER":return {"RUNNING_MODAL"}
        if self._index>=len(self._sources):return self._finish(context)
        try:
            self._outputs.extend(bake_object(context,self._sources[self._index],self._profile_value));self._index+=1;context.window_manager.progress_update(self._index)
        except Exception as error:return self._abort(context,error)
        return {"RUNNING_MODAL"}
    def cancel(self,context):
        if self._transaction and not self._transaction.closed:self._transaction.rollback()
        self._cleanup(context)
class WB_OT_bake_clear(Operator):
    bl_idname="worldbuilder.bake_clear";bl_label="Clear Bake";bl_options={"UNDO"}
    def execute(self,context):clear_profile(context.scene,_profile(context.scene));return {"FINISHED"}

class WB_PT_bake(Panel):
    bl_label="Bake";bl_idname="WB_PT_bake";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;settings=context.scene.worldbuilder_bake;layout.template_list("WB_UL_bake_profiles","",settings,"profiles",settings,"active_index",rows=2);layout.operator("worldbuilder.bake_profile_add",icon="ADD");profile=_profile(context.scene);layout.prop(settings,"scope");layout.prop(profile,"output_root");layout.prop(profile,"generate_lods");row=layout.row(align=True);row.prop(profile,"lod1");row.prop(profile,"lod2");row.prop(profile,"lod3");layout.prop(profile,"generate_colliders");layout.prop(profile,"collider_mode");layout.label(text="Decimate cannot guarantee material/silhouette preservation",icon="INFO");row=layout.row(align=True);row.operator("worldbuilder.bake_validate");row.operator("worldbuilder.bake_run");row.operator("worldbuilder.bake_clear");layout.label(text=settings.last_report)

CLASSES=(WBBakeProfile,WBBakeSettings,WB_UL_bake_profiles,WB_OT_bake_profile_add,WB_OT_bake_validate,WB_OT_bake_run,WB_OT_bake_clear,WB_PT_bake)
def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_bake=bpy.props.PointerProperty(type=WBBakeSettings)
def unregister():
    if hasattr(bpy.types.Scene,"worldbuilder_bake"):del bpy.types.Scene.worldbuilder_bake
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
