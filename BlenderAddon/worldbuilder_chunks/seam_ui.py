"""Explicit chunk seam validation, cached overlay, and safe equal-resolution stitching."""

import bpy
import gpu
from bpy.props import CollectionProperty, EnumProperty, FloatProperty, IntProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from gpu_extras.batch import batch_for_shader

from . import contract, exporter, overlay, seam, state

_overlay_lines=[];_handle=None

class WBSeamResult(PropertyGroup):
    object_a:StringProperty();object_b:StringProperty();chunk_a:StringProperty();chunk_b:StringProperty();edge:StringProperty();status:StringProperty();maximum:FloatProperty();average:FloatProperty();normal_maximum:FloatProperty();uv_maximum:FloatProperty();affected:IntProperty()
class WBSeamSettings(PropertyGroup):
    results:CollectionProperty(type=WBSeamResult);active_index:IntProperty(default=0);position_tolerance:FloatProperty(name="Position",default=.001,min=0);xy_tolerance:FloatProperty(name="XY Match",default=.001,min=0);normal_tolerance:FloatProperty(name="Normal Degrees",default=3,min=0,max=180);uv_tolerance:FloatProperty(name="UV",default=.001,min=0);scope:EnumProperty(name="Scope",items=(("ACTIVE","Active Chunk",""),("DIRTY","Dirty Chunks",""),("ALL","All Chunks","")),default="ALL");stitch_mode:EnumProperty(name="Stitch",items=(("AVERAGE","Average Both",""),("A_TO_B","Snap A to B",""),("B_TO_A","Snap B to A","")),default="AVERAGE")

def _terrain(obj):return obj.type=="MESH" and bool(obj.get("wb_terrain") or obj.get("wb_biome_target") or obj.get("nex_stylized_terrain_kind")=="TERRAIN")
def _edge_points(obj,coord,edge,grid,tolerance):
    minimum_x,minimum_y,maximum_x,maximum_y=contract.chunk_bounds_xy(coord,grid.origin_x,grid.origin_y,grid.chunk_size)
    boundary={"WEST":minimum_x,"EAST":maximum_x,"SOUTH":minimum_y,"NORTH":maximum_y}[edge];axis=0 if edge in {"WEST","EAST"} else 1;result=[]
    for vertex in obj.data.vertices:
        world=obj.matrix_world@vertex.co
        if abs(world[axis]-boundary)<=tolerance:result.append((vertex.index,tuple(world)))
    return result
def _vertex_uv(obj,index):
    layer=obj.data.uv_layers.active
    if layer is None:return None
    for loop in obj.data.loops:
        if loop.vertex_index==index:return tuple(layer.data[loop.index].uv)
    return None
def _comparison_metrics(a,b,left,right,pairs):
    normal_values=[];uv_values=[]
    for ai,bi in pairs:
        ia,ib=left[ai][0],right[bi][0];na=(a.matrix_world.to_3x3()@a.data.vertices[ia].normal).normalized();nb=(b.matrix_world.to_3x3()@b.data.vertices[ib].normal).normalized();normal_values.append(na.angle(nb)*57.295779513)
        ua,ub=_vertex_uv(a,ia),_vertex_uv(b,ib)
        if ua is not None and ub is not None:uv_values.append(((ua[0]-ub[0])**2+(ua[1]-ub[1])**2)**.5)
    return max(normal_values,default=0),max(uv_values,default=0)
def validate(scene):
    global _overlay_lines
    settings=scene.worldbuilder_seams;settings.results.clear();_overlay_lines=[];grid=scene.worldbuilder_chunks;mapping=exporter.chunk_collection_map(scene);chunks={}
    for obj in scene.objects:
        if _terrain(obj):chunks.setdefault(exporter.object_chunk(obj,grid,mapping),[]).append(obj)
    scope=set(chunks)
    if settings.scope=="ACTIVE":scope={state.explicit_active_chunk(grid) or (0,0)}
    elif settings.scope=="DIRTY":scope=state.dirty_chunks(grid)
    for coord in sorted(scope):
        objects=chunks.get(coord,[])
        if len(objects)>1:
            item=settings.results.add();item.object_a=objects[0].name;item.chunk_a=contract.chunk_name(coord);item.status="MULTIPLE_TERRAIN";item.affected=len(objects)
        for edge in ("EAST","NORTH"):
            other_coord=seam.neighbor(coord,edge);others=chunks.get(other_coord,[])
            if not objects:continue
            if not others:
                item=settings.results.add();item.object_a=objects[0].name;item.chunk_a=contract.chunk_name(coord);item.chunk_b=contract.chunk_name(other_coord);item.edge=edge;item.status="MISSING_NEIGHBOR";continue
            a,b=objects[0],others[0];left=_edge_points(a,coord,edge,grid,settings.xy_tolerance);opposite="WEST" if edge=="EAST" else "SOUTH";right=_edge_points(b,other_coord,opposite,grid,settings.xy_tolerance)
            pairs,status=seam.match_edges([p for _,p in left],[p for _,p in right],edge,settings.xy_tolerance);errors=seam.position_errors([p for _,p in left],[p for _,p in right],pairs);normal_max,uv_max=_comparison_metrics(a,b,left,right,pairs);materials_a=tuple(slot.material.name if slot.material else "" for slot in a.material_slots);materials_b=tuple(slot.material.name if slot.material else "" for slot in b.material_slots);resolved=status
            if status=="OK":
                if errors["maximum"]>settings.position_tolerance:resolved="POSITION_SEAM"
                elif normal_max>settings.normal_tolerance:resolved="NORMAL_SEAM"
                elif uv_max>settings.uv_tolerance:resolved="UV_SEAM"
                elif materials_a!=materials_b:resolved="MATERIAL_SEAM"
            item=settings.results.add();item.object_a=a.name;item.object_b=b.name;item.chunk_a=contract.chunk_name(coord);item.chunk_b=contract.chunk_name(other_coord);item.edge=edge;item.status=resolved;item.maximum=errors["maximum"];item.average=errors["average"];item.normal_maximum=normal_max;item.uv_maximum=uv_max;item.affected=errors["affected"]
            color=(.2,1,.2,1) if item.status=="OK" else (1,.1,.1,1) if item.status=="POSITION_SEAM" else (1,0,1,1)
            for ai,bi in pairs:
                if item.status!="OK":_overlay_lines.append((left[ai][1],right[bi][1],color))
    overlay.invalidate_all();return len(settings.results)

def _draw():
    if not _overlay_lines:return
    shader=gpu.shader.from_builtin("POLYLINE_UNIFORM_COLOR")
    for a,b,color in _overlay_lines:
        batch=batch_for_shader(shader,"LINES",{"pos":(a,b)});shader.bind();shader.uniform_float("color",color);shader.uniform_float("lineWidth",3);shader.uniform_float("viewportSize",gpu.state.viewport_get()[2:]);batch.draw(shader)

class WB_UL_seams(UIList):
    def draw_item(self,_c,layout,_d,item,_i,_ad,_ap,_ix):layout.label(text=f"{item.chunk_a} {item.edge}");layout.label(text=item.status);layout.label(text=f"{item.maximum:.4f}")
class WB_OT_seam_validate(Operator):
    bl_idname="worldbuilder.seam_validate";bl_label="Validate Seams"
    def execute(self,context):self.report({"INFO"},f"Validated {validate(context.scene)} seam pair(s)");return {"FINISHED"}
class WB_OT_seam_focus(Operator):
    bl_idname="worldbuilder.seam_focus";bl_label="Focus Seam"
    def execute(self,context):
        value=context.scene.worldbuilder_seams
        if not value.results:return {"CANCELLED"}
        item=value.results[value.active_index];a=bpy.data.objects.get(item.object_a);b=bpy.data.objects.get(item.object_b);bpy.ops.object.select_all(action="DESELECT")
        for obj in (a,b):
            if obj:obj.select_set(True)
        if a:context.view_layer.objects.active=a
        return {"FINISHED"}
class WB_OT_seam_stitch(Operator):
    bl_idname="worldbuilder.seam_stitch";bl_label="Apply Safe Stitch";bl_options={"UNDO"}
    def execute(self,context):
        settings=context.scene.worldbuilder_seams
        if not settings.results:return {"CANCELLED"}
        item=settings.results[settings.active_index];a=bpy.data.objects.get(item.object_a);b=bpy.data.objects.get(item.object_b)
        if not a or not b:return {"CANCELLED"}
        grid=context.scene.worldbuilder_chunks;coord=contract.parse_chunk_name(item.chunk_a);other=contract.parse_chunk_name(item.chunk_b);left=_edge_points(a,coord,item.edge,grid,settings.xy_tolerance);opposite="WEST" if item.edge=="EAST" else "SOUTH";right=_edge_points(b,other,opposite,grid,settings.xy_tolerance);pairs,status=seam.match_edges([p for _,p in left],[p for _,p in right],item.edge,settings.xy_tolerance)
        if status!="OK":self.report({"ERROR"},"Unsafe stitch blocked: edge topology differs");return {"CANCELLED"}
        points_a=[p for _,p in left];points_b=[p for _,p in right];new_a,new_b=seam.stitched_positions(points_a,points_b,pairs,settings.stitch_mode);inv_a=a.matrix_world.inverted_safe();inv_b=b.matrix_world.inverted_safe()
        for (index,_),value in zip(left,new_a):a.data.vertices[index].co=inv_a@__import__('mathutils').Vector(value)
        for (index,_),value in zip(right,new_b):b.data.vertices[index].co=inv_b@__import__('mathutils').Vector(value)
        a.data.update();b.data.update();state.mark_dirty(grid,coord,other);validate(context.scene);return {"FINISHED"}

class WB_PT_seams(Panel):
    bl_label="Seams";bl_idname="WB_PT_seams";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;settings=context.scene.worldbuilder_seams;layout.prop(settings,"scope");row=layout.row(align=True);row.prop(settings,"position_tolerance");row.prop(settings,"xy_tolerance");row=layout.row(align=True);row.prop(settings,"normal_tolerance");row.prop(settings,"uv_tolerance");layout.operator("worldbuilder.seam_validate",icon="CHECKMARK");layout.template_list("WB_UL_seams","",settings,"results",settings,"active_index",rows=4);row=layout.row(align=True);row.operator("worldbuilder.seam_focus");row.prop(settings,"stitch_mode",text="");row.operator("worldbuilder.seam_stitch")

CLASSES=(WBSeamResult,WBSeamSettings,WB_UL_seams,WB_OT_seam_validate,WB_OT_seam_focus,WB_OT_seam_stitch,WB_PT_seams)
def register():
    global _handle
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_seams=bpy.props.PointerProperty(type=WBSeamSettings);_handle=bpy.types.SpaceView3D.draw_handler_add(_draw,(),"WINDOW","POST_VIEW")
def unregister():
    global _handle
    if _handle:bpy.types.SpaceView3D.draw_handler_remove(_handle,"WINDOW");_handle=None
    if hasattr(bpy.types.Scene,"worldbuilder_seams"):del bpy.types.Scene.worldbuilder_seams
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
