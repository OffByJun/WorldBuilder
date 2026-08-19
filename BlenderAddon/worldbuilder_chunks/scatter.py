"""Rule-based, deterministic linked-instance scatter authoring."""

from __future__ import annotations

import bisect
import json
import math
import random
import time
import uuid

import bpy
import gpu
from bpy.app.handlers import persistent
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from mathutils import Quaternion, Vector
from gpu_extras.batch import batch_for_shader

from . import asset_library, biome, contract, exclusion, exporter, overlay, scatter_rules, scatter_state, state

SCHEMA_VERSION=2
_transform_guard=False
_triangle_cache={}
_exclusion_overlay={"batch":None,"shader":None};_exclusion_handle=None

def _rebuild_exclusion_overlay(scene):
    vertices=[]
    for obj in scene.objects:
        value=getattr(obj,"worldbuilder_exclusion",None)
        if value is None or not value.enabled or value.shape=="NONE":continue
        if value.shape=="BOX":
            corners=[obj.matrix_world@Vector((x,y,z)) for x in (-1,1) for y in (-1,1) for z in (-1,1)];edges=((0,1),(0,2),(0,4),(1,3),(1,5),(2,3),(2,6),(3,7),(4,5),(4,6),(5,7),(6,7))
            for a,b in edges:vertices.extend((corners[a],corners[b]))
        elif value.shape=="SPHERE":
            for axis in range(3):
                points=[]
                for index in range(33):
                    angle=math.tau*index/32;local=[0.0,0.0,0.0];local[(axis+1)%3]=math.cos(angle);local[(axis+2)%3]=math.sin(angle);points.append(obj.matrix_world@Vector(local))
                for a,b in zip(points,points[1:]):vertices.extend((a,b))
        elif obj.type=="CURVE":
            for spline in obj.data.splines:
                values=spline.bezier_points if spline.type=="BEZIER" else spline.points;points=[obj.matrix_world@(point.co if spline.type=="BEZIER" else point.co.xyz) for point in values]
                for a,b in zip(points,points[1:]):
                    direction=(b-a).xy
                    if direction.length<=1e-8:continue
                    direction.normalize();side=Vector((-direction.y,direction.x,0))*value.radius;vertices.extend((a+side,b+side,a-side,b-side))
    _exclusion_overlay.update(batch=None,shader=None)
    if not vertices:return
    try:
        shader=gpu.shader.from_builtin("POLYLINE_UNIFORM_COLOR");_exclusion_overlay["shader"]=shader;_exclusion_overlay["batch"]=batch_for_shader(shader,"LINES",{"pos":vertices})
    except SystemError:pass
def _draw_exclusion_overlay():
    batch,shader=_exclusion_overlay["batch"],_exclusion_overlay["shader"]
    if batch and shader:
        shader.bind();shader.uniform_float("color",(1,.15,.05,.9));shader.uniform_float("lineWidth",2);shader.uniform_float("viewportSize",gpu.state.viewport_get()[2:]);batch.draw(shader)

def _uuid(): return uuid.uuid4().hex
def _settings(context): return context.scene.worldbuilder_scatter
def _active(context):
    value=_settings(context)
    return value.layers[value.active_index] if value.layers and value.active_index<len(value.layers) else None

class WBScatterAsset(PropertyGroup):
    object: PointerProperty(name="Asset",type=bpy.types.Object)
    weight: FloatProperty(name="Weight",default=1.0,min=0.0)
    enabled: BoolProperty(name="Enabled",default=True)
    scale_multiplier: FloatProperty(name="Scale",default=1.0,min=0.001)
    vertical_offset: FloatProperty(name="Vertical Offset",default=0.0)
    role: EnumProperty(name="Role",items=(("AUTO","Auto","Follow the Structure Library asset entry"),("INSTANCE","Instance","Plain Unity prefab placement"),("ENTITY","Entity","DOTS entity placement")),default="AUTO")
    asset_id: StringProperty(name="Asset ID")

def resolve_placement(scene,entry):
    """Return (role, library item) for a scatter asset entry.

    AUTO follows the Structure Library registration so an asset registered as an
    entity scatters as an entity without configuring the layer twice.
    """
    item=asset_library.find_asset(scene,entry.asset_id)
    if entry.role=="AUTO":
        role="ENTITY" if item is not None and item.placement_kind=="ENTITY" else "INSTANCE"
    else:
        role=entry.role
    return role,item

class WBScatterTombstone(PropertyGroup):
    instance_id:StringProperty(); layer_id:StringProperty(); reason:StringProperty(default="USER_EXCLUDED"); revision:IntProperty(default=1)

class WBScatterLayer(PropertyGroup):
    stable_id:StringProperty(name="Stable ID",default="")
    name:StringProperty(name="Name",default="Scatter Layer")
    enabled:BoolProperty(name="Enabled",default=True)
    target_object:PointerProperty(name="Terrain",type=bpy.types.Object)
    source_collection:PointerProperty(name="Source Collection",type=bpy.types.Collection)
    seed:IntProperty(name="Seed",default=1)
    density:FloatProperty(name="Density / m²",default=0.02,min=0.0)
    minimum_distance:FloatProperty(name="Minimum Distance",default=1.0,min=0.0)
    max_instances:IntProperty(name="Max Instances",default=10000,min=0,max=1000000)
    biome_id:StringProperty(name="Biome ID")
    biome_min_weight:FloatProperty(name="Minimum Biome Weight",default=0.0,min=0,max=1)
    exclusion_biome_id:StringProperty(name="Exclusion Biome ID")
    exclusion_min_weight:FloatProperty(name="Exclusion Weight",default=1.0,min=0,max=1)
    min_height:FloatProperty(name="Min Height",default=-100000.0); max_height:FloatProperty(name="Max Height",default=100000.0)
    min_slope_degrees:FloatProperty(name="Min Slope",default=0,min=0,max=180); max_slope_degrees:FloatProperty(name="Max Slope",default=180,min=0,max=180)
    scale_min:FloatProperty(name="Scale Min",default=.8,min=.001); scale_max:FloatProperty(name="Scale Max",default=1.2,min=.001)
    yaw_min:FloatProperty(name="Yaw Min",default=0); yaw_max:FloatProperty(name="Yaw Max",default=math.tau)
    align_to_normal:BoolProperty(name="Align to Normal",default=True); normal_align_strength:FloatProperty(name="Normal Align",default=1,min=0,max=1)
    preview_only:BoolProperty(name="Preview Only",default=True); chunk_aware:BoolProperty(name="Chunk Aware",default=True); enabled_for_export:BoolProperty(name="Export",default=True)
    preserve_manual_edits:BoolProperty(name="Preserve Manual Edits",default=True); preserve_deleted_instances:BoolProperty(name="Preserve Deleted",default=True); remove_orphans:BoolProperty(name="Remove Orphans",default=False)
    assets:CollectionProperty(type=WBScatterAsset); active_asset_index:IntProperty(default=0)
    tombstones:CollectionProperty(type=WBScatterTombstone)
    generated_ids_json:StringProperty(default="[]",options={"HIDDEN"})
    statistics:StringProperty(name="Statistics",default="Not generated")

class WBScatterSettings(PropertyGroup):
    layers:CollectionProperty(type=WBScatterLayer); active_index:IntProperty(default=0)

class WBExclusionSettings(PropertyGroup):
    shape:EnumProperty(name="Shape",items=(("NONE","None",""),("BOX","Box",""),("SPHERE","Sphere",""),("CURVE","Curve Corridor","")),default="NONE")
    stable_id:StringProperty(name="Stable ID",default="")
    enabled:BoolProperty(name="Enabled",default=True); affects_all_layers:BoolProperty(name="All Layers",default=True); affected_layer_ids:StringProperty(name="Layer IDs")
    radius:FloatProperty(name="Radius",default=2,min=0); falloff:FloatProperty(name="Falloff",default=0,min=0); hard_exclusion:BoolProperty(name="Hard",default=True); priority:IntProperty(name="Priority",default=0)

def _generated_root(scene):
    value=bpy.data.collections.get("WB_SCATTER_GENERATED")
    if value is None: value=bpy.data.collections.new("WB_SCATTER_GENERATED"); scene.collection.children.link(value)
    return value

def _layer_collection(scene, layer):
    name=f"WB_SCATTER_{layer.stable_id}"
    value=bpy.data.collections.get(name)
    if value is None: value=bpy.data.collections.new(name); _generated_root(scene).children.link(value)
    value["wb_scatter_layer_id"]=layer.stable_id
    return value

def _ensure_assets(layer):
    if layer.assets or layer.source_collection is None:return
    for source in sorted(layer.source_collection.objects,key=lambda value:value.name):
        if source.type not in {"MESH","EMPTY"}:continue
        entry=layer.assets.add();entry.object=source;entry.asset_id=source.get("asset_id") or source.name

def _triangles(obj,depsgraph):
    key=obj.as_pointer()
    cached=_triangle_cache.get(key)
    if cached is not None:return cached
    evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
    try:
        if len(mesh.vertices)!=len(obj.data.vertices):raise ValueError("Scatter terrain cannot use topology-changing modifiers")
        mesh.calc_loop_triangles();points=[obj.matrix_world@v.co for v in mesh.vertices]
        result=[];cumulative=[];total=0.0
        for triangle in mesh.loop_triangles:
            indices=tuple(triangle.vertices);a,b,c=(points[i] for i in indices);area=(b-a).cross(c-a).length*.5
            if area<=1e-10:continue
            total+=area;result.append((indices,a,b,c));cumulative.append(total)
        value=(result,cumulative,total);_triangle_cache[key]=value;return value
    finally:evaluated.to_mesh_clear()

def _volume_weight(scene,position,layer_id):
    result=0.0
    for obj in scene.objects:
        value=getattr(obj,"worldbuilder_exclusion",None)
        if value is None or not value.enabled or value.shape=="NONE":continue
        if not value.affects_all_layers and layer_id not in {item.strip() for item in value.affected_layer_ids.split(",")}:continue
        local=obj.matrix_world.inverted_safe()@position
        if value.shape=="BOX": distance=max(abs(local.x)-1,abs(local.y)-1,abs(local.z)-1,0)*max(obj.scale)
        elif value.shape=="SPHERE": distance=max(0,local.length-1)*max(obj.scale); inside=local.length<=1
        else:
            points=[]
            if obj.type=="CURVE":
                for spline in obj.data.splines:
                    values=spline.bezier_points if spline.type=="BEZIER" else spline.points
                    points.extend([obj.matrix_world@(p.co if spline.type=="BEZIER" else p.co.xyz) for p in values])
            distance=exclusion.distance_to_curve_xy(position,points)-value.radius
        result=max(result,exclusion.soft_weight(distance,0,value.falloff) if not value.hard_exclusion else (1.0 if distance<=0 else 0.0))
    return result

def _generated_records(layer):
    collection=bpy.data.collections.get(f"WB_SCATTER_{layer.stable_id}")
    if collection is None:return {}
    return {obj.get("wb_scatter_instance_id"):obj for obj in collection.all_objects if obj.get("wb_scatter_instance_id")}

def _remove_object(obj):
    bpy.data.objects.remove(obj,do_unlink=True)

def generate(context,layer,preview):
    global _transform_guard
    if layer.target_object is None or layer.target_object.type!="MESH":raise ValueError("Scatter target must be a Mesh")
    _ensure_assets(layer);assets=[entry for entry in layer.assets if entry.enabled and entry.object and entry.weight>0]
    if not assets:raise ValueError("Scatter layer has no enabled weighted assets")
    placements=[resolve_placement(context.scene,entry) for entry in assets]
    triangles,cumulative,total=_triangles(layer.target_object,context.evaluated_depsgraph_get())
    desired=min(layer.max_instances,max(0,int(total*layer.density)));attempts=min(max(desired*8,desired),max(layer.max_instances*16,1000))
    randomizer=random.Random(scatter_rules.stable_seed(layer.stable_id,layer.seed));distance_grid=scatter_rules.SpatialHash(layer.minimum_distance)
    old=_generated_records(layer)
    if layer.preserve_deleted_instances:
        try:previous_ids=set(json.loads(layer.generated_ids_json or "[]"))
        except (TypeError,ValueError):previous_ids=set()
        known_tombstones={item.instance_id for item in layer.tombstones}
        for missing in sorted(previous_ids-set(old)-known_tombstones):
            item=layer.tombstones.add();item.instance_id=missing;item.layer_id=layer.stable_id;item.reason="USER_DELETED"
    tombstones={item.instance_id for item in layer.tombstones};accepted=[];reject={key:0 for key in ("HEIGHT","SLOPE","BIOME","EXCLUSION_BIOME","EXCLUSION","DISTANCE")}
    definitions={item.stable_id:item for item in biome.biome_definitions(context.scene)}
    for sample_index in range(attempts):
        if len(accepted)>=desired:break
        cursor=randomizer.random()*total;triangle=triangles[bisect.bisect_left(cumulative,cursor)];indices,a,b,c=triangle
        r1=math.sqrt(randomizer.random());r2=randomizer.random();weights=(1-r1,r1*(1-r2),r1*r2);position=a*weights[0]+b*weights[1]+c*weights[2];normal=(b-a).cross(c-a).normalized();slope=scatter_rules.slope_degrees(normal)
        biome_weight=1.0;exclusion_biome_weight=0.0
        if layer.biome_id and layer.biome_id in definitions:
            attribute=layer.target_object.data.attributes.get(definitions[layer.biome_id].attribute_name)
            biome_weight=sum(attribute.data[index].value*weight for index,weight in zip(indices,weights)) if attribute else 0
        if layer.exclusion_biome_id and layer.exclusion_biome_id in definitions:
            attribute=layer.target_object.data.attributes.get(definitions[layer.exclusion_biome_id].attribute_name)
            exclusion_biome_weight=sum(attribute.data[index].value*weight for index,weight in zip(indices,weights)) if attribute else 0
        candidate={"position":position,"normal":normal,"slope":slope,"biome_weight":biome_weight,"exclusion_biome_weight":exclusion_biome_weight}
        rules={"min_height":layer.min_height,"max_height":layer.max_height,"min_slope":layer.min_slope_degrees,"max_slope":layer.max_slope_degrees,"biome_min":layer.biome_min_weight,"exclusion_biome_min":layer.exclusion_min_weight if layer.exclusion_biome_id else math.inf}
        ok,reason=scatter_rules.evaluate_candidate(candidate,rules,_volume_weight(context.scene,position,layer.stable_id))
        if not ok:reject[reason]=reject.get(reason,0)+1;continue
        if not distance_grid.can_insert(position):reject["DISTANCE"]+=1;continue
        key=scatter_rules.candidate_key(layer.stable_id,sample_index,position);instance_id=scatter_state.instance_id(layer.stable_id,key)
        if instance_id in tombstones:continue
        distance_grid.insert(position);accepted.append((sample_index,key,instance_id,position,normal))
    collection=_layer_collection(context.scene,layer);keep=set();_transform_guard=True
    try:
        for sample_index,key,instance_id,position,normal in accepted:
            keep.add(instance_id);existing=old.get(instance_id)
            if existing and layer.preserve_manual_edits and existing.get("wb_scatter_state") in {scatter_state.MANUALLY_MOVED,scatter_state.LOCKED}:continue
            asset_index=scatter_rules.weighted_index([item.weight for item in assets],scatter_rules.stable_seed(layer.seed,key));entry=assets[asset_index];obj=existing or entry.object.copy();coordinate=contract.chunk_coord_from_xy(position.x,position.y,context.scene.worldbuilder_chunks.origin_x,context.scene.worldbuilder_chunks.origin_y,context.scene.worldbuilder_chunks.chunk_size)
            target_collection=collection
            if existing is None:obj.data=entry.object.data;target_collection.objects.link(obj)
            elif obj.name not in target_collection.objects:
                for owner in list(obj.users_collection):
                    if owner.get("wb_scatter_layer_id")==layer.stable_id:owner.objects.unlink(obj)
                target_collection.objects.link(obj)
            obj.name=f"SC_{layer.name}_{instance_id[:8]}";scale=random.Random(scatter_rules.stable_seed(key,"scale")).uniform(layer.scale_min,layer.scale_max)*entry.scale_multiplier;yaw=random.Random(scatter_rules.stable_seed(key,"yaw")).uniform(layer.yaw_min,layer.yaw_max)
            obj.location=position+normal*entry.vertical_offset;obj.rotation_mode="QUATERNION";alignment=Vector((0,0,1)).rotation_difference(normal);alignment=Quaternion().slerp(alignment,layer.normal_align_strength) if layer.align_to_normal else Quaternion();obj.rotation_quaternion=Quaternion(normal if layer.align_to_normal else Vector((0,0,1)),yaw)@alignment
            obj.scale=(scale,scale,scale);obj["wb_scatter_schema_version"]=SCHEMA_VERSION;obj["wb_scatter_layer_id"]=layer.stable_id;obj["wb_scatter_instance_id"]=instance_id;obj["wb_scatter_sample_key"]=key;obj["wb_scatter_asset_id"]=entry.asset_id;obj["wb_scatter_state"]=scatter_state.GENERATED;obj["wb_generated_position"]=list(obj.location);obj["wb_generated_rotation"]=list(obj.rotation_quaternion);obj["wb_generated_scale"]=list(obj.scale);obj["wb_scatter_preview"]=bool(preview)
            if hasattr(obj,"worldbuilder_chunk"):
                placement_role,library_item=placements[asset_index];obj.worldbuilder_chunk.role=placement_role;obj.worldbuilder_chunk.asset_id=entry.asset_id;obj.worldbuilder_chunk.stable_id=instance_id;obj.worldbuilder_chunk.override_chunk=layer.chunk_aware;obj.worldbuilder_chunk.chunk_x=coordinate[0];obj.worldbuilder_chunk.chunk_z=coordinate[1]
                if placement_role=="ENTITY" and library_item is not None:asset_library.apply_entity_properties(obj.worldbuilder_chunk,library_item)
            if layer.chunk_aware:obj["wb_chunk_x"]=coordinate[0];obj["wb_chunk_z"]=coordinate[1]
            state.mark_dirty(context.scene.worldbuilder_chunks,coordinate)
        for instance_id,obj in old.items():
            if instance_id not in keep and obj.get("wb_scatter_state") not in {scatter_state.MANUALLY_MOVED,scatter_state.LOCKED}:_remove_object(obj)
    finally:_transform_guard=False
    layer.generated_ids_json=json.dumps(sorted(keep),separators=(",",":"));layer.preview_only=preview;layer.statistics=f"Candidates {attempts} | Accepted {len(accepted)} | "+" ".join(f"{k}:{v}" for k,v in reject.items() if v)
    overlay.invalidate_all();return len(accepted)

class WB_UL_scatter_layers(UIList):
    def draw_item(self,_c,layout,_d,item,_i,_ad,_ap,_ix):layout.prop(item,"enabled",text="");layout.prop(item,"name",text="",emboss=False);layout.label(text=item.statistics)
class WB_UL_scatter_assets(UIList):
    def draw_item(self,context,layout,_d,item,_i,_ad,_ap,_ix):
        layout.prop(item,"enabled",text="");layout.prop(item,"object",text="");layout.prop(item,"weight",text="W")
        role,_=resolve_placement(context.scene,item)
        if role=="ENTITY":layout.label(text="",icon="OUTLINER_OB_POINTCLOUD")

class WB_OT_scatter_add(Operator):
    bl_idname="worldbuilder.scatter_add";bl_label="Add Scatter Layer";bl_options={"UNDO"}
    def execute(self,context):
        item=_settings(context).layers.add();item.stable_id=_uuid();_settings(context).active_index=len(_settings(context).layers)-1;return {"FINISHED"}
class WB_OT_scatter_sync_assets(Operator):
    bl_idname="worldbuilder.scatter_sync_assets";bl_label="Populate Assets from Collection";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context)
        if not layer or layer.source_collection is None:return {"CANCELLED"}
        layer.assets.clear();_ensure_assets(layer);self.report({"INFO"},f"Loaded {len(layer.assets)} asset entries");return {"FINISHED"}
class WB_OT_scatter_remove(Operator):
    bl_idname="worldbuilder.scatter_remove";bl_label="Remove Scatter Layer";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context)
        if not layer:return {"CANCELLED"}
        collection=bpy.data.collections.get(f"WB_SCATTER_{layer.stable_id}")
        if collection:
            for obj in list(collection.all_objects):_remove_object(obj)
            for child in list(collection.children):bpy.data.collections.remove(child)
            bpy.data.collections.remove(collection)
        _settings(context).layers.remove(_settings(context).active_index);return {"FINISHED"}
class _GenerateBase:
    preview=True
    def execute(self,context):
        layer=_active(context)
        try:count=generate(context,layer,self.preview)
        except (ValueError,RuntimeError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Generated {count} linked instances");return {"FINISHED"}
class WB_OT_scatter_preview(_GenerateBase,Operator):bl_idname="worldbuilder.scatter_preview";bl_label="Generate Preview";bl_options={"REGISTER","UNDO"}
class WB_OT_scatter_apply(_GenerateBase,Operator):bl_idname="worldbuilder.scatter_apply";bl_label="Apply Layer";bl_options={"REGISTER","UNDO"};preview=False
class WB_OT_scatter_clear(Operator):
    bl_idname="worldbuilder.scatter_clear";bl_label="Clear Generated";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context);collection=bpy.data.collections.get(f"WB_SCATTER_{layer.stable_id}") if layer else None
        if collection:
            for obj in list(collection.all_objects):_remove_object(obj)
        return {"FINISHED"}
class WB_OT_scatter_exclude_selected(Operator):
    bl_idname="worldbuilder.scatter_exclude_selected";bl_label="Exclude Selected";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context);count=0
        for obj in list(context.selected_objects):
            if layer and obj.get("wb_scatter_layer_id")==layer.stable_id:
                item=layer.tombstones.add();item.instance_id=obj["wb_scatter_instance_id"];item.layer_id=layer.stable_id;_remove_object(obj);count+=1
        self.report({"INFO"},f"Excluded {count}");return {"FINISHED"}
class WB_OT_scatter_lock_selected(Operator):
    bl_idname="worldbuilder.scatter_lock_selected";bl_label="Lock Selected";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context);count=0
        for obj in context.selected_objects:
            if layer and obj.get("wb_scatter_layer_id")==layer.stable_id:obj["wb_scatter_state"]=scatter_state.LOCKED;count+=1
        self.report({"INFO"},f"Locked {count}");return {"FINISHED"}
class WB_OT_scatter_reset_selected(Operator):
    bl_idname="worldbuilder.scatter_reset_selected";bl_label="Reset Selected";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context);count=0
        for obj in context.selected_objects:
            if layer and obj.get("wb_scatter_layer_id")==layer.stable_id:
                obj.location=obj.get("wb_generated_position",obj.location);obj.rotation_mode="QUATERNION";obj.rotation_quaternion=obj.get("wb_generated_rotation",obj.rotation_quaternion);obj.scale=obj.get("wb_generated_scale",obj.scale);obj["wb_scatter_state"]=scatter_state.GENERATED;count+=1
        self.report({"INFO"},f"Reset {count}");return {"FINISHED"}
class WB_OT_scatter_clear_tombstones(Operator):
    bl_idname="worldbuilder.scatter_clear_tombstones";bl_label="Clear Tombstones";bl_options={"UNDO"}
    def execute(self,context):
        layer=_active(context)
        if layer:layer.tombstones.clear()
        return {"FINISHED"}
class WB_OT_exclusion_create(Operator):
    bl_idname="worldbuilder.exclusion_create";bl_label="Create Exclusion";shape:EnumProperty(items=(("BOX","Box",""),("SPHERE","Sphere","")))
    def execute(self,context):
        if self.shape=="BOX":bpy.ops.object.empty_add(type="CUBE")
        else:bpy.ops.object.empty_add(type="SPHERE")
        obj=context.object;obj.name=f"WB_EXCLUSION_{self.shape}_{_uuid()[:8]}";obj.worldbuilder_exclusion.shape=self.shape;obj.worldbuilder_exclusion.stable_id=_uuid();_rebuild_exclusion_overlay(context.scene);return {"FINISHED"}
class WB_OT_exclusion_from_curve(Operator):
    bl_idname="worldbuilder.exclusion_from_curve";bl_label="Use Active Curve as Exclusion";bl_options={"UNDO"}
    def execute(self,context):
        obj=context.object
        if obj is None or obj.type!="CURVE":self.report({"ERROR"},"Select a Curve object");return {"CANCELLED"}
        obj.worldbuilder_exclusion.shape="CURVE";obj.worldbuilder_exclusion.stable_id=obj.worldbuilder_exclusion.stable_id or _uuid();_rebuild_exclusion_overlay(context.scene);return {"FINISHED"}

class WB_PT_scatter(Panel):
    bl_label="Scatter";bl_idname="WB_PT_rule_scatter";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def _draw_active_asset(self,layout,context,layer):
        entry=layer.assets[layer.active_asset_index] if layer.assets and layer.active_asset_index<len(layer.assets) else None
        if entry is None:return
        box=layout.box();row=box.row(align=True);row.prop(entry,"asset_id");row.prop(entry,"role",text="")
        role,item=resolve_placement(context.scene,entry)
        if role!="ENTITY":return
        if item is None:box.label(text="Entity role needs a registered Structure Library asset",icon="ERROR")
        else:box.label(text=f"{item.entity_kind} #{item.entity_prefab_id}",icon="OUTLINER_OB_POINTCLOUD")

    def draw(self,context):
        layout=self.layout;settings=_settings(context);layout.template_list("WB_UL_scatter_layers","",settings,"layers",settings,"active_index",rows=3);row=layout.row(align=True);row.operator("worldbuilder.scatter_add",text="",icon="ADD");row.operator("worldbuilder.scatter_remove",text="",icon="REMOVE")
        layer=_active(context)
        if layer:
            layout.prop(layer,"target_object");layout.prop(layer,"source_collection");layout.operator("worldbuilder.scatter_sync_assets",icon="FILE_REFRESH");layout.template_list("WB_UL_scatter_assets","",layer,"assets",layer,"active_asset_index",rows=2);self._draw_active_asset(layout,context,layer);row=layout.row(align=True);row.prop(layer,"seed");row.prop(layer,"density");row=layout.row(align=True);row.prop(layer,"minimum_distance");row.prop(layer,"max_instances");row=layout.row(align=True);row.prop(layer,"min_height");row.prop(layer,"max_height");row=layout.row(align=True);row.prop(layer,"min_slope_degrees");row.prop(layer,"max_slope_degrees");layout.prop(layer,"biome_id");layout.prop(layer,"biome_min_weight");layout.prop(layer,"preserve_manual_edits");layout.prop(layer,"preserve_deleted_instances");row=layout.row(align=True);row.operator("worldbuilder.scatter_preview");row.operator("worldbuilder.scatter_apply");row=layout.row(align=True);row.operator("worldbuilder.scatter_clear");row.operator("worldbuilder.scatter_exclude_selected");row=layout.row(align=True);row.operator("worldbuilder.scatter_lock_selected");row.operator("worldbuilder.scatter_reset_selected");row.operator("worldbuilder.scatter_clear_tombstones");collection=bpy.data.collections.get(f"WB_SCATTER_{layer.stable_id}");states=[obj.get("wb_scatter_state") for obj in collection.all_objects] if collection else [];layout.label(text=f"Moved {states.count(scatter_state.MANUALLY_MOVED)} | Locked {states.count(scatter_state.LOCKED)} | Tombstones {len(layer.tombstones)}");layout.label(text=layer.statistics)
        box=layout.box();box.label(text="Exclusion Volumes");row=box.row(align=True);op=row.operator("worldbuilder.exclusion_create",text="Box");op.shape="BOX";op=row.operator("worldbuilder.exclusion_create",text="Sphere");op.shape="SPHERE";row.operator("worldbuilder.exclusion_from_curve",text="Active Curve")
        obj=context.object
        if obj and getattr(obj,"worldbuilder_exclusion",None) and obj.worldbuilder_exclusion.shape!="NONE":box.prop(obj.worldbuilder_exclusion,"shape");box.prop(obj.worldbuilder_exclusion,"falloff");box.prop(obj.worldbuilder_exclusion,"hard_exclusion")

@persistent
def depsgraph_update(_scene,depsgraph):
    if _transform_guard:return
    for update in depsgraph.updates:
        obj=update.id
        if isinstance(obj,bpy.types.Object) and obj.type=="MESH" and (getattr(update,"is_updated_geometry",False) or getattr(update,"is_updated_transform",False)):_triangle_cache.pop(obj.as_pointer(),None)
        if isinstance(obj,bpy.types.Object) and getattr(obj,"worldbuilder_exclusion",None) and obj.worldbuilder_exclusion.shape!="NONE":_rebuild_exclusion_overlay(_scene)
        if not isinstance(obj,bpy.types.Object) or not obj.get("wb_scatter_instance_id") or not getattr(update,"is_updated_transform",True):continue
        current=(tuple(obj.location),tuple(obj.rotation_quaternion),tuple(obj.scale));generated=(tuple(obj.get("wb_generated_position",obj.location)),tuple(obj.get("wb_generated_rotation",obj.rotation_quaternion)),tuple(obj.get("wb_generated_scale",obj.scale)))
        obj["wb_scatter_state"]=scatter_state.MANUALLY_MOVED if scatter_state.transform_changed(current,generated) else scatter_state.GENERATED

CLASSES=(WBScatterAsset,WBScatterTombstone,WBScatterLayer,WBScatterSettings,WBExclusionSettings,WB_UL_scatter_layers,WB_UL_scatter_assets,WB_OT_scatter_add,WB_OT_scatter_sync_assets,WB_OT_scatter_remove,WB_OT_scatter_preview,WB_OT_scatter_apply,WB_OT_scatter_clear,WB_OT_scatter_exclude_selected,WB_OT_scatter_lock_selected,WB_OT_scatter_reset_selected,WB_OT_scatter_clear_tombstones,WB_OT_exclusion_create,WB_OT_exclusion_from_curve,WB_PT_scatter)
def register():
    global _exclusion_handle
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_scatter=PointerProperty(type=WBScatterSettings);bpy.types.Object.worldbuilder_exclusion=PointerProperty(type=WBExclusionSettings)
    _exclusion_handle=bpy.types.SpaceView3D.draw_handler_add(_draw_exclusion_overlay,(),"WINDOW","POST_VIEW")
    if depsgraph_update not in bpy.app.handlers.depsgraph_update_post:bpy.app.handlers.depsgraph_update_post.append(depsgraph_update)
def unregister():
    global _exclusion_handle
    if _exclusion_handle:bpy.types.SpaceView3D.draw_handler_remove(_exclusion_handle,"WINDOW");_exclusion_handle=None
    if depsgraph_update in bpy.app.handlers.depsgraph_update_post:bpy.app.handlers.depsgraph_update_post.remove(depsgraph_update)
    if hasattr(bpy.types.Object,"worldbuilder_exclusion"):del bpy.types.Object.worldbuilder_exclusion
    if hasattr(bpy.types.Scene,"worldbuilder_scatter"):del bpy.types.Scene.worldbuilder_scatter
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
