"""Object-mode, cached 3D biome painting tool."""

from __future__ import annotations

import math

import bpy
import gpu
from bpy.props import BoolProperty, EnumProperty, FloatProperty
from bpy.types import Operator, WorkSpaceTool
from bpy_extras import view3d_utils
from gpu_extras.batch import batch_for_shader
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from . import biome, biome_brush, contract, exporter, overlay, state

_preview_cache={"batch":None,"shader":None};_preview_handler=None
def _rebuild_preview(scene,obj=None):
    _preview_cache.update(batch=None,shader=None);settings=getattr(scene,"worldbuilder_biome_brush",None);obj=obj or bpy.context.object
    if settings is None or settings.preview_mode=="OFF" or not biome.is_biome_target(obj):return
    definitions=[definition for definition in biome.biome_definitions(scene) if definition.enabled]
    if not definitions:return
    active_settings=scene.worldbuilder_biomes;active=definitions[0]
    if active_settings.layers and active_settings.active_index<len(active_settings.layers):active=active_settings.layers[active_settings.active_index]
    positions=[];colors=[];step=max(1,math.ceil(len(obj.data.vertices)/50000))
    for vertex_index in range(0,len(obj.data.vertices),step):
        vertex=obj.data.vertices[vertex_index]
        positions.append(obj.matrix_world@vertex.co)
        if settings.preview_mode=="ACTIVE":
            attribute=obj.data.attributes.get(active.attribute_name);weight=attribute.data[vertex.index].value if attribute else 0;color=active.color;colors.append((color[0]*weight,color[1]*weight,color[2]*weight,max(.05,weight)))
        else:
            weighted=[]
            for definition in definitions:
                attribute=obj.data.attributes.get(definition.attribute_name);weighted.append((attribute.data[vertex.index].value if attribute else 0,definition))
            weight,definition=max(weighted,key=lambda item:item[0]);color=definition.color;colors.append((color[0],color[1],color[2],max(.05,weight)))
    try:
        shader=gpu.shader.from_builtin("SMOOTH_COLOR");_preview_cache["shader"]=shader;_preview_cache["batch"]=batch_for_shader(shader,"POINTS",{"pos":positions,"color":colors})
    except SystemError:
        # Blender background mode intentionally has no draw-capable GPU context.
        _preview_cache.update(batch=None,shader=None)
def _preview_changed(_self,context):
    if context:_rebuild_preview(context.scene,context.object)
def _draw_preview():
    batch=_preview_cache["batch"];shader=_preview_cache["shader"]
    if batch and shader:
        gpu.state.blend_set("ALPHA");gpu.state.point_size_set(5);batch.draw(shader);gpu.state.point_size_set(1);gpu.state.blend_set("NONE")


class WBBiomeBrushSettings(bpy.types.PropertyGroup):
    radius: FloatProperty(name="Radius", default=5.0, min=0.01, unit="LENGTH")
    strength: FloatProperty(name="Strength", default=0.35, min=0.0, max=1.0)
    falloff: EnumProperty(name="Falloff", items=((v, v.title(), "") for v in ("LINEAR", "SMOOTH", "SHARP", "CONSTANT")), default="SMOOTH")
    mode: EnumProperty(name="Mode", items=((v, v.title(), "") for v in ("ADD", "REPLACE", "ERASE", "SMOOTH")), default="ADD")
    auto_normalize: BoolProperty(name="Auto Normalize", default=True)
    affect_hidden: BoolProperty(name="Affect Hidden", default=False)
    front_faces_only: BoolProperty(name="Front Faces Only", default=True)
    symmetry_x: BoolProperty(name="Symmetry X", default=False)
    sample_spacing: FloatProperty(name="Sample Spacing", default=0.15, min=0.01, max=1.0)
    preview_mode: EnumProperty(name="Preview",items=(("OFF","Off",""),("ACTIVE","Active Layer",""),("DOMINANT","Dominant Biome","")),default="OFF",update=_preview_changed)
    changed_vertices: bpy.props.IntProperty(name="Changed Vertices", default=0)


class _TargetCache:
    def __init__(self, obj, depsgraph):
        self.obj = obj
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            if len(mesh.vertices) != len(obj.data.vertices):
                raise ValueError(f"{obj.name}: topology-changing modifiers are unsupported while painting")
            mesh.calc_loop_triangles()
            self.points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
            triangles = [tuple(item.vertices) for item in mesh.loop_triangles]
            self.bvh = BVHTree.FromPolygons(self.points, triangles, all_triangles=True)
        finally:
            evaluated.to_mesh_clear()
        self.kdtree = KDTree(len(self.points))
        for index, point in enumerate(self.points): self.kdtree.insert(point, index)
        self.kdtree.balance()
        self.neighbors = [set() for _ in self.points]
        for edge in obj.data.edges:
            a, b = edge.vertices
            self.neighbors[a].add(b); self.neighbors[b].add(a)


_draw_state = {"location": None, "normal": None, "radius": 1.0, "color": (0.2,0.7,1.0,1.0), "valid": False}


def _draw_brush():
    center = _draw_state["location"]
    if center is None: return
    normal = Vector(_draw_state["normal"] or (0,0,1)).normalized()
    side = normal.cross(Vector((0,0,1)))
    if side.length_squared < 1e-8: side = Vector((1,0,0))
    side.normalize(); up = normal.cross(side).normalized()
    radius = _draw_state["radius"]
    points = [center + radius*(math.cos(math.tau*i/48)*side + math.sin(math.tau*i/48)*up) for i in range(49)]
    shader = gpu.shader.from_builtin("POLYLINE_UNIFORM_COLOR")
    batch = batch_for_shader(shader, "LINE_STRIP", {"pos": points})
    shader.bind(); shader.uniform_float("color", _draw_state["color"] if _draw_state["valid"] else (1,0.1,0.1,1)); shader.uniform_float("lineWidth", 2.0); shader.uniform_float("viewportSize", gpu.state.viewport_get()[2:])
    batch.draw(shader)


class WB_OT_biome_paint(Operator):
    bl_idname = "worldbuilder.biome_paint"
    bl_label = "WorldBuilder Biome Paint"
    bl_options = {"REGISTER", "UNDO", "BLOCKING"}

    _targets = None; _original = None; _changed = None; _painting = False; _handler = None; _last_hit = None

    def _ray(self, context, event):
        region, rv3d = context.region, context.region_data
        coordinate = (event.mouse_region_x, event.mouse_region_y)
        origin = view3d_utils.region_2d_to_origin_3d(region, rv3d, coordinate)
        direction = view3d_utils.region_2d_to_vector_3d(region, rv3d, coordinate).normalized()
        best = None
        for cache in self._targets:
            inverse = cache.obj.matrix_world.inverted_safe()
            local_origin = inverse @ origin
            local_direction = (inverse.to_3x3() @ direction).normalized()
            hit, normal, face, distance = cache.bvh.ray_cast(origin, direction, 1.0e9)
            if hit is not None and (best is None or distance < best[0]): best = (distance, cache, hit, normal)
        return best

    def _definitions(self, context):
        settings = context.scene.worldbuilder_biomes
        enabled = [item for item in settings.layers if item.enabled]
        active = settings.layers[settings.active_index] if settings.layers and settings.active_index < len(settings.layers) else None
        return enabled, active

    def _apply_center(self, context, cache, location, normal, event):
        brush = context.scene.worldbuilder_biome_brush
        enabled, active = self._definitions(context)
        if active is None: return
        mode = "SMOOTH" if event.shift else "ERASE" if event.ctrl else brush.mode
        attributes = [biome.ensure_biome_attribute(cache.obj.data, definition) for definition in enabled]
        try: active_index = enabled.index(active)
        except ValueError: return
        for _, vertex_index, distance in cache.kdtree.find_range(location, brush.radius):
            if brush.front_faces_only and normal.dot((location-cache.points[vertex_index]).normalized()) < -0.05: continue
            influence = biome_brush.brush_influence(distance, brush.radius, brush.strength, brush.falloff)
            if influence <= 0: continue
            key=(cache.obj.as_pointer(),vertex_index)
            if key not in self._original: self._original[key]=[float(attribute.data[vertex_index].value) for attribute in attributes]
            old=float(attributes[active_index].data[vertex_index].value)
            if mode == "ERASE": value=biome_brush.erase(old,1.0,influence)
            elif mode == "SMOOTH": value=biome_brush.smooth(old,[attributes[active_index].data[n].value for n in cache.neighbors[vertex_index]],1.0,influence)
            else: value=biome_brush.paint(old,1.0,influence,mode)
            values=[float(attribute.data[vertex_index].value) for attribute in attributes]
            values[active_index]=value
            if brush.auto_normalize: values=biome_brush.auto_normalize(values,active_index,value)
            for attribute, weight in zip(attributes,values): attribute.data[vertex_index].value=weight
            self._changed.add(key)
        cache.obj.data.update(); biome.invalidate_biome_cache(cache.obj)
        brush.changed_vertices=len(self._changed)

    def _apply(self, context, hit, event):
        _,cache,location,normal=hit;brush=context.scene.worldbuilder_biome_brush
        if self._last_hit is not None and (location-self._last_hit).length<brush.radius*brush.sample_spacing:return
        self._last_hit=location.copy();self._apply_center(context,cache,location,normal,event)
        if brush.symmetry_x:
            local=cache.obj.matrix_world.inverted_safe()@location;local.x=-local.x;mirrored=cache.obj.matrix_world@local
            local_normal=cache.obj.matrix_world.inverted_safe().to_3x3()@normal;local_normal.x=-local_normal.x;mirrored_normal=(cache.obj.matrix_world.to_3x3()@local_normal).normalized();self._apply_center(context,cache,mirrored,mirrored_normal,event)

    def invoke(self, context, event):
        if context.area.type != "VIEW_3D" or context.mode != "OBJECT": return {"CANCELLED"}
        depsgraph=context.evaluated_depsgraph_get(); self._targets=[]
        try:
            affect_hidden=context.scene.worldbuilder_biome_brush.affect_hidden
            for obj in context.scene.objects:
                if biome.is_biome_target(obj) and (affect_hidden or not obj.hide_viewport): self._targets.append(_TargetCache(obj,depsgraph))
        except ValueError as error:
            self.report({"ERROR"},str(error)); return {"CANCELLED"}
        if not self._targets: self.report({"ERROR"},"No visible Biome Target"); return {"CANCELLED"}
        self._original={}; self._changed=set();self._last_hit=None; self._handler=bpy.types.SpaceView3D.draw_handler_add(_draw_brush,(),"WINDOW","POST_VIEW")
        hit=self._ray(context,event)
        if event.alt and hit:
            values=biome.sample_all_biomes_world(hit[1].obj,hit[2])
            if values:
                winner=max(values,key=values.get); settings=context.scene.worldbuilder_biomes
                settings.active_index=next((i for i,v in enumerate(settings.layers) if v.stable_id==winner),settings.active_index)
            self._finish(context,False); return {"FINISHED"}
        self._painting=event.type=="LEFTMOUSE"
        if self._painting and hit:self._apply(context,hit,event)
        context.window_manager.modal_handler_add(self); return {"RUNNING_MODAL"}

    def _finish(self, context, cancel=False):
        if cancel:
            enabled,_=self._definitions(context)
            by_pointer={cache.obj.as_pointer():cache.obj for cache in self._targets}
            for (pointer,index),values in self._original.items():
                obj=by_pointer[pointer]
                for definition,value in zip(enabled,values): biome.ensure_biome_attribute(obj.data,definition).data[index].value=value
                obj.data.update()
        else:
            grid=context.scene.worldbuilder_chunks
            for cache in self._targets:
                if any(key[0]==cache.obj.as_pointer() for key in self._changed):
                    bounds=exporter.world_bounds(cache.obj)
                    if bounds:
                        minimum,maximum=bounds
                        min_coord=contract.chunk_coord_from_xy(minimum.x,minimum.y,grid.origin_x,grid.origin_y,grid.chunk_size)
                        max_coord=contract.chunk_coord_from_xy(maximum.x-1e-7,maximum.y-1e-7,grid.origin_x,grid.origin_y,grid.chunk_size)
                        state.mark_dirty(grid,*((x,z) for x in range(min_coord[0],max_coord[0]+1) for z in range(min_coord[1],max_coord[1]+1)))
            _rebuild_preview(context.scene)
        if self._handler is not None: bpy.types.SpaceView3D.draw_handler_remove(self._handler,"WINDOW"); self._handler=None
        _draw_state["location"]=None; overlay.invalidate_all()

    def modal(self, context, event):
        if event.type in {"ESC","RIGHTMOUSE"}: self._finish(context,True); return {"CANCELLED"}
        if event.type in {"MIDDLEMOUSE","WHEELUPMOUSE","WHEELDOWNMOUSE"}: return {"PASS_THROUGH"}
        hit=self._ray(context,event)
        brush=context.scene.worldbuilder_biome_brush
        if hit:
            _,_,location,normal=hit; _draw_state.update(location=location,normal=normal,radius=brush.radius,valid=True)
            if event.alt and event.type=="LEFTMOUSE" and event.value=="PRESS":
                values=biome.sample_all_biomes_world(hit[1].obj,location)
                if values:
                    winner=max(values,key=values.get); settings=context.scene.worldbuilder_biomes
                    settings.active_index=next((i for i,v in enumerate(settings.layers) if v.stable_id==winner),settings.active_index)
                return {"RUNNING_MODAL"}
        else: _draw_state["valid"]=False
        if event.type=="LEFTMOUSE":
            self._painting=event.value!= "RELEASE"
            if self._painting and hit: self._apply(context,hit,event)
            if event.value=="RELEASE": self._finish(context,False); return {"FINISHED"}
        elif event.type=="MOUSEMOVE" and self._painting and hit: self._apply(context,hit,event)
        context.area.tag_redraw(); return {"RUNNING_MODAL"}


class WB_WST_biome_paint(WorkSpaceTool):
    bl_space_type="VIEW_3D"; bl_context_mode="OBJECT"; bl_idname="worldbuilder.biome_paint_tool"; bl_label="Biome Paint"; bl_description="Paint WorldBuilder biome weights"; bl_icon="ops.sculpt.paint"; bl_widget=None
    bl_keymap=(("worldbuilder.biome_paint", {"type":"LEFTMOUSE","value":"PRESS"}, None),("worldbuilder.biome_adjust_brush", {"type":"F","value":"PRESS"}, {"properties":[("kind","RADIUS")]}),("worldbuilder.biome_adjust_brush", {"type":"F","value":"PRESS","shift":True}, {"properties":[("kind","STRENGTH")]}))

class WB_OT_biome_adjust_brush(Operator):
    bl_idname="worldbuilder.biome_adjust_brush";bl_label="Adjust Biome Brush";bl_options={"BLOCKING"}
    kind:EnumProperty(items=(("RADIUS","Radius",""),("STRENGTH","Strength","")),default="RADIUS")
    _start_mouse=0;_start_value=0.0
    def invoke(self,context,event):
        value=context.scene.worldbuilder_biome_brush;self._start_mouse=event.mouse_x;self._start_value=value.radius if self.kind=="RADIUS" else value.strength;context.window_manager.modal_handler_add(self);return {"RUNNING_MODAL"}
    def modal(self,context,event):
        value=context.scene.worldbuilder_biome_brush
        if event.type=="MOUSEMOVE":
            delta=event.mouse_x-self._start_mouse
            if self.kind=="RADIUS":value.radius=max(.01,self._start_value*math.exp(delta*.01))
            else:value.strength=max(0,min(1,self._start_value+delta*.005))
            context.area.tag_redraw();return {"RUNNING_MODAL"}
        if event.type in {"LEFTMOUSE","RET","NUMPAD_ENTER"}:return {"FINISHED"}
        if event.type in {"RIGHTMOUSE","ESC"}:
            if self.kind=="RADIUS":value.radius=self._start_value
            else:value.strength=self._start_value
            return {"CANCELLED"}
        return {"RUNNING_MODAL"}


_classes=(WBBiomeBrushSettings,WB_OT_biome_paint,WB_OT_biome_adjust_brush)

def register():
    global _preview_handler
    for cls in _classes: bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_biome_brush=bpy.props.PointerProperty(type=WBBiomeBrushSettings)
    bpy.utils.register_tool(WB_WST_biome_paint,after={"builtin.select_box"},separator=True,group=True)
    _preview_handler=bpy.types.SpaceView3D.draw_handler_add(_draw_preview,(),"WINDOW","POST_VIEW")

def unregister():
    global _preview_handler
    if _preview_handler:bpy.types.SpaceView3D.draw_handler_remove(_preview_handler,"WINDOW");_preview_handler=None
    try: bpy.utils.unregister_tool(WB_WST_biome_paint)
    except Exception: pass
    if hasattr(bpy.types.Scene,"worldbuilder_biome_brush"): del bpy.types.Scene.worldbuilder_biome_brush
    for cls in reversed(_classes): bpy.utils.unregister_class(cls)
