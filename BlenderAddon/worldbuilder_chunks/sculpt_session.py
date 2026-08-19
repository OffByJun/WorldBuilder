"""Temporary welded multi-chunk Sculpt proxy with seam-safe commit."""
import bpy
from bpy.props import BoolProperty,EnumProperty,IntProperty,PointerProperty
from bpy.types import Operator,Panel,PropertyGroup
from mathutils import Vector
from . import biome,chunk_terrain,localization,state

_session=None

class WBSculptSessionSettings(PropertyGroup):
    scope:EnumProperty(name="Scope",items=(("ACTIVE_NEIGHBORS","Active Chunk + Neighbors",""),("SELECTED","Selected Chunks","")),default="ACTIVE_NEIGHBORS")
    neighbor_ring:IntProperty(name="Neighbor Ring",default=1,min=0,max=3);lock_outer_boundary:BoolProperty(name="Lock Outer Boundary",default=True)

def _coordinates(scene):
    settings=scene.worldbuilder_sculpt;grid=scene.worldbuilder_chunks
    base={state.explicit_active_chunk(grid) or (0,0)} if settings.scope=="ACTIVE_NEIGHBORS" else (state.selected_chunks(grid) or {state.explicit_active_chunk(grid) or (0,0)})
    ring=settings.neighbor_ring
    return {(x+dx,z+dz) for x,z in base for dx in range(-ring,ring+1) for dz in range(-ring,ring+1)}

def _build_proxy(scene,sources,lock_boundary):
    vertices=[];faces=[];lookup={};mapping={};edge_use={}
    def index(point):
        key=tuple(round(v,6) for v in point)
        if key not in lookup:lookup[key]=len(vertices);vertices.append(tuple(point))
        return lookup[key]
    for source in sources:
        values=[]
        for vertex in source.data.vertices:values.append(index(source.matrix_world@vertex.co))
        mapping[source.name]=values
        for polygon in source.data.polygons:
            face=tuple(values[value] for value in polygon.vertices);faces.append(face)
            for a,b in zip(face,face[1:]+face[:1]):edge=tuple(sorted((a,b)));edge_use[edge]=edge_use.get(edge,0)+1
    mesh=bpy.data.meshes.new("WB_SCULPT_SESSION_MESH");mesh.from_pydata(vertices,[],faces);mesh.update()
    if lock_boundary:
        boundary={value for edge,count in edge_use.items() if count==1 for value in edge};attribute=mesh.attributes.new(".sculpt_mask","FLOAT","POINT")
        for i,item in enumerate(attribute.data):item.value=1.0 if i in boundary else 0.0
    obj=bpy.data.objects.new("WB_SCULPT_SESSION",mesh);scene.collection.objects.link(obj);obj["wb_sculpt_session"]=True;return obj,mapping,len(vertices)

def begin(scene):
    global _session
    if _session is not None:raise ValueError("A Sculpt Session is already active")
    _cleanup_stale_sessions()
    sources=[chunk_terrain.terrain_for_coordinate(scene,coord) for coord in sorted(_coordinates(scene))];sources=[obj for obj in sources if obj is not None]
    if not sources:raise ValueError("No authoritative chunk terrain exists in the selected scope")
    cells={obj.get("wb_terrain_cells") for obj in sources}
    if len(cells)!=1:raise ValueError("Sculpt Session requires matching Cells per Chunk")
    proxy,mapping,count=_build_proxy(scene,sources,scene.worldbuilder_sculpt.lock_outer_boundary);visibility={obj.name:obj.hide_viewport for obj in sources}
    for obj in sources:obj.hide_viewport=True
    _session={"proxy":proxy,"sources":sources,"mapping":mapping,"vertex_count":count,"visibility":visibility};return proxy

def _cleanup():
    global _session
    if _session is None:return
    for source in _session["sources"]:
        if source and source.name in bpy.data.objects:source.hide_viewport=_session["visibility"].get(source.name,False)
    proxy=_session["proxy"]
    if proxy and proxy.name in bpy.data.objects:bpy.data.objects.remove(proxy,do_unlink=True)
    _session=None

def apply(scene):
    if _session is None:raise ValueError("No Sculpt Session is active")
    proxy=_session["proxy"]
    if len(proxy.data.vertices)!=_session["vertex_count"]:raise ValueError("Sculpt topology changed. Undo Dyntopo/Remesh before Apply")
    changed=[]
    for source in _session["sources"]:
        if source is None or source.name not in bpy.data.objects:continue
        indices=_session["mapping"][source.name]
        if len(indices)!=len(source.data.vertices):raise ValueError(f"{source.name}: source topology changed during Sculpt Session")
        inverse=source.matrix_world.inverted_safe();basis=bpy.data.meshes.get(source.get("wb_terrain_basis_mesh",""))
        for vertex,proxy_index in zip(source.data.vertices,indices):vertex.co=inverse@(proxy.matrix_world@proxy.data.vertices[proxy_index].co)
        attribute=source.data.attributes.get("wb_sculpt_delta")
        if attribute and (attribute.data_type!="FLOAT_VECTOR" or attribute.domain!="POINT"):source.data.attributes.remove(attribute);attribute=None
        attribute=attribute or source.data.attributes.new("wb_sculpt_delta","FLOAT_VECTOR","POINT")
        for i,item in enumerate(attribute.data):item.vector=source.data.vertices[i].co-(basis.vertices[i].co if basis and i<len(basis.vertices) else source.data.vertices[i].co)
        source["wb_sculpt_revision"]=int(source.get("wb_sculpt_revision",0))+1;source.data.update();biome.invalidate_biome_cache(source);state.mark_dirty(scene.worldbuilder_chunks,(source.get("wb_chunk_x"),source.get("wb_chunk_z")));changed.append(source)
    _cleanup();return changed

class WB_OT_sculpt_begin(Operator):
    bl_idname="worldbuilder.sculpt_session_begin";bl_label="Begin Sculpt Session";bl_options={"UNDO"}
    def execute(self,context):
        try:proxy=begin(context.scene)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        bpy.ops.object.select_all(action="DESELECT");proxy.select_set(True);context.view_layer.objects.active=proxy
        if not bpy.app.background:bpy.ops.object.mode_set(mode="SCULPT")
        self.report({"INFO"},"Sculpt Session started. Dyntopo and Remesh are not supported");return {"FINISHED"}
class WB_OT_sculpt_apply(Operator):
    bl_idname="worldbuilder.sculpt_session_apply";bl_label="Apply Sculpt";bl_options={"UNDO"}
    def execute(self,context):
        if context.object and context.object.mode!="OBJECT":bpy.ops.object.mode_set(mode="OBJECT")
        try:values=apply(context.scene)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Applied Sculpt to {len(values)} terrain chunk(s)");return {"FINISHED"}
class WB_OT_sculpt_cancel(Operator):
    bl_idname="worldbuilder.sculpt_session_cancel";bl_label="Cancel Sculpt"
    def execute(self,context):
        if context.object and context.object.mode!="OBJECT":bpy.ops.object.mode_set(mode="OBJECT")
        _cleanup();return {"FINISHED"}

class WB_PT_sculpt_session(Panel):
    bl_label="Terrain Sculpt";bl_idname="WB_PT_sculpt_session";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_sculpt;t=lambda key:localization.tr(key,context.scene);layout.label(text=t("sculpt"),icon="SCULPTMODE_HLT");layout.prop(s,"scope",text=t("terrain_scope"));layout.prop(s,"neighbor_ring",text=t("neighbor_ring"));layout.prop(s,"lock_outer_boundary",text=t("lock_boundary"));row=layout.row(align=True);row.enabled=_session is None;row.operator("worldbuilder.sculpt_session_begin",text=t("begin_sculpt"));row=layout.row(align=True);row.enabled=_session is not None;row.operator("worldbuilder.sculpt_session_apply",text=t("apply_sculpt"));row.operator("worldbuilder.sculpt_session_cancel",text=t("cancel_sculpt"));layout.label(text=t("sculpt_hint"),icon="INFO")

CLASSES=(WBSculptSessionSettings,WB_OT_sculpt_begin,WB_OT_sculpt_apply,WB_OT_sculpt_cancel,WB_PT_sculpt_session)
def _cleanup_stale_sessions():
    """Run only after Blender has restored normal access to the current file."""
    stale=[obj for obj in bpy.data.objects if obj.get("wb_sculpt_session")]
    if stale:
        for obj in stale:bpy.data.objects.remove(obj,do_unlink=True)
        for obj in bpy.data.objects:
            if obj.get(chunk_terrain.TERRAIN_TAG):obj.hide_viewport=False
    return None

def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_sculpt=PointerProperty(type=WBSculptSessionSettings)
def unregister():
    try:_cleanup()
    except AttributeError:pass
    if hasattr(bpy.types.Scene,"worldbuilder_sculpt"):del bpy.types.Scene.worldbuilder_sculpt
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
