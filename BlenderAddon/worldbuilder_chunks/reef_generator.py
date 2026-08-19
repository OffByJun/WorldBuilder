"""Deterministic stylized reef formations inspired by layered underwater rock references."""
from __future__ import annotations

import math
import random
import uuid
from dataclasses import dataclass

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Vector

from . import localization, rock_generator

TAG = "wb_reef_generated"
SCHEMA_VERSION = 1

FORMATIONS = (
    ("MOUND", "Reef Mound", "Rounded multi-level reef outcrop"),
    ("TERRACES", "Layered Terraces", "Wide stepped shelves and ledges"),
    ("SPIRE", "Reef Spire", "Tall asymmetric underwater pinnacle"),
    ("ARCH", "Reef Arch", "Walkable natural arch with thick supports"),
)
ASSET_KINDS = (
    ("COMPLETE", "Complete Reef", "Rock formation with sand, seaweed, coral, and pebbles"),
    ("ROCK_ONLY", "Rock Formation", "Formation and ground dressing without plants"),
    ("SEAWEED_PATCH", "Seaweed Patch", "Standalone animated seaweed Collection Asset for scatter"),
    ("CORAL_PATCH", "Coral Patch", "Standalone branching and tube coral Collection Asset for scatter"),
)


@dataclass(frozen=True)
class RockSpec:
    location: tuple[float, float, float]
    size: tuple[float, float, float]
    rotation: float
    style: str
    seed_offset: int


class MeshBatch:
    def __init__(self):
        self.vertices: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, ...]] = []
        self.materials: list[int] = []

    def face(self, indices, material=0):
        self.faces.append(tuple(indices)); self.materials.append(material)

    def tube(self, points, radii, sides=5, material=0, cap_material=None):
        start = len(self.vertices)
        count = len(points)
        for ring, point in enumerate(points):
            for side in range(sides):
                angle = math.tau * side / sides
                self.vertices.append((point.x + math.cos(angle) * radii[ring], point.y + math.sin(angle) * radii[ring], point.z))
        for ring in range(count - 1):
            for side in range(sides):
                nxt = (side + 1) % sides
                a = start + ring * sides + side; b = start + ring * sides + nxt
                c = start + (ring + 1) * sides + nxt; d = start + (ring + 1) * sides + side
                self.face((a, b, c, d), material)
        bottom = len(self.vertices); self.vertices.append(tuple(points[0]))
        top = len(self.vertices); self.vertices.append(tuple(points[-1]))
        for side in range(sides):
            nxt = (side + 1) % sides
            self.face((bottom, start + nxt, start + side), material)
            self.face((top, start + (count - 1) * sides + side, start + (count - 1) * sides + nxt), cap_material if cap_material is not None else material)

    def append_rock(self, vertices, faces, location, scale, rotation, material=0):
        start = len(self.vertices); c = math.cos(rotation); s = math.sin(rotation)
        for x, y, z in vertices:
            x *= scale[0]; y *= scale[1]; z *= scale[2]
            self.vertices.append((location[0] + x * c - y * s, location[1] + x * s + y * c, location[2] + z))
        for face in faces:self.face((start + index for index in face), material)


def _set_material(name, color, roughness=.82, metallic=0.0):
    value = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    value.diffuse_color = color; value.use_nodes = True
    node = value.node_tree.nodes.get("Principled BSDF")
    if node:
        node.inputs["Base Color"].default_value = color
        node.inputs["Roughness"].default_value = roughness
        node.inputs["Metallic"].default_value = metallic
    return value


def _palette():
    return {
        "rock": rock_generator._get_palette()["rock_base"],
        "rock_light": rock_generator._get_palette()["rock_light"],
        "rock_dark": rock_generator._get_palette()["rock_dark"],
        "sand": rock_generator._get_palette()["sand"],
        "kelp_dark": _set_material("WB_Reef_Kelp_Dark", (.035, .22, .11, 1)),
        "kelp": _set_material("WB_Reef_Kelp", (.08, .47, .20, 1)),
        "kelp_tip": _set_material("WB_Reef_Kelp_Tip", (.42, .68, .08, 1)),
        "coral_pink": _set_material("WB_Reef_Coral_Pink", (.92, .18, .52, 1), .72),
        "coral_blue": _set_material("WB_Reef_Coral_Blue", (.05, .56, .94, 1), .72),
        "coral_yellow": _set_material("WB_Reef_Coral_Yellow", (.96, .62, .08, 1), .74),
        "coral_dark": _set_material("WB_Reef_Coral_Opening", (.045, .035, .055, 1), .92),
    }


def _stable_id(asset_id, suffix):
    return uuid.uuid5(uuid.NAMESPACE_URL, f"worldbuilder:{asset_id}:{suffix}").hex


def _tag(obj, asset_id, suffix):
    obj[TAG] = True; obj["wb_reef_schema_version"] = SCHEMA_VERSION; obj["wb_role"] = "GLOBAL"
    props = getattr(obj, "worldbuilder_chunk", None)
    if props is not None:props.role = "GLOBAL"; props.stable_id = _stable_id(asset_id, suffix)
    else:obj["wb_id"] = _stable_id(asset_id, suffix)


def _mesh_object(name, batch, collection, materials, asset_id, suffix):
    mesh = bpy.data.meshes.new(name + "Mesh"); mesh.from_pydata(batch.vertices, [], batch.faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); collection.objects.link(obj)
    for material in materials:mesh.materials.append(material)
    for polygon, index in zip(mesh.polygons, batch.materials):polygon.material_index = min(index, len(materials) - 1); polygon.use_smooth = False
    _tag(obj, asset_id, suffix); return obj


def _formation_specs(style, width, depth, height, seed):
    rng = random.Random(seed); specs: list[RockSpec] = []
    def add(x, y, z, sx, sy, sz, rock_style="SLAB"):
        index = len(specs); specs.append(RockSpec((x, y, z), (sx, sy, sz), rng.uniform(-.22, .22), rock_style, index * 193 + 17))
    if style == "MOUND":
        levels = 4
        for level in range(levels):
            t = level / levels; count = max(1, 5 - level)
            ring = width * (.23 - t * .10); z = height * t * .72
            for i in range(count):
                angle = math.tau * i / count + rng.uniform(-.35, .35)
                add(math.cos(angle) * ring, math.sin(angle) * ring * depth / width, z,
                    width * (.34 - t * .075), depth * (.38 - t * .08), height * (.28 - t * .025), "BOULDER" if level == 0 else "SLAB")
    elif style == "TERRACES":
        levels = 5
        for level in range(levels):
            t = level / (levels - 1); z = height * t * .72
            drift = (t - .5) * width * .12
            add(drift, rng.uniform(-depth*.04, depth*.04), z, width * (.72 - t*.38), depth * (1.0 - t*.52), height*.22, "TERRACE")
            for side in (-1,1):
                side_x=drift+side*width*(.34-t*.13)
                add(side_x, depth*rng.uniform(-.15,.15), z-height*rng.uniform(.01,.045),
                    width*(.31-t*.065), depth*(.46-t*.10), height*rng.uniform(.17,.22), "SLAB")
    elif style == "SPIRE":
        levels = 7; segment = height / levels
        drift = Vector((0.0, 0.0))
        for level in range(levels):
            t = level / (levels - 1); drift += Vector((rng.uniform(-width*.025,width*.025), rng.uniform(-depth*.025,depth*.025)))
            add(drift.x, drift.y, level*segment*.88, width*(.64-t*.35), depth*(.68-t*.38), segment*1.12, "PILLAR")
            if level in {1, 2, 4}:
                angle = rng.uniform(0, math.tau); add(drift.x+math.cos(angle)*width*(.30-t*.08), drift.y+math.sin(angle)*depth*(.30-t*.08), level*segment*.84,
                    width*.28, depth*.30, segment*.72, "SLAB")
    else:  # ARCH
        opening = width*.52; levels = 4; segment = height*.58/levels
        for side in (-1, 1):
            for level in range(levels):
                t = level/(levels-1); x = side*(opening*.5 + width*(.13-t*.025))
                add(x, rng.uniform(-depth*.035,depth*.035), level*segment*.92, width*(.27-t*.035), depth*(.54-t*.08), segment*1.15, "PILLAR")
            add(side*width*.40, 0, 0, width*.25, depth*.60, height*.22, "BOULDER")
        bridge_count = 5
        for i in range(bridge_count):
            t = i/(bridge_count-1); x = (t-.5)*opening; z = height*.56 + math.sin(t*math.pi)*height*.10
            add(x, 0, z, width*.24, depth*.48, height*.18, "SLAB")
    return specs


def _block_geometry(size, seed, style, irregularity):
    """Create vertical fractured cliff modules instead of radial pancake rocks."""
    rng=random.Random(seed);width,depth,height=size
    outline=((-1,-.35),(-.82,-.78),(-.35,-1),(.35,-1),(.82,-.78),(1,-.35),(1,.35),(.78,.82),(.32,1),(-.35,1),(-.82,.76),(-1,.30))
    profiles={"PILLAR":(1.0,1.0,.98,.93,.86,.76),"TERRACE":(1.0,1.0,.98,.96,.91,.84),"BOULDER":(.82,.94,1.0,.94,.82,.64),"SLAB":(1.0,1.0,.99,.97,.92,.86)}
    scales=profiles.get(style,profiles["SLAB"]);zs=(0,.18,.38,.60,.80,1.0);vertices=[];faces=[];sides=len(outline);drift=Vector((0,0))
    for ring,(z,scale) in enumerate(zip(zs,scales)):
        if ring:drift+=Vector((rng.uniform(-width*.018,width*.018),rng.uniform(-depth*.018,depth*.018)))
        for x,y in outline:
            radial=1+rng.uniform(-(.025+irregularity*.18),.025+irregularity*.18)
            vertices.append((drift.x+x*width*.5*scale*radial,drift.y+y*depth*.5*scale*radial,z*height+rng.uniform(-height*.018,height*.018) if ring not in {0,3} else z*height))
    for ring in range(len(zs)-1):
        for side in range(sides):
            nxt=(side+1)%sides;a=ring*sides+side;b=ring*sides+nxt;c=(ring+1)*sides+nxt;d=(ring+1)*sides+side
            if (ring+side+seed)%2:faces.extend(((a,b,c),(a,c,d)))
            else:faces.extend(((a,b,d),(b,c,d)))
    bottom=len(vertices);vertices.append((0,0,0));top=len(vertices);vertices.append((drift.x,drift.y,height))
    top_ring=(len(zs)-1)*sides
    for side in range(sides):
        nxt=(side+1)%sides;faces.append((bottom,nxt,side));faces.append((top,top_ring+side,top_ring+nxt))
    return vertices,faces


def _create_block_object(spec,index,settings,collection,root,asset_id):
    vertices,faces=_block_geometry(spec.size,settings.seed+spec.seed_offset,spec.style,settings.irregularity)
    name=f"ReefRock_{index:02d}";mesh=bpy.data.meshes.new(name+"Mesh");mesh.from_pydata(vertices,[],faces);mesh.update();obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    rock_generator._assign_rock_materials(obj,settings.seed+spec.seed_offset);obj.parent=root;obj.location=spec.location;obj.rotation_euler.z=spec.rotation;_tag(obj,asset_id,f"rock:{index}");return obj


def _create_formation(settings, collection, root, asset_id):
    specs = _formation_specs(settings.formation, settings.width, settings.depth, settings.height, settings.seed)
    anchors=[]
    for index, spec in enumerate(specs):
        _create_block_object(spec,index,settings,collection,root,asset_id)
        anchors.append((Vector((spec.location[0], spec.location[1], spec.location[2] + spec.size[2]*.96)), min(spec.size[0], spec.size[1])*.36))
    return specs, anchors


def _create_sand(settings, collection, root, asset_id):
    if not settings.sand_base:return
    params = rock_generator.RockBuildParams("SLAB", settings.seed+7001, settings.width*1.22, settings.depth*1.25,
        max(.15, settings.height*.025), 14, 3, .18, False, 0, True)
    obj = rock_generator._create_sand_base(params, collection, root, .62); obj.name="ReefSandBase"; _tag(obj, asset_id, "sand")


def _chosen_anchors(anchors, density, rng, maximum):
    if density <= 0:return []
    values=list(anchors); rng.shuffle(values); count=min(maximum, max(1, round(len(values)*density)))
    return values[:count]


def _seaweed_batch(settings, anchors, rng):
    batch=MeshBatch()
    for cluster,(anchor,radius) in enumerate(_chosen_anchors(anchors,settings.seaweed_density*settings.decoration_density,rng,18)):
        stems=rng.randint(3,6)
        for stem in range(stems):
            angle=rng.uniform(0,math.tau); base=anchor+Vector((math.cos(angle)*radius*rng.uniform(0,.85),math.sin(angle)*radius*rng.uniform(0,.85),.02))
            height=settings.height*rng.uniform(.055,.15); bend=Vector((rng.uniform(-height*.16,height*.16),rng.uniform(-height*.16,height*.16),0))
            points=[]; segments=4
            for i in range(segments+1):
                t=i/segments; sway=math.sin(t*math.pi)*height*.055
                points.append(base+Vector((bend.x*t+sway*math.cos(angle),bend.y*t+sway*math.sin(angle),height*t)))
            radius0=max(.045,settings.width*.011*rng.uniform(.75,1.3)); batch.tube(points,[radius0*(1-t*.68) for t in [i/segments for i in range(segments+1)]],5,1 if stem%4 else 0,2)
            branch_count=2 if stem%2 else 3
            for branch in range(branch_count):
                point_index=min(3,1+branch);mid=points[point_index];branch_angle=angle+(-1 if branch%2 else 1)*rng.uniform(.55,1.25)
                direction=Vector((math.cos(branch_angle),math.sin(branch_angle),rng.uniform(.7,1.2))).normalized();end=mid+direction*height*rng.uniform(.22,.36)
                batch.tube([mid,end],[radius0*.62,radius0*.17],5,1,2)
    return batch


def _coral_batch(settings, anchors, rng):
    batch=MeshBatch(); colors=(0,1,2)
    for cluster,(anchor,radius) in enumerate(_chosen_anchors(anchors,settings.coral_density*settings.decoration_density,rng,14)):
        color=colors[cluster%3]; base=anchor+Vector((rng.uniform(-radius,radius)*.45,rng.uniform(-radius,radius)*.45,.025))
        if cluster%3==0:  # Tube sponge cluster.
            for tube in range(rng.randint(3,6)):
                angle=rng.uniform(0,math.tau); offset=Vector((math.cos(angle)*radius*rng.uniform(0,.6),math.sin(angle)*radius*rng.uniform(0,.6),0))
                height=settings.height*rng.uniform(.045,.105); r=max(.05,settings.width*rng.uniform(.009,.016))
                batch.tube([base+offset,base+offset+Vector((rng.uniform(-r,r),rng.uniform(-r,r),height))],[r,r*.72],5,color,3)
        else:  # Branching coral.
            trunk_h=settings.height*rng.uniform(.065,.14); r=max(.045,settings.width*.009)
            trunk=[base,base+Vector((0,0,trunk_h*.58)),base+Vector((rng.uniform(-r*2,r*2),rng.uniform(-r*2,r*2),trunk_h))]
            batch.tube(trunk,[r,r*.72,r*.28],5,color)
            for branch in range(rng.randint(3,6)):
                angle=math.tau*branch/rng.randint(3,6)+rng.uniform(-.35,.35); start=trunk[1]; length=trunk_h*rng.uniform(.38,.68)
                end=start+Vector((math.cos(angle)*length*.55,math.sin(angle)*length*.55,length*.82)); batch.tube([start,end],[r*.55,r*.18],5,color)
    return batch


def _pebble_batch(settings, rng):
    batch=MeshBatch(); count=settings.pebble_count
    for index in range(count):
        angle=rng.uniform(0,math.tau); distance=math.sqrt(rng.random())
        location=(math.cos(angle)*settings.width*.64*distance,math.sin(angle)*settings.depth*.66*distance,0)
        size=settings.width*rng.uniform(.018,.055)
        params=rock_generator.RockBuildParams("BOULDER",settings.seed+11000+index,size,size*rng.uniform(.65,1.1),size*rng.uniform(.45,.9),5,3,.22,False,0,False)
        vertices,faces=rock_generator._build_rock_geometry(params);batch.append_rock(vertices,faces,location,(1,1,1),rng.uniform(0,math.tau),0)
    return batch


def _remove_existing(asset_id):
    name="WB_ASSET_"+asset_id.replace(".","_")
    collection=bpy.data.collections.get(name)
    if collection:
        for obj in list(collection.objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(collection)
    for obj in list(bpy.data.objects):
        if obj.get("wb_reef_preview_asset_id")==asset_id:bpy.data.objects.remove(obj,do_unlink=True)


def generate(context, settings):
    asset_id=settings.asset_id.strip()
    if not asset_id:raise ValueError("Asset ID is required")
    _remove_existing(asset_id)
    collection=bpy.data.collections.new("WB_ASSET_"+asset_id.replace(".","_")); collection["wb_reef_asset_id"]=asset_id; collection["wb_reef_schema_version"]=SCHEMA_VERSION;collection["wb_reef_asset_kind"]=settings.asset_kind
    root=bpy.data.objects.new("ReefAssetRoot",None);collection.objects.link(root);_tag(root,asset_id,"root")
    if settings.asset_kind in {"COMPLETE","ROCK_ONLY"}:
        specs,anchors=_create_formation(settings,collection,root,asset_id);_create_sand(settings,collection,root,asset_id)
    else:specs,anchors=[],[]
    rng=random.Random(settings.seed+29021);base_anchors=[]
    for i in range(max(8,round(settings.decoration_density*18))):
        angle=math.tau*i/max(8,round(settings.decoration_density*18))+rng.uniform(-.2,.2)
        distance=rng.uniform(.12,1.0) if settings.asset_kind in {"SEAWEED_PATCH","CORAL_PATCH"} else rng.uniform(.82,1.0)
        base_anchors.append((Vector((math.cos(angle)*settings.width*.53*distance,math.sin(angle)*settings.depth*.55*distance,.02)),settings.width*.07))
    all_anchors=anchors+base_anchors;pal=_palette()
    seaweed=_seaweed_batch(settings,all_anchors,rng) if settings.asset_kind in {"COMPLETE","SEAWEED_PATCH"} else MeshBatch()
    if seaweed.faces:
        obj=_mesh_object("ReefSeaweed",seaweed,collection,[pal["kelp_dark"],pal["kelp"],pal["kelp_tip"]],asset_id,"seaweed");obj.parent=root;obj["wb_shader_family"]="SEAWEED";obj["wb_sway_amplitude"]=.18
        if obj.data.vertices:
            minimum=min(vertex.co.z for vertex in obj.data.vertices);maximum=max(vertex.co.z for vertex in obj.data.vertices);span=max(1e-5,maximum-minimum)
            color=obj.data.color_attributes.new("WB_Sway","FLOAT_COLOR","POINT")
            for vertex,item in zip(obj.data.vertices,color.data):
                weight=max(0,min(1,(vertex.co.z-minimum)/span));item.color=(weight,weight,weight,1)
    coral=_coral_batch(settings,all_anchors,rng) if settings.asset_kind in {"COMPLETE","CORAL_PATCH"} else MeshBatch()
    if coral.faces:
        obj=_mesh_object("ReefCoral",coral,collection,[pal["coral_pink"],pal["coral_blue"],pal["coral_yellow"],pal["coral_dark"]],asset_id,"coral");obj.parent=root
    pebbles=_pebble_batch(settings,rng) if settings.asset_kind in {"COMPLETE","ROCK_ONLY"} else MeshBatch()
    if pebbles.faces:
        obj=_mesh_object("ReefPebbles",pebbles,collection,[pal["rock"]],asset_id,"pebbles");obj.parent=root
    try:
        collection.asset_mark();collection.asset_data.description=f"WorldBuilder {settings.asset_kind}: {settings.formation}"
    except (AttributeError,RuntimeError):pass
    preview=bpy.data.objects.new(f"PREVIEW_{collection.name}",None);context.collection.objects.link(preview);preview.instance_type="COLLECTION";preview.instance_collection=collection;preview.location=context.scene.cursor.location;preview["wb_reef_preview_asset_id"]=asset_id;preview["wb_role"]="GLOBAL";_tag(preview,asset_id,"preview")
    library=getattr(context.scene,"worldbuilder_structure_library",None)
    if library is not None:
        category="Environment/Reef" if settings.asset_kind in {"COMPLETE","ROCK_ONLY"} else "Environment/SeaLife"
        library.draft_collection=collection;library.draft_asset_id=asset_id;library.draft_name_ko=settings.name_ko;library.draft_name_en=settings.name_en;library.draft_category=category;library.draft_ownership="REGION" if settings.asset_kind in {"COMPLETE","ROCK_ONLY"} and settings.formation in {"SPIRE","ARCH"} else "CHUNK"
    context.view_layer.objects.active=preview;preview.select_set(True)
    return collection,preview,len(specs)


class WBReefSettings(PropertyGroup):
    asset_kind:EnumProperty(name="Asset Type",items=ASSET_KINDS,default="COMPLETE")
    formation:EnumProperty(name="Formation",items=FORMATIONS,default="TERRACES")
    seed:IntProperty(name="Seed",default=17,min=0,max=999999)
    asset_id:StringProperty(name="Asset ID",default="environment.reef.terraces.01")
    name_ko:StringProperty(name="Korean Name",default="산호초 층상 바위")
    name_en:StringProperty(name="English Name",default="Reef Terraces")
    width:FloatProperty(name="Width",default=12,min=1,max=100,unit="LENGTH")
    depth:FloatProperty(name="Depth",default=9,min=1,max=100,unit="LENGTH")
    height:FloatProperty(name="Height",default=9,min=1,max=100,unit="LENGTH")
    rock_sides:IntProperty(name="Rock Sides",default=7,min=5,max=12)
    rock_levels:IntProperty(name="Facet Levels",default=6,min=3,max=12)
    irregularity:FloatProperty(name="Irregularity",default=.18,min=0,max=.45,subtype="FACTOR")
    sand_base:BoolProperty(name="Sand Base",default=True)
    decoration_density:FloatProperty(name="Decoration Density",default=.72,min=0,max=1,subtype="FACTOR")
    seaweed_density:FloatProperty(name="Seaweed Density",default=.65,min=0,max=1,subtype="FACTOR")
    coral_density:FloatProperty(name="Coral Density",default=.52,min=0,max=1,subtype="FACTOR")
    pebble_count:IntProperty(name="Pebbles",default=36,min=0,max=200)


class WB_OT_generate_reef(Operator):
    bl_idname="worldbuilder.generate_reef_asset";bl_label="Generate Reef Asset";bl_options={"REGISTER","UNDO"}
    def execute(self,context):
        try:collection,preview,count=generate(context,context.scene.worldbuilder_reef)
        except ValueError as error:self.report({"ERROR"},str(error));return {"CANCELLED"}
        self.report({"INFO"},f"Generated {collection.name}: {count} rock modules");return {"FINISHED"}


class WB_OT_randomize_reef(Operator):
    bl_idname="worldbuilder.randomize_reef_seed";bl_label="Randomize Reef Seed";bl_options={"UNDO"}
    def execute(self,context):context.scene.worldbuilder_reef.seed=random.randint(0,999999);return {"FINISHED"}


class WB_OT_generate_reef_sheet(Operator):
    bl_idname="worldbuilder.generate_reef_sheet";bl_label="Generate Reef Variation Sheet";bl_options={"REGISTER","UNDO"}
    def execute(self,context):
        settings=context.scene.worldbuilder_reef;base_id=settings.asset_id.strip() or "environment.reef";base_cursor=context.scene.cursor.location.copy();original=(settings.asset_kind,settings.formation,settings.seed,settings.asset_id,settings.name_en,settings.name_ko);count=0
        try:
            formations=("MOUND","TERRACES","SPIRE","ARCH")
            for index in range(12):
                formation=formations[index%4];row=index//4;column=index%4;settings.formation=formation;settings.seed=original[2]+index*137
                label=formation.lower() if settings.asset_kind in {"COMPLETE","ROCK_ONLY"} else settings.asset_kind.lower()
                settings.asset_id=f"{base_id}.{label}.{index+1:02d}";settings.name_en=f"{settings.asset_kind.replace('_',' ').title()} {index+1:02d}";settings.name_ko=f"해저 에셋 {index+1:02d}";context.scene.cursor.location=base_cursor+Vector(((column-1.5)*settings.width*1.6,-row*settings.depth*1.75,0));generate(context,settings);count+=1
        finally:
            settings.asset_kind,settings.formation,settings.seed,settings.asset_id,settings.name_en,settings.name_ko=original;context.scene.cursor.location=base_cursor
        self.report({"INFO"},f"Generated {count} reef Collection Assets");return {"FINISHED"}


class WB_PT_reef_generator(Panel):
    bl_label="Reef Formation Builder";bl_idname="WB_PT_reef_generator";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="WorldBuilder";bl_parent_id="WB_PT_toolkit_overview";bl_options={"DEFAULT_CLOSED"}
    def draw(self,context):
        layout=self.layout;s=context.scene.worldbuilder_reef;ko=localization.language(context.scene)=="KO"
        layout.label(text="산호초 바위 제작기" if ko else "Reef Formation Builder",icon="OUTLINER_COLLECTION");layout.prop(s,"asset_kind",text="에셋 종류" if ko else "Asset Type");row=layout.row();row.enabled=s.asset_kind in {"COMPLETE","ROCK_ONLY"};row.prop(s,"formation",text="구조" if ko else "Formation")
        row=layout.row(align=True);row.prop(s,"seed",text="시드" if ko else "Seed");row.operator("worldbuilder.randomize_reef_seed",text="",icon="FILE_REFRESH")
        identity=layout.box();identity.label(text="Collection Asset");identity.prop(s,"asset_id");identity.prop(s,"name_ko");identity.prop(s,"name_en")
        shape=layout.box();shape.label(text="형태" if ko else "Shape");shape.prop(s,"width",text="너비" if ko else "Width");shape.prop(s,"depth",text="깊이" if ko else "Depth");shape.prop(s,"height",text="높이" if ko else "Height");shape.prop(s,"irregularity",text="불규칙성" if ko else "Irregularity")
        detail=layout.box();detail.label(text="해초·산호·자갈" if ko else "Sea Life Details");detail.prop(s,"sand_base",text="모래 바닥" if ko else "Sand Base");detail.prop(s,"decoration_density",text="전체 밀도" if ko else "Overall Density");detail.prop(s,"seaweed_density",text="해초 밀도" if ko else "Seaweed Density");detail.prop(s,"coral_density",text="산호 밀도" if ko else "Coral Density");detail.prop(s,"pebble_count",text="자갈 수" if ko else "Pebbles")
        layout.operator("worldbuilder.generate_reef_asset",text="산호초 Collection Asset 생성" if ko else "Generate Reef Collection Asset",icon="MOD_BUILD")
        layout.operator("worldbuilder.generate_reef_sheet",text="12종 변형 시트 생성" if ko else "Generate 12-Asset Variation Sheet",icon="OUTLINER_COLLECTION")
        layout.label(text="생성 후 구조물 라이브러리 등록 항목이 자동 준비됩니다." if ko else "Structure Library registration fields are prepared automatically.",icon="INFO")


CLASSES=(WBReefSettings,WB_OT_generate_reef,WB_OT_randomize_reef,WB_OT_generate_reef_sheet,WB_PT_reef_generator)
def register():
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_reef=PointerProperty(type=WBReefSettings)
def unregister():
    if hasattr(bpy.types.Scene,"worldbuilder_reef"):del bpy.types.Scene.worldbuilder_reef
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
