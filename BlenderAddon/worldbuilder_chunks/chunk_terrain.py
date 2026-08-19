"""Authoritative WorldGrid-aligned terrain generation for selected chunks."""
import bpy
from bpy.props import BoolProperty,EnumProperty,FloatProperty,IntProperty,PointerProperty
from bpy.types import Operator,Panel,PropertyGroup
from . import biome,chunk_terrain_math,contract,exporter,localization,state,terrain_toolkit

TERRAIN_TAG="wb_authoritative_chunk_terrain"

class WBChunkTerrainSettings(PropertyGroup):
    scope:EnumProperty(name="Scope",items=(("ACTIVE","Active Chunk",""),("SELECTED","Selected Chunks",""),("RECTANGLE","Rectangle","")),default="SELECTED")
    rect_min_x:IntProperty(name="Min X",default=0);rect_min_z:IntProperty(name="Min Z",default=0);rect_max_x:IntProperty(name="Max X",default=0);rect_max_z:IntProperty(name="Max Z",default=0)
    preset:EnumProperty(name="Preset",items=(("REEF_PLAINS","Reef Plains",""),("RIDGED","Ridged",""),("CANYON","Canyon",""),("PLATEAU","Plateau","")),default="REEF_PLAINS")
    cells:IntProperty(name="Cells per Chunk",default=64,min=4,max=256);seed:IntProperty(name="Seed",default=1234);base_height:FloatProperty(name="Base Height",default=-20);relief:FloatProperty(name="Relief",default=8,min=0);feature_size:FloatProperty(name="Feature Size",default=32,min=.1);replace_existing:BoolProperty(name="Replace Existing",default=True);apply_palette:BoolProperty(name="Apply Palette",default=True)

def selected_coordinates(scene):
    settings=scene.worldbuilder_chunk_terrain;grid=scene.worldbuilder_chunks
    if settings.scope=="ACTIVE":return {state.explicit_active_chunk(grid) or (0,0)}
    if settings.scope=="SELECTED":return state.selected_chunks(grid) or {state.explicit_active_chunk(grid) or (0,0)}
    low_x,high_x=sorted((settings.rect_min_x,settings.rect_max_x));low_z,high_z=sorted((settings.rect_min_z,settings.rect_max_z))
    return {(x,z) for x in range(low_x,high_x+1) for z in range(low_z,high_z+1)}

def terrain_for_coordinate(scene,coordinate):
    return next((obj for obj in scene.objects if obj.get(TERRAIN_TAG) and (obj.get("wb_chunk_x"),obj.get("wb_chunk_z"))==coordinate),None)

def _remove_coordinate(scene,coordinate):
    for obj in list(scene.objects):
        if obj.get(TERRAIN_TAG) and (obj.get("wb_chunk_x"),obj.get("wb_chunk_z"))==coordinate:
            basis=bpy.data.meshes.get(obj.get("wb_terrain_basis_mesh",""));bpy.data.objects.remove(obj,do_unlink=True)
            if basis and basis.users==0:bpy.data.meshes.remove(basis)

def generate_coordinate(scene,coordinate,settings):
    grid=scene.worldbuilder_chunks
    if settings.replace_existing:_remove_coordinate(scene,coordinate)
    elif terrain_for_coordinate(scene,coordinate):return terrain_for_coordinate(scene,coordinate)
    values={"seed":settings.seed,"feature_size":settings.feature_size,"relief":settings.relief,"base_height":settings.base_height,"preset":settings.preset}
    vertices=chunk_terrain_math.chunk_vertices(coordinate,(grid.origin_x,grid.origin_y),grid.chunk_size,settings.cells,values);faces=chunk_terrain_math.chunk_faces(coordinate,settings.cells)
    name=f"WB_Terrain_{contract.chunk_name(coordinate)}";mesh=bpy.data.meshes.new(name+"Mesh");mesh.from_pydata(vertices,[],faces);mesh.update();obj=bpy.data.objects.new(name,mesh)
    collection=exporter.ensure_chunk_collection(scene,coordinate);collection.objects.link(obj);bounds=contract.chunk_bounds_xy(coordinate,grid.origin_x,grid.origin_y,grid.chunk_size);obj.location=(bounds[0],bounds[1],0)
    obj[TERRAIN_TAG]=True;obj["wb_chunk_x"]=coordinate[0];obj["wb_chunk_z"]=coordinate[1];obj["wb_terrain_cells"]=settings.cells;obj["wb_terrain_generation_version"]=1;obj["wb_sculpt_revision"]=0
    obj.worldbuilder_chunk.role="GEOMETRY";obj.worldbuilder_chunk.stable_id=f"terrain_{coordinate[0]}_{coordinate[1]}";obj.worldbuilder_chunk.override_chunk=True;obj.worldbuilder_chunk.chunk_x=coordinate[0];obj.worldbuilder_chunk.chunk_z=coordinate[1]
    basis=mesh.copy();basis.name=f"WB_BASIS_TERRAIN_{coordinate[0]}_{coordinate[1]}";obj["wb_terrain_basis_mesh"]=basis.name;biome.mark_biome_target(obj)
    if settings.apply_palette:terrain_toolkit._assign_terrain_materials(obj,scene.nex_terrain_settings)
    state.mark_dirty(grid,coordinate);return obj

class WB_OT_generate_chunk_terrain(Operator):
    bl_idname="worldbuilder.generate_chunk_terrain";bl_label="Generate Selected Chunk Terrain";bl_options={"UNDO"}
    def invoke(self,context,event):
        s=context.scene.worldbuilder_chunk_terrain
        if s.replace_existing and any(terrain_for_coordinate(context.scene,coord) for coord in selected_coordinates(context.scene)):return context.window_manager.invoke_confirm(self,event)
        return self.execute(context)
    def execute(self,context):
        settings=context.scene.worldbuilder_chunk_terrain;coords=sorted(selected_coordinates(context.scene))
        if not coords:self.report({"ERROR"},"No chunk coordinate is selected");return {"CANCELLED"}
        estimated=len(coords)*(settings.cells+1)**2
        if estimated>1_500_000:self.report({"ERROR"},f"Requested terrain is too dense ({estimated:,} vertices)");return {"CANCELLED"}
        created=[generate_coordinate(context.scene,coord,settings) for coord in coords];bpy.ops.object.select_all(action="DESELECT")
        for obj in created:obj.select_set(True)
        if created:context.view_layer.objects.active=created[0]
        self.report({"INFO"},f"Generated {len(created)} authoritative terrain chunk(s)");return {"FINISHED"}

class WB_PT_chunk_terrain(Panel):
    bl_label="Chunk Terrain";bl_idname="WB_PT_chunk_terrain";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_chunk_terrain;t=lambda key:localization.tr(key,context.scene);layout.label(text=t("chunk_terrain"),icon="MESH_GRID");layout.prop(s,"scope",text=t("terrain_scope"))
        if s.scope=="RECTANGLE":
            row=layout.row(align=True);row.prop(s,"rect_min_x");row.prop(s,"rect_min_z");row=layout.row(align=True);row.prop(s,"rect_max_x");row.prop(s,"rect_max_z")
        layout.prop(s,"preset");layout.prop(s,"cells",text=t("cells"));row=layout.row(align=True);row.prop(s,"base_height",text=t("base_height"));row.prop(s,"relief",text=t("relief"));layout.prop(s,"feature_size",text=t("feature_size"));layout.prop(s,"seed");layout.prop(s,"replace_existing",text=t("replace"));layout.prop(s,"apply_palette");layout.operator("worldbuilder.generate_chunk_terrain",text=t("terrain_fill"),icon="MOD_BUILD")

CLASSES=(WBChunkTerrainSettings,WB_OT_generate_chunk_terrain,WB_PT_chunk_terrain)
def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_chunk_terrain=PointerProperty(type=WBChunkTerrainSettings)
def unregister():
    if hasattr(bpy.types.Scene,"worldbuilder_chunk_terrain"):del bpy.types.Scene.worldbuilder_chunk_terrain
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
