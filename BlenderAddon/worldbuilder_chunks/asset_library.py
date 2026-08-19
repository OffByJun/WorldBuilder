"""Collection-instance structure library and interactive surface placement."""
import math,os,uuid
import bpy
from bpy.props import BoolProperty,CollectionProperty,EnumProperty,FloatProperty,IntProperty,PointerProperty,StringProperty
from bpy.types import Operator,Panel,PropertyGroup,UIList
from bpy_extras import view3d_utils
from mathutils import Quaternion,Vector
from . import contract,entity_catalog,exporter,layers,localization,stamp_io,state

_records={}
def _draft_pick_changed(self,context):
    scene=getattr(context,"scene",None)
    if scene is not None:entity_catalog.apply_pick(self,scene,self.draft_entity_catalog_pick,"draft_entity_")
_ENTITY_KIND_ITEMS=tuple((value,value,"") for value in contract.ENTITY_KINDS)
_PLACEMENT_KIND_ITEMS=(("STATIC","Static","Plain Unity prefab placement"),("ENTITY","Entity","DOTS entity placement driven by WorldEntityAuthoring"))

class WBStructureAsset(PropertyGroup):
    asset_id:StringProperty(name="Asset ID");name_ko:StringProperty(name="Korean Name");name_en:StringProperty(name="English Name");category:StringProperty(name="Category",default="Environment/Rock");source_collection:PointerProperty(name="Source Collection",type=bpy.types.Collection);collection_name:StringProperty();ownership:EnumProperty(name="Ownership",items=(("CHUNK","Chunk",""),("REGION","Region","")),default="CHUNK");align_to_surface:BoolProperty(name="Align to Surface",default=True);scale_min:FloatProperty(name="Min Scale",default=1,min=.001);scale_max:FloatProperty(name="Max Scale",default=1,min=.001);placement_kind:EnumProperty(name="Placement Kind",items=_PLACEMENT_KIND_ITEMS,default="STATIC");entity_prefab_id:IntProperty(name="Entity Prefab ID",default=0,min=0);entity_kind:EnumProperty(name="Entity Kind",items=_ENTITY_KIND_ITEMS,default="Generic");entity_persistent:BoolProperty(name="Persistent",default=False);entity_region_streamed:BoolProperty(name="Region Streamed",default=True);entity_replicated:BoolProperty(name="Replicated",default=False);entity_lifetime:FloatProperty(name="Lifetime",default=0,min=0)
class WBStructureLibrarySettings(PropertyGroup):
    registry_file:StringProperty(name="Asset Registry",subtype="FILE_PATH");assets:CollectionProperty(type=WBStructureAsset);active_index:IntProperty(default=0);search:StringProperty(name="Search");draft_collection:PointerProperty(name="Source Collection",type=bpy.types.Collection);draft_asset_id:StringProperty(name="Asset ID");draft_name_ko:StringProperty(name="Korean Name");draft_name_en:StringProperty(name="English Name");draft_category:StringProperty(name="Category",default="Environment/Rock");draft_ownership:EnumProperty(name="Ownership",items=(("CHUNK","Chunk",""),("REGION","Region","")),default="CHUNK");align_to_surface:BoolProperty(name="Align to Surface",default=True);scale_min:FloatProperty(name="Min Scale",default=.9,min=.001);scale_max:FloatProperty(name="Max Scale",default=1.1,min=.001);draft_placement_kind:EnumProperty(name="Placement Kind",items=_PLACEMENT_KIND_ITEMS,default="STATIC");draft_entity_prefab_id:IntProperty(name="Entity Prefab ID",default=0,min=0);draft_entity_kind:EnumProperty(name="Entity Kind",items=_ENTITY_KIND_ITEMS,default="Generic");draft_entity_persistent:BoolProperty(name="Persistent",default=False);draft_entity_region_streamed:BoolProperty(name="Region Streamed",default=True);draft_entity_replicated:BoolProperty(name="Replicated",default=False);draft_entity_lifetime:FloatProperty(name="Lifetime",default=0,min=0);draft_entity_catalog_pick:EnumProperty(name="Catalog",items=entity_catalog.enum_items,update=_draft_pick_changed)

def _active(scene):
    settings=scene.worldbuilder_structure_library
    return settings.assets[settings.active_index] if settings.assets and settings.active_index<len(settings.assets) else None
def _registry_path(scene):return bpy.path.abspath(scene.worldbuilder_structure_library.registry_file)
def _record(item,registry_path):
    blend=bpy.data.filepath;relative=os.path.relpath(blend,os.path.dirname(registry_path)) if blend and registry_path else ""
    return {"assetId":item.asset_id,"displayName":{"ko":item.name_ko,"en":item.name_en},"category":item.category,"blendPath":relative,"collectionName":item.source_collection.name if item.source_collection else item.collection_name,"ownership":item.ownership,"alignToSurface":item.align_to_surface,"scaleMin":item.scale_min,"scaleMax":item.scale_max,"unityPrefabId":item.asset_id,"placementKind":item.placement_kind,"entity":{"prefabId":item.entity_prefab_id,"kind":item.entity_kind,"flags":contract.entity_flag_names(item.entity_persistent,item.entity_region_streamed,item.entity_replicated),"lifetimeSeconds":contract.normalized_float(item.entity_lifetime)},"version":1}
def save_registry(scene):
    path=_registry_path(scene)
    if not path:raise ValueError("Choose an Asset Registry path")
    entries=[_record(item,path) for item in scene.worldbuilder_structure_library.assets];os.makedirs(os.path.dirname(path),exist_ok=True);stamp_io.save_asset_registry(path,entries)
def _resolve_collection(entry,path):
    name=entry.get("collectionName","");collection=bpy.data.collections.get(name)
    if collection:return collection
    blend=entry.get("blendPath","");blend=os.path.normpath(os.path.join(os.path.dirname(path),blend)) if blend and not os.path.isabs(blend) else blend
    if not blend or not os.path.isfile(blend):return None
    with bpy.data.libraries.load(blend,link=True) as (source,target):target.collections=[name] if name in source.collections else []
    return target.collections[0] if target.collections else None
def load_registry(scene):
    global _records
    path=_registry_path(scene)
    if not path:raise ValueError("Choose an Asset Registry path")
    _records=stamp_io.load_asset_registry(path);settings=scene.worldbuilder_structure_library;settings.assets.clear()
    for asset_id,entry in sorted(_records.items()):
        item=settings.assets.add();item.asset_id=asset_id;names=entry.get("displayName",{});item.name_ko=names.get("ko","");item.name_en=names.get("en","");item.category=entry.get("category","");item.collection_name=entry.get("collectionName","");item.source_collection=_resolve_collection(entry,path);item.ownership=entry.get("ownership","CHUNK") if entry.get("ownership") in {"CHUNK","REGION"} else "CHUNK";item.align_to_surface=bool(entry.get("alignToSurface",True));item.scale_min=max(.001,float(entry.get("scaleMin",1)));item.scale_max=max(item.scale_min,float(entry.get("scaleMax",1)));item.placement_kind=entry.get("placementKind","STATIC") if entry.get("placementKind") in {"STATIC","ENTITY"} else "STATIC";entity=entry.get("entity") or {};item.entity_prefab_id=max(0,int(entity.get("prefabId",0)));item.entity_kind=entity.get("kind","Generic") if entity.get("kind") in contract.ENTITY_KINDS else "Generic";flags=set(entity.get("flags") or ());item.entity_persistent="Persistent" in flags;item.entity_region_streamed="RegionStreamed" in flags;item.entity_replicated="Replicated" in flags;item.entity_lifetime=max(0.,float(entity.get("lifetimeSeconds",0)))
    settings.active_index=min(settings.active_index,max(0,len(settings.assets)-1));return len(settings.assets)

def find_asset(scene,asset_id):
    settings=getattr(scene,"worldbuilder_structure_library",None)
    if settings is None or not str(asset_id).strip():return None
    return next((item for item in settings.assets if item.asset_id==asset_id),None)
def apply_entity_properties(properties,item):
    properties.entity_prefab_id=item.entity_prefab_id;properties.entity_kind=item.entity_kind;properties.entity_persistent=item.entity_persistent;properties.entity_region_streamed=item.entity_region_streamed;properties.entity_replicated=item.entity_replicated;properties.entity_lifetime=item.entity_lifetime

def _owner_coordinate(scene,location,ownership):
    grid=scene.worldbuilder_chunks;coord=contract.chunk_coord_from_xy(location.x,location.y,grid.origin_x,grid.origin_y,grid.chunk_size)
    if ownership=="REGION":
        region=contract.region_coord(coord[0],coord[1],grid.chunks_per_region);return region[0]*grid.chunks_per_region,region[1]*grid.chunks_per_region
    return coord
def placement_location(scene,location):
    grid=getattr(scene,"worldbuilder_chunks",None)
    return layers.clamp_to_active_layer(location,grid) if grid is not None and grid.layer_lock_placement else location
def create_instance(scene,item,location,normal=Vector((0,0,1)),yaw=0.0,scale=1.0,preview=False):
    if item is None or item.source_collection is None:raise ValueError("Selected asset has no resolved source collection")
    entity=item.placement_kind=="ENTITY";prefix="ENT" if entity else "INST";location=placement_location(scene,Vector(location))
    obj=bpy.data.objects.new(f"{prefix}_{item.asset_id}_{uuid.uuid4().hex[:8]}",None);obj.instance_type="COLLECTION";obj.instance_collection=item.source_collection;scene.collection.objects.link(obj);obj.location=location;obj.rotation_mode="QUATERNION";surface=Vector((0,0,1)).rotation_difference(normal.normalized()) if item.align_to_surface else Quaternion();obj.rotation_quaternion=Quaternion(normal.normalized(),yaw)@surface;obj.scale=(scale,scale,scale)
    obj.worldbuilder_chunk.role="GLOBAL" if preview else ("ENTITY" if entity else "INSTANCE");obj.worldbuilder_chunk.asset_id=item.asset_id;obj.worldbuilder_chunk.stable_id=uuid.uuid4().hex;obj["wb_structure_asset"]=True;obj["wb_streaming_ownership"]=item.ownership;obj["wb_asset_preview"]=preview
    if entity:apply_entity_properties(obj.worldbuilder_chunk,item)
    if not preview:
        coordinate=_owner_coordinate(scene,location,item.ownership);obj.worldbuilder_chunk.override_chunk=True;obj.worldbuilder_chunk.chunk_x=coordinate[0];obj.worldbuilder_chunk.chunk_z=coordinate[1];obj["wb_prop_streaming_ownership"]=item.ownership;state.mark_dirty(scene.worldbuilder_chunks,coordinate)
    return obj

class WB_UL_structure_assets(UIList):
    def filter_items(self,context,data,propname):
        items=getattr(data,propname);query=context.scene.worldbuilder_structure_library.search.strip().lower();flags=[]
        for item in items:flags.append(self.bitflag_filter_item if not query or query in f"{item.asset_id} {item.name_ko} {item.name_en} {item.category}".lower() else 0)
        return flags,[]
    def draw_item(self,context,layout,_data,item,_icon,_active,_prop,_index):
        name=item.name_ko if localization.language(context.scene)=="ko" and item.name_ko else item.name_en or item.asset_id;entity=item.placement_kind=="ENTITY";layout.label(text=name,icon="OUTLINER_OB_POINTCLOUD" if entity else "OUTLINER_COLLECTION");layout.label(text=f"{item.entity_kind} #{item.entity_prefab_id}" if entity else item.category)
class WB_OT_structure_register(Operator):
    bl_idname="worldbuilder.structure_asset_register";bl_label="Register Structure Asset"
    def execute(self,context):
        s=context.scene.worldbuilder_structure_library
        if not s.draft_asset_id.strip() or s.draft_collection is None:self.report({"ERROR"},"Asset ID and Source Collection are required");return {"CANCELLED"}
        path=_registry_path(context.scene)
        if not s.assets and path and os.path.isfile(path):
            try:load_registry(context.scene)
            except (OSError,ValueError):pass
        item=next((value for value in s.assets if value.asset_id==s.draft_asset_id.strip()),None) or s.assets.add();item.asset_id=s.draft_asset_id.strip();item.name_ko=s.draft_name_ko;item.name_en=s.draft_name_en;item.category=s.draft_category;item.source_collection=s.draft_collection;item.collection_name=s.draft_collection.name;item.ownership=s.draft_ownership;item.align_to_surface=s.align_to_surface;item.scale_min=min(s.scale_min,s.scale_max);item.scale_max=max(s.scale_min,s.scale_max);item.placement_kind=s.draft_placement_kind;item.entity_prefab_id=s.draft_entity_prefab_id;item.entity_kind=s.draft_entity_kind;item.entity_persistent=s.draft_entity_persistent;item.entity_region_streamed=s.draft_entity_region_streamed;item.entity_replicated=s.draft_entity_replicated;item.entity_lifetime=s.draft_entity_lifetime
        for source in item.source_collection.all_objects:
            if hasattr(source,"worldbuilder_chunk"):source.worldbuilder_chunk.role="GLOBAL"
        if hasattr(item.source_collection,"asset_mark"):
            item.source_collection.asset_mark()
            if item.source_collection.asset_data:item.source_collection.asset_data.description=item.name_ko or item.name_en or item.asset_id
        try:save_registry(context.scene)
        except (OSError,ValueError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        s.active_index=list(s.assets).index(item);self.report({"INFO"},f"Registered {item.asset_id}");return {"FINISHED"}
class WB_OT_structure_reload(Operator):
    bl_idname="worldbuilder.structure_asset_reload";bl_label="Reload Structure Registry"
    def execute(self,context):
        try:count=load_registry(context.scene)
        except (OSError,ValueError) as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Loaded {count} structure assets");return {"FINISHED"}
class WB_OT_structure_place_cursor(Operator):
    bl_idname="worldbuilder.structure_place_cursor";bl_label="Place Structure at Cursor";bl_options={"UNDO"}
    def execute(self,context):
        item=_active(context.scene)
        try:obj=create_instance(context.scene,item,Vector(context.scene.cursor.location),scale=(item.scale_min+item.scale_max)*.5 if item else 1)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        context.view_layer.objects.active=obj;obj.select_set(True);return {"FINISHED"}
class WB_OT_structure_place_surface(Operator):
    bl_idname="worldbuilder.structure_place_surface";bl_label="Structure Surface Placement";bl_options={"REGISTER","UNDO","BLOCKING"};_preview=None;_yaw=0.0;_scale=1.0;_region=None
    def _update(self,context,event):
        region=self._region;rv3d=context.space_data.region_3d;mouse=(event.mouse_x-region.x,event.mouse_y-region.y);origin=view3d_utils.region_2d_to_origin_3d(region,rv3d,mouse);direction=view3d_utils.region_2d_to_vector_3d(region,rv3d,mouse);hit,location,normal,*_=context.scene.ray_cast(context.evaluated_depsgraph_get(),origin,direction,distance=100000)
        if hit:self._preview.location=placement_location(context.scene,location);item=_active(context.scene);surface=Vector((0,0,1)).rotation_difference(normal.normalized()) if item and item.align_to_surface else Quaternion();self._preview.rotation_quaternion=Quaternion(normal.normalized(),self._yaw)@surface
    def invoke(self,context,event):
        item=_active(context.scene)
        if item is None or item.source_collection is None:self.report({"ERROR"},localization.tr("no_asset",context.scene));return {"CANCELLED"}
        self._region=next((region for region in context.area.regions if region.type=="WINDOW"),None)
        if self._region is None:return {"CANCELLED"}
        self._scale=(item.scale_min+item.scale_max)*.5;self._preview=create_instance(context.scene,item,Vector(context.scene.cursor.location),scale=self._scale,preview=True);context.window.cursor_modal_set("CROSSHAIR");context.window_manager.modal_handler_add(self);return {"RUNNING_MODAL"}
    def modal(self,context,event):
        if event.type=="MOUSEMOVE":self._update(context,event);return {"RUNNING_MODAL"}
        if event.type in {"WHEELUPMOUSE","WHEELDOWNMOUSE"}:
            direction=1 if event.type=="WHEELUPMOUSE" else -1
            if event.shift:
                item=_active(context.scene);self._scale=max(item.scale_min,min(item.scale_max,self._scale*(1+direction*.05)));self._preview.scale=(self._scale,)*3
            else:self._yaw+=direction*math.radians(5)
            self._update(context,event);return {"RUNNING_MODAL"}
        if event.type=="LEFTMOUSE" and event.value=="PRESS":
            item=_active(context.scene);location=self._preview.location.copy();rotation=self._preview.rotation_quaternion.copy();bpy.data.objects.remove(self._preview,do_unlink=True);obj=create_instance(context.scene,item,location,scale=self._scale);obj.rotation_quaternion=rotation;self._preview=create_instance(context.scene,item,location,scale=self._scale,preview=True);return {"RUNNING_MODAL"}
        if event.type in {"RIGHTMOUSE","ESC"}:
            if self._preview:bpy.data.objects.remove(self._preview,do_unlink=True)
            context.window.cursor_modal_restore();return {"FINISHED"}
        return {"RUNNING_MODAL"}
    def cancel(self,context):
        if self._preview and self._preview.name in bpy.data.objects:bpy.data.objects.remove(self._preview,do_unlink=True)
        context.window.cursor_modal_restore()

class WB_PT_structure_library(Panel):
    bl_label="Structure Library";bl_idname="WB_PT_structure_library";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_structure_library;t=lambda key:localization.tr(key,context.scene);layout.label(text=t("asset_library"),icon="ASSET_MANAGER");row=layout.row(align=True);row.prop(s,"registry_file",text=t("registry"));row.operator("worldbuilder.structure_asset_reload",text="",icon="FILE_REFRESH");layout.prop(s,"search",text=t("search"));layout.template_list("WB_UL_structure_assets","",s,"assets",s,"active_index",rows=5)
        box=layout.box();box.label(text=t("save_asset"));box.prop(s,"draft_collection",text=t("source_collection"));box.prop(s,"draft_asset_id",text=t("asset_id"));row=box.row(align=True);row.prop(s,"draft_name_ko",text=t("name_ko"));row.prop(s,"draft_name_en",text=t("name_en"));box.prop(s,"draft_category",text=t("category"));box.prop(s,"draft_ownership",text=t("ownership"));box.prop(s,"align_to_surface",text=t("align_surface"));row=box.row(align=True);row.prop(s,"scale_min",text=t("scale_min"));row.prop(s,"scale_max",text=t("scale_max"));box.prop(s,"draft_placement_kind",text=t("placement_kind"))
        if s.draft_placement_kind=="ENTITY":
            entity_box=box.box();entity_catalog.draw_picker(entity_box,context.scene,s,"draft_entity_catalog_pick","draft_entity_prefab_id");entity_box.prop(s,"draft_entity_prefab_id",text=t("entity_prefab_id"));entity_box.prop(s,"draft_entity_kind",text=t("entity_kind"));row=entity_box.row(align=True);row.prop(s,"draft_entity_persistent",text=t("entity_persistent"),toggle=True);row.prop(s,"draft_entity_region_streamed",text=t("entity_region_streamed"),toggle=True);row.prop(s,"draft_entity_replicated",text=t("entity_replicated"),toggle=True);entity_box.prop(s,"draft_entity_lifetime")
        box.operator("worldbuilder.structure_asset_register",text=t("save_asset"))
        row=layout.row(align=True);row.operator("worldbuilder.structure_place_cursor",text=t("place_cursor"));row.operator("worldbuilder.structure_place_surface",text=t("place_surface"))

CLASSES=(WBStructureAsset,WBStructureLibrarySettings,WB_UL_structure_assets,WB_OT_structure_register,WB_OT_structure_reload,WB_OT_structure_place_cursor,WB_OT_structure_place_surface,WB_PT_structure_library)
def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_structure_library=PointerProperty(type=WBStructureLibrarySettings)
def unregister():
    if hasattr(bpy.types.Scene,"worldbuilder_structure_library"):del bpy.types.Scene.worldbuilder_structure_library
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
