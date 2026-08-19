"""WorldBuilder curve authoring, non-destructive terrain carve, and generated spline meshes."""

from __future__ import annotations

import json
import math
import uuid

import bpy
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from mathutils import Vector
from mathutils.geometry import interpolate_bezier

from . import biome, chunk_clipper, contract, exporter, overlay, spline_contract, spline_mesh, spline_sampling, state, terrain_carve

_terrain_basis_cache={}

def _uuid():return uuid.uuid4().hex
def _props(obj):return getattr(obj,"worldbuilder_spline",None)

class WBSplineModifier(PropertyGroup):
    stable_id:StringProperty(default="");enabled:BoolProperty(name="Enabled",default=True)
    type:EnumProperty(name="Type",items=((value,value.replace("_"," ").title(),"") for value in spline_contract.MODIFIER_TYPES),default="TERRAIN_CARVE")
    target:PointerProperty(name="Target Terrain",type=bpy.types.Object)
    width:FloatProperty(name="Width",default=4,min=.001);depth:FloatProperty(name="Depth / Height",default=2,min=0);falloff_distance:FloatProperty(name="Falloff",default=2,min=0)
    profile:EnumProperty(name="Profile",items=((v,v.replace("_"," ").title(),"") for v in ("V","U","FLAT_BOTTOM","SMOOTH")),default="SMOOTH")
    falloff:EnumProperty(name="Falloff Shape",items=((v,v.title(),"") for v in ("LINEAR","SMOOTH","SHARP")),default="SMOOTH")
    preserve_boundary:BoolProperty(name="Preserve Boundary",default=True);preview_only:BoolProperty(name="Preview Only",default=True)

class WBSplineProperties(PropertyGroup):
    enabled:BoolProperty(name="WorldBuilder Spline",default=False);schema_version:IntProperty(default=1);stable_id:StringProperty(default="")
    type:EnumProperty(name="Spline Type",items=((value,value.replace("_"," ").title(),"") for value in spline_contract.SPLINE_TYPES),default="PATH")
    sample_spacing:FloatProperty(name="Sample Spacing",default=1,min=.01);max_samples:IntProperty(name="Max Samples",default=100000,min=2)
    modifiers:CollectionProperty(type=WBSplineModifier);active_modifier:IntProperty(default=0)
    chunk_split:BoolProperty(name="Split at Chunk Boundaries",default=True);mesh_radius:FloatProperty(name="Radius / Width",default=2,min=.01);radial_segments:IntProperty(name="Radial Segments",default=12,min=3,max=128);cap_start:BoolProperty(name="Cap Start",default=False);cap_end:BoolProperty(name="Cap End",default=False);taper:FloatProperty(name="End Taper",default=0,min=0,max=.99);entrance_flare:FloatProperty(name="Entrance Flare",default=.25,min=0,max=4);cave_flat_floor:FloatProperty(name="Flat Floor",default=.2,min=0,max=.9);cave_roughness:FloatProperty(name="Cave Radius Variation",default=.08,min=0,max=.5);cliff_height:FloatProperty(name="Cliff Height",default=8,min=.01);cliff_vertical_segments:IntProperty(name="Vertical Segments",default=2,min=1,max=64);cliff_overhang:FloatProperty(name="Cliff Overhang",default=.6,min=0,max=20);cliff_roughness:FloatProperty(name="Cliff Roughness",default=.25,min=0,max=5);flip_side:BoolProperty(name="Flip Side",default=False);path_shoulder:FloatProperty(name="Shoulder",default=0,min=0);river_depth:FloatProperty(name="Riverbed Depth",default=1.5,min=0);river_bottom_ratio:FloatProperty(name="Bottom Width Ratio",default=.55,min=.05,max=.95);river_bank_width:FloatProperty(name="Bank Width",default=2,min=0);z_offset:FloatProperty(name="Z Offset",default=.02)
    last_report:StringProperty(default="")

def sample_curve(obj,spacing=None):
    if bpy.context.view_layer is not None:bpy.context.view_layer.update()
    value=_props(obj);points=[];cyclic=False
    for spline in obj.data.splines:
        cyclic=cyclic or spline.use_cyclic_u
        if spline.type=="BEZIER":
            items=spline.bezier_points
            if len(items)<2:continue
            segment_count=len(items) if spline.use_cyclic_u else len(items)-1
            for index in range(segment_count):
                a=items[index];b=items[(index+1)%len(items)]
                values=interpolate_bezier(a.co,a.handle_right,b.handle_left,b.co,max(2,obj.data.resolution_u+2))
                points.extend(obj.matrix_world@point for point in (values[:-1] if index<segment_count-1 else values))
        elif spline.type=="POLY":points.extend(obj.matrix_world@point.co.xyz for point in spline.points)
        else:raise ValueError("NURBS sampling is not supported; convert the spline to Bezier or Poly")
    return spline_sampling.sample_polyline(points,spacing or value.sample_spacing,cyclic,value.max_samples)

def _basis_mesh(target):
    name=target.get("wb_terrain_basis_mesh")
    basis=bpy.data.meshes.get(name) if name else None
    if basis is None:
        basis=target.data.copy();basis.name=f"WB_BASIS_{target.name}_{_uuid()[:8]}";target["wb_terrain_basis_mesh"]=basis.name
    return basis

def _basis_positions(target,basis):
    key=(target.as_pointer(),basis.as_pointer(),int(target.get("wb_sculpt_revision",0)),tuple(round(value,9) for row in target.matrix_world for value in row))
    cached=_terrain_basis_cache.get(key)
    if cached is None:
        delta=target.data.attributes.get("wb_sculpt_delta") if target.type=="MESH" else None
        cached=[tuple(target.matrix_world@(vertex.co+(Vector(delta.data[vertex.index].vector) if delta and vertex.index<len(delta.data) else Vector((0,0,0))))) for vertex in basis.vertices]
        _terrain_basis_cache.clear();_terrain_basis_cache[key]=cached
    return list(cached)

def rebuild_carves(scene,spline_obj):
    props=_props(spline_obj);samples=sample_curve(spline_obj);polyline=[item["position"] for item in samples]
    count=0; grouped={}
    for modifier in props.modifiers:
        if modifier.enabled and modifier.type in {"TERRAIN_CARVE","TERRAIN_RAISE"} and modifier.target and modifier.target.type=="MESH":
            grouped.setdefault(modifier.target,[]).append(modifier)
    for target,modifiers in grouped.items():
        basis=_basis_mesh(target);positions=_basis_positions(target,basis);inverse=target.matrix_world.inverted_safe()
        bounds=exporter.world_bounds(target);boundary=None
        if bounds and any(modifier.preserve_boundary for modifier in modifiers):
            minimum,maximum=bounds;epsilon=1e-4
            boundary=lambda p,mn=minimum,mx=maximum:abs(p[0]-mn.x)<epsilon or abs(p[0]-mx.x)<epsilon or abs(p[1]-mn.y)<epsilon or abs(p[1]-mx.y)<epsilon
        for modifier in modifiers:
            positions=terrain_carve.rebuild_vertices(positions,polyline,{"width":modifier.width,"depth":modifier.depth,"falloff":modifier.falloff_distance,"profile":modifier.profile,"falloff_kind":modifier.falloff,"raise":modifier.type=="TERRAIN_RAISE","preserve_boundary":modifier.preserve_boundary},boundary)
        for vertex,value in zip(target.data.vertices,positions):vertex.co=inverse@Vector(value)
        target.data.update();biome.invalidate_biome_cache(target);target["wb_terrain_modified_by_spline"]=spline_obj.worldbuilder_spline.stable_id
        grid=scene.worldbuilder_chunks;coord=contract.chunk_coord_from_xy(target.location.x,target.location.y,grid.origin_x,grid.origin_y,grid.chunk_size);state.mark_dirty(grid,coord);count+=1
    overlay.invalidate_all();return count

def _generated_collection(scene):
    collection=bpy.data.collections.get("WB_SPLINE_GENERATED")
    if collection is None:collection=bpy.data.collections.new("WB_SPLINE_GENERATED");scene.collection.children.link(collection)
    return collection

def _remove_generated(spline_id,kind):
    for obj in list(bpy.data.objects):
        if obj.get("wb_source_spline_id")==spline_id and obj.get("wb_generated_kind")==kind:bpy.data.objects.remove(obj,do_unlink=True)

def _split_generated_mesh(scene,obj):
    grid=scene.worldbuilder_chunks;mesh=obj.data;mesh.calc_loop_triangles();uv_layer=mesh.uv_layers.active;triangles=[]
    for triangle in mesh.loop_triangles:
        corners=[]
        for vertex_index,loop_index in zip(triangle.vertices,triangle.loops):
            vertex=mesh.vertices[vertex_index];record={"position":tuple(obj.matrix_world@vertex.co),"normal":tuple((obj.matrix_world.to_3x3()@vertex.normal).normalized())}
            if uv_layer:record["uv"]=tuple(uv_layer.data[loop_index].uv)
            corners.append(record)
        triangles.append({"vertices":corners,"material":mesh.polygons[triangle.polygon_index].material_index})
    outputs=chunk_clipper.clip_triangles(triangles,(grid.origin_x,grid.origin_y),grid.chunk_size)
    if not outputs:raise ValueError(f"{obj.name}: clipping produced no non-degenerate triangles from {len(triangles)} input triangles; first={triangles[0] if triangles else None}")
    created=[];collection=_generated_collection(scene)
    try:
        for coordinate,data in outputs.items():
            name=f"{obj.name}_{contract.chunk_name(coordinate)}";new_mesh=bpy.data.meshes.new(name+"Mesh");new_mesh.from_pydata([vertex["position"] for vertex in data["vertices"]],[],data["faces"])
            for material in mesh.materials:new_mesh.materials.append(material)
            for polygon,material_index in zip(new_mesh.polygons,data["materials"]):polygon.material_index=min(material_index,max(0,len(new_mesh.materials)-1))
            if any("uv" in vertex for vertex in data["vertices"]):
                layer=new_mesh.uv_layers.new(name=uv_layer.name if uv_layer else "UVMap")
                for loop in new_mesh.loops:layer.data[loop.index].uv=data["vertices"][loop.vertex_index].get("uv",(0,0))
            new_mesh.update();chunk_obj=bpy.data.objects.new(name,new_mesh);collection.objects.link(chunk_obj);chunk_obj["wb_source_spline_id"]=obj.get("wb_source_spline_id");chunk_obj["wb_generated_kind"]=obj.get("wb_generated_kind");chunk_obj["wb_spline_schema_version"]=1;chunk_obj["wb_chunk_split_supported"]=True;chunk_obj["wb_chunk_x"]=coordinate[0];chunk_obj["wb_chunk_z"]=coordinate[1]
            if hasattr(chunk_obj,"worldbuilder_chunk"):
                chunk_obj.worldbuilder_chunk.role="GEOMETRY";chunk_obj.worldbuilder_chunk.stable_id=f"{obj.get('wb_source_spline_id')}_{str(obj.get('wb_generated_kind')).lower()}_{coordinate[0]}_{coordinate[1]}";chunk_obj.worldbuilder_chunk.override_chunk=True;chunk_obj.worldbuilder_chunk.chunk_x=coordinate[0];chunk_obj.worldbuilder_chunk.chunk_z=coordinate[1]
            created.append(chunk_obj);state.mark_dirty(grid,coordinate)
    except Exception:
        for value in created:bpy.data.objects.remove(value,do_unlink=True)
        raise
    bpy.data.objects.remove(obj,do_unlink=True);return created

def generate_mesh(scene,spline_obj,kind):
    props=_props(spline_obj);samples=sample_curve(spline_obj);_remove_generated(props.stable_id,kind)
    if len(samples)<2:raise ValueError("Spline needs at least two valid samples")
    uvs=None
    if kind=="CAVE":
        radii=[props.mesh_radius*(1+props.entrance_flare*(1-sample["normalized_distance"])**2)*(1-props.taper*sample["normalized_distance"])*(1+props.cave_roughness*math.sin(sample["cumulative_distance"]*.73+math.sin(sample["cumulative_distance"]*.19)*2.1)) for sample in samples];data=spline_mesh.sweep(samples,radii,props.radial_segments,True,props.cave_flat_floor,props.cap_start,props.cap_end);vertices=data["vertices"];faces=data["faces"];uvs=data["uvs"]
    elif kind=="PATH":
        vertices=[];faces=[]
        river=props.type=="RIVER"
        for sample in samples:
            tangent=Vector(sample["tangent"]);side=tangent.cross(Vector((0,0,1)))
            if side.length<1e-6:side=Vector((1,0,0))
            side.normalize();center=Vector(sample["position"])+Vector((0,0,props.z_offset));width=props.mesh_radius+props.path_shoulder
            if river:
                bottom=props.mesh_radius*props.river_bottom_ratio;outer=props.mesh_radius+props.river_bank_width;down=Vector((0,0,props.river_depth));vertices.extend([center-side*outer,center-side*props.mesh_radius-down*.35,center-side*bottom-down,center+side*bottom-down,center+side*props.mesh_radius-down*.35,center+side*outer])
            else:vertices.extend([center-side*width,center+side*width])
        stride=6 if river else 2
        faces=[(i*stride+column,i*stride+column+1,(i+1)*stride+column+1,(i+1)*stride+column) for i in range(len(samples)-1) for column in range(stride-1)]
        uvs=[(column/(stride-1),sample["normalized_distance"]) for sample in samples for column in range(stride)]
    else:
        vertices=[];faces=[]
        for sample_index,sample in enumerate(samples):
            top=Vector(sample["position"]);tangent=Vector(sample["tangent"]);side=tangent.cross(Vector((0,0,1)))
            if side.length<1e-6:side=Vector((1,0,0))
            side.normalize();side*=(-1 if props.flip_side else 1)
            for row in range(props.cliff_vertical_segments+1):
                t=row/props.cliff_vertical_segments;offset=props.cliff_overhang*math.sin(math.pi*t)+props.cliff_roughness*math.sin(sample_index*1.371+row*2.173)*math.sin(math.pi*t);vertices.append(top-Vector((0,0,props.cliff_height*t))+side*offset)
        stride=props.cliff_vertical_segments+1
        for i in range(len(samples)-1):
            for row in range(props.cliff_vertical_segments):
                face=(i*stride+row,(i+1)*stride+row,(i+1)*stride+row+1,i*stride+row+1);faces.append(tuple(reversed(face)) if props.flip_side else face)
        uvs=[(sample["normalized_distance"],row/props.cliff_vertical_segments) for sample in samples for row in range(props.cliff_vertical_segments+1)]
    mesh=bpy.data.meshes.new(f"WB_{kind}_{props.stable_id[:8]}");mesh.from_pydata(vertices,[],faces);mesh.update();obj=bpy.data.objects.new(mesh.name,mesh);_generated_collection(scene).objects.link(obj)
    if uvs:
        layer=mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:layer.data[loop.index].uv=uvs[loop.vertex_index]
    obj["wb_source_spline_id"]=props.stable_id;obj["wb_generated_kind"]=kind;obj["wb_spline_schema_version"]=1;obj["wb_chunk_split_supported"]=False
    if hasattr(obj,"worldbuilder_chunk"):obj.worldbuilder_chunk.role="GEOMETRY";obj.worldbuilder_chunk.stable_id=f"{props.stable_id}_{kind.lower()}"
    if kind=="CAVE":
        _remove_generated(props.stable_id,"CAVE_PORTAL")
        for label,sample in (("START",samples[0]),("END",samples[-1])):
            portal=bpy.data.objects.new(f"WB_CAVE_PORTAL_{label}_{props.stable_id[:8]}",None);_generated_collection(scene).objects.link(portal);portal.location=sample["position"];portal["wb_source_spline_id"]=props.stable_id;portal["wb_generated_kind"]="CAVE_PORTAL";portal["wb_portal_end"]=label
            if hasattr(portal,"worldbuilder_chunk"):portal.worldbuilder_chunk.role="MARKER";portal.worldbuilder_chunk.marker_type="CavePortal";portal.worldbuilder_chunk.stable_id=f"{props.stable_id}_portal_{label.lower()}"
    return _split_generated_mesh(scene,obj)[0] if props.chunk_split else obj

class WB_UL_spline_modifiers(UIList):
    def draw_item(self,_c,layout,_d,item,_i,_ad,_ap,_ix):layout.prop(item,"enabled",text="");layout.prop(item,"type",text="")
class WB_OT_spline_create(Operator):
    bl_idname="worldbuilder.spline_create";bl_label="Create Spline";bl_options={"UNDO"};type:EnumProperty(items=((v,v.title(),"") for v in spline_contract.SPLINE_TYPES),default="PATH")
    def execute(self,context):
        curve=bpy.data.curves.new(f"WB_{self.type}","CURVE");curve.dimensions="3D";spline=curve.splines.new("BEZIER");spline.bezier_points.add(1);spline.bezier_points[0].co=(-2,0,0);spline.bezier_points[1].co=(2,0,0)
        for p in spline.bezier_points:p.handle_left_type="AUTO";p.handle_right_type="AUTO"
        obj=bpy.data.objects.new(curve.name,curve);context.collection.objects.link(obj);obj.worldbuilder_spline.enabled=True;obj.worldbuilder_spline.schema_version=1;obj.worldbuilder_spline.stable_id=_uuid();obj.worldbuilder_spline.type=self.type;context.view_layer.objects.active=obj;obj.select_set(True);return {"FINISHED"}
class WB_OT_spline_add_modifier(Operator):
    bl_idname="worldbuilder.spline_add_modifier";bl_label="Add Terrain Modifier";bl_options={"UNDO"};type:EnumProperty(items=(("TERRAIN_CARVE","Carve",""),("TERRAIN_RAISE","Raise","")),default="TERRAIN_CARVE")
    def execute(self,context):
        props=_props(context.object);item=props.modifiers.add();item.stable_id=_uuid();item.type=self.type;props.active_modifier=len(props.modifiers)-1;return {"FINISHED"}
class WB_OT_spline_rebuild(Operator):
    bl_idname="worldbuilder.spline_rebuild";bl_label="Rebuild Terrain";bl_options={"UNDO"}
    def execute(self,context):
        try:count=rebuild_carves(context.scene,context.object)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Rebuilt {count} terrain target(s) from basis");return {"FINISHED"}
class WB_OT_spline_restore_basis(Operator):
    bl_idname="worldbuilder.spline_restore_basis";bl_label="Restore Terrain Basis";bl_options={"UNDO"}
    def execute(self,context):
        count=0
        for modifier in _props(context.object).modifiers:
            target=modifier.target
            if target and target.get("wb_terrain_basis_mesh"):
                basis=bpy.data.meshes.get(target["wb_terrain_basis_mesh"])
                if basis:
                    for a,b in zip(target.data.vertices,basis.vertices):a.co=b.co
                    target.data.update();count+=1
        self.report({"INFO"},f"Restored {count} terrain(s)");return {"FINISHED"}
class WB_OT_spline_generate_mesh(Operator):
    bl_idname="worldbuilder.spline_generate_mesh";bl_label="Generate Spline Mesh";bl_options={"UNDO"};kind:EnumProperty(items=(("CAVE","Cave",""),("CLIFF","Cliff",""),("PATH","Path / Riverbed","")),default="PATH")
    def execute(self,context):
        try:obj=generate_mesh(context.scene,context.object,self.kind)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Generated {obj.name} with deterministic chunk clipping");return {"FINISHED"}

class WB_PT_splines(Panel):
    bl_label="Splines";bl_idname="WB_PT_splines";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;row=layout.row(align=True)
        for kind in ("PATH","RIVER","CAVE"):op=row.operator("worldbuilder.spline_create",text=kind.title());op.type=kind
        obj=context.object;props=_props(obj) if obj else None
        if not props or not props.enabled:layout.label(text="Select a WorldBuilder Curve");return
        layout.prop(props,"type");layout.prop(props,"sample_spacing");layout.template_list("WB_UL_spline_modifiers","",props,"modifiers",props,"active_modifier",rows=3);row=layout.row(align=True);op=row.operator("worldbuilder.spline_add_modifier",text="Carve");op.type="TERRAIN_CARVE";op=row.operator("worldbuilder.spline_add_modifier",text="Raise");op.type="TERRAIN_RAISE"
        if props.modifiers:
            item=props.modifiers[props.active_modifier];layout.prop(item,"target");layout.prop(item,"width");layout.prop(item,"depth");layout.prop(item,"falloff_distance");layout.prop(item,"profile");layout.prop(item,"preserve_boundary")
        row=layout.row(align=True);row.operator("worldbuilder.spline_rebuild");row.operator("worldbuilder.spline_restore_basis")
        layout.prop(props,"chunk_split");layout.prop(props,"mesh_radius");layout.prop(props,"radial_segments");row=layout.row(align=True);row.prop(props,"cap_start");row.prop(props,"cap_end");layout.prop(props,"taper");layout.prop(props,"entrance_flare");layout.prop(props,"cave_flat_floor");layout.prop(props,"cave_roughness");layout.prop(props,"cliff_height");layout.prop(props,"cliff_vertical_segments");layout.prop(props,"cliff_overhang");layout.prop(props,"cliff_roughness");layout.prop(props,"flip_side");layout.prop(props,"path_shoulder");layout.prop(props,"river_depth");layout.prop(props,"river_bottom_ratio");layout.prop(props,"river_bank_width");layout.prop(props,"z_offset");row=layout.row(align=True)
        for kind in ("PATH","CLIFF","CAVE"):op=row.operator("worldbuilder.spline_generate_mesh",text=kind.title());op.kind=kind
        layout.label(text="Chunk split interpolates position, UV, normals, and materials",icon="INFO")

CLASSES=(WBSplineModifier,WBSplineProperties,WB_UL_spline_modifiers,WB_OT_spline_create,WB_OT_spline_add_modifier,WB_OT_spline_rebuild,WB_OT_spline_restore_basis,WB_OT_spline_generate_mesh,WB_PT_splines)
def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Object.worldbuilder_spline=PointerProperty(type=WBSplineProperties)
def unregister():
    if hasattr(bpy.types.Object,"worldbuilder_spline"):del bpy.types.Object.worldbuilder_spline
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
