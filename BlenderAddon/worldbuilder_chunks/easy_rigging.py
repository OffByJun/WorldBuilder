import math
import uuid
from contextlib import contextmanager

import bpy
from bpy.props import BoolProperty, CollectionProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from bpy_extras.io_utils import ExportHelper

from . import easy_rigging_core as core, localization


OWNER_KEY = "wb_easy_rig_owner"
KIND_KEY = "wb_easy_rig_kind"
_registered = []
_property_registered = False


def settings(scene):
    return getattr(scene, "worldbuilder_easy_rigging", None)


def _owned(obj, value, kind):
    return (obj is not None and bool(value.owner_id)
            and obj.get(OWNER_KEY) == value.owner_id and obj.get(KIND_KEY) == kind)


def _parent_name_get(self):
    value = settings(self.id_data)
    if value and 0 <= self.parent_index < len(value.joints):
        return value.joints[self.parent_index].name
    return "" if self.parent_index == -1 else "<Missing parent>"


def _parent_name_set(self, name):
    value = settings(self.id_data)
    self.parent_index = -1 if not name else next(
        (index for index, item in enumerate(value.joints) if item.name == name), len(value.joints))


def _name_changed(self, context):
    value = settings(self.id_data)
    if value and _owned(self.guide, value, "GUIDE"):
        self.guide.name = f"WBGuide_{self.name}"


def _mesh_poll(self, obj):
    return obj.type == "MESH"


class WBEasyRigJoint(PropertyGroup):
    name: StringProperty(name="Joint", default="Joint", update=_name_changed)
    guide: PointerProperty(type=bpy.types.Object)
    parent_index: IntProperty(name="Parent Index (-1 = root)", default=-1, min=-1)
    parent_name: StringProperty(name="Parent Joint", get=_parent_name_get, set=_parent_name_set)


class WBEasyRigSettings(PropertyGroup):
    target: PointerProperty(name="Mesh", type=bpy.types.Object, poll=_mesh_poll)
    armature: PointerProperty(name="Built Rig", type=bpy.types.Object)
    rig_target: PointerProperty(type=bpy.types.Object)
    joints: CollectionProperty(type=WBEasyRigJoint)
    active_index: IntProperty(default=0, min=0)
    owner_id: StringProperty(options={"HIDDEN"})


def _live(context, obj):
    try:
        return obj is not None and context.view_layer.objects.get(obj.name) == obj
    except ReferenceError:
        return False


def _object_mode(context):
    if context.object is not None and context.object.mode != "OBJECT":
        if bpy.ops.object.mode_set(mode="OBJECT") != {"FINISHED"}:
            raise ValueError("Switch to Object Mode first")


def _select_only(context, objects, active):
    for obj in context.view_layer.objects:
        if obj.select_get():
            obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
        if not obj.select_get():
            raise ValueError(f"Make {obj.name} selectable first")
    context.view_layer.objects.active = active


@contextmanager
def preserve_context(context, restore_on_success=True):
    active = context.view_layer.objects.active
    mode = active.mode if active else "OBJECT"
    selected = [obj for obj in context.view_layer.objects if obj.select_get()]
    completed = False
    try:
        _object_mode(context)
        yield
        completed = True
    finally:
        if restore_on_success or not completed:
            _object_mode(context)
            _select_only(context, [obj for obj in selected if _live(context, obj)],
                         active if _live(context, active) else None)
            if mode != "OBJECT" and _live(context, active):
                if bpy.ops.object.mode_set(mode=mode) != {"FINISHED"}:
                    raise RuntimeError(f"Could not restore {mode} mode")


def _validate_object(context, obj, kind):
    if obj is None or obj.type != kind or not _live(context, obj):
        raise ValueError(f"Choose a {kind.lower()} in the current view layer")
    if obj.library or obj.override_library or (obj.data and (obj.data.library or obj.data.override_library)):
        raise ValueError("Use local editable objects, not linked data or overrides")
    if not obj.visible_get(view_layer=context.view_layer) or obj.hide_select:
        raise ValueError(f"Unhide and unlock {obj.name} first")
    matrix = obj.matrix_world
    if not all(math.isfinite(component) for row in matrix for component in row):
        raise ValueError(f"Invalid transform: {obj.name}")
    determinant = matrix.to_3x3().determinant()
    if not math.isfinite(determinant) or abs(determinant) <= 1e-12:
        raise ValueError(f"Singular transform: {obj.name}")
    if kind in {"MESH", "ARMATURE"}:
        axes = [matrix.to_3x3().col[index] for index in range(3)]
        if (determinant < 0 or any(abs(axis.length - 1.0) > 1e-4 for axis in axes)
                or any(abs(axes[a].dot(axes[b])) > 1e-4 for a, b in ((0, 1), (0, 2), (1, 2)))):
            raise ValueError(f"Apply scale and remove shear/reflection on {obj.name} first")
    return obj


def _target(context, value):
    target = _validate_object(context, value.target, "MESH")
    if not target.data.vertices or not target.data.polygons:
        raise ValueError("Target must contain a mesh surface")
    for vertex in target.data.vertices:
        core.validate_point(vertex.co)
        core.validate_point(target.matrix_world @ vertex.co)
    return target


def _rig(context, value):
    rig = _validate_object(context, value.armature, "ARMATURE")
    if not _owned(rig, value, "ARMATURE") or value.rig_target != value.target:
        raise ValueError("Select the original mesh and its Easy Rig armature")
    if not rig.data.bones:
        raise ValueError("Armature has no bones")
    return rig


def _mark_owned(obj, value, kind):
    obj[OWNER_KEY] = value.owner_id
    obj[KIND_KEY] = kind
    obj["wb_role"] = "GLOBAL"
    if hasattr(obj, "worldbuilder_chunk"):
        obj.worldbuilder_chunk.role = "GLOBAL"


def _active_joint(value):
    if not 0 <= value.active_index < len(value.joints):
        raise ValueError("Select a joint")
    return value.joints[value.active_index]


def add_joint(context):
    value = settings(context.scene)
    if context.mode != "OBJECT":
        raise ValueError("Switch to Object Mode to place guides")
    point = core.validate_point(context.scene.cursor.location)
    if not value.owner_id:
        value.owner_id = uuid.uuid4().hex
    index = len(value.joints)
    number = index
    while any(item.name == f"Joint_{number:03d}" for item in value.joints):
        number += 1
    guide = bpy.data.objects.new(f"WBGuide_Joint_{number:03d}", None)
    try:
        context.scene.collection.objects.link(guide)
        _mark_owned(guide, value, "GUIDE")
        guide.empty_display_type = "SPHERE"
        guide.empty_display_size = 0.08
        guide.show_in_front = True
        guide.show_name = True
        guide.hide_render = True
        guide.location = point
        item = value.joints.add()
        item.guide = guide
        item.name = f"Joint_{number:03d}"
        item.parent_index = value.active_index if index and value.active_index < index else index - 1
        value.active_index = index
        _select_only(context, [guide], guide)
    except Exception:
        if len(value.joints) > index:
            value.joints.remove(index)
        bpy.data.objects.remove(guide, do_unlink=True)
        raise
    return guide


def _check_guide_deletion(context, value, guide):
    if guide is None:
        return
    if not _owned(guide, value, "GUIDE") or guide.type != "EMPTY":
        raise ValueError("Guide is not owned by this Easy Rig setup")
    if guide.library or guide.override_library or guide.children or len(guide.users_scene) > 1:
        raise ValueError("Guide is linked, shared, or has children; detach it manually first")
    for scene in bpy.data.scenes:
        other = settings(scene)
        if other and scene != context.scene and any(item.guide == guide for item in other.joints):
            raise ValueError("Guide is referenced by another scene")


def remove_joint(context):
    value = settings(context.scene)
    item = _active_joint(value)
    parents = core.parents_after_removal([joint.parent_index for joint in value.joints], value.active_index)
    guide = item.guide
    _check_guide_deletion(context, value, guide)
    if guide is not None and sum(joint.guide == guide for joint in value.joints) != 1:
        raise ValueError("Duplicate guide reference; fix joint pointers first")
    with preserve_context(context):
        if guide is not None:
            bpy.data.objects.remove(guide, do_unlink=True)
        value.joints.remove(value.active_index)
        for joint, parent in zip(value.joints, parents):
            joint.parent_index = parent
        value.active_index = max(0, min(value.active_index, len(value.joints) - 1))


def reset_guides(context):
    value = settings(context.scene)
    guides = {item.guide for item in value.joints if item.guide is not None}
    for guide in guides:
        _check_guide_deletion(context, value, guide)
    with preserve_context(context):
        for guide in guides:
            bpy.data.objects.remove(guide, do_unlink=True)
        value.joints.clear()
        value.active_index = 0


def build_armature(context):
    value = settings(context.scene)
    if value.armature is not None or any(_owned(obj, value, "ARMATURE") for obj in bpy.data.objects):
        raise ValueError("Rig already exists; manually remove the old armature before rebuilding")
    rig = None
    data = None
    with preserve_context(context):
        context.view_layer.update()
        target = _target(context, value)
        if target.parent or any(mod.type == "ARMATURE" for mod in target.modifiers):
            raise ValueError("Target already has a parent or rig; use a separate unrigged mesh")
        guides = [item.guide for item in value.joints]
        if len(set(guides)) != len(guides):
            raise ValueError("Duplicate guide references")
        for guide in guides:
            _validate_object(context, guide, "EMPTY")
            if not _owned(guide, value, "GUIDE"):
                raise ValueError("Missing or unowned joint guide")
        plan = core.bone_plan([item.name for item in value.joints],
                              [item.parent_index for item in value.joints],
                              [guide.matrix_world.translation for guide in guides])
        try:
            data = bpy.data.armatures.new(f"WB_Rig_{target.name}")
            rig = bpy.data.objects.new(data.name, data)
            context.scene.collection.objects.link(rig)
            _mark_owned(rig, value, "ARMATURE")
            rig.show_in_front = True
            data.display_type = "OCTAHEDRAL"
            _select_only(context, [rig], rig)
            if bpy.ops.object.mode_set(mode="EDIT") != {"FINISHED"}:
                raise RuntimeError("Cannot edit the new armature")
            bones = {}
            for edge in plan:
                bone = data.edit_bones.new(edge["name"])
                bone.head = edge["head"]
                bone.tail = edge["tail"]
                if edge["parent"] is not None:
                    bone.parent = bones[edge["parent"]]
                    bone.use_connect = True
                bones[edge["name"]] = bone
            _object_mode(context)
            if len(data.bones) != len(plan):
                raise ValueError("Blender discarded a bone; move joints farther apart")
            value.armature = rig
            value.rig_target = target
        except Exception:
            _object_mode(context)
            if rig is not None:
                bpy.data.objects.remove(rig, do_unlink=True)
            if data is not None and data.users == 0:
                bpy.data.armatures.remove(data)
            value.armature = None
            raise
    return rig


def inspect_weights(target, rig):
    names = {group.index: group.name for group in target.vertex_groups}
    core.validate_weights(
        ([(names.get(item.group, ""), item.weight) for item in vertex.groups]
         for vertex in target.data.vertices),
        [bone.name for bone in rig.data.bones if bone.use_deform])


def bind_automatic(context):
    value = settings(context.scene)
    with preserve_context(context):
        context.view_layer.update()
        target = _target(context, value)
        rig = _rig(context, value)
        if target.vertex_groups or target.parent or any(mod.type == "ARMATURE" for mod in target.modifiers):
            raise ValueError("Existing groups, weights, parents and rigs are preserved; bind a clean mesh")
        if target.data.users != 1 or target.constraints or target.animation_data:
            raise ValueError("Bind requires single-user mesh data without constraints or object animation")
        if rig.parent or rig.constraints or rig.animation_data or rig.data.pose_position != "REST":
            if rig.parent or rig.constraints or rig.animation_data or any(
                    any(abs(bone.matrix_basis[row][column] - (1.0 if row == column else 0.0)) > 1e-5
                        for row in range(4) for column in range(4)) for bone in rig.pose.bones):
                raise ValueError("Bind the armature in its rest pose before adding animation")
        modifiers = {mod.as_pointer() for mod in target.modifiers}
        groups = {group.name for group in target.vertex_groups}
        parent = target.parent
        parent_type = target.parent_type
        parent_bone = target.parent_bone
        inverse = target.matrix_parent_inverse.copy()
        basis = target.matrix_basis.copy()
        world = target.matrix_world.copy()
        try:
            _select_only(context, [target, rig], rig)
            result = bpy.ops.object.parent_set(type="ARMATURE_AUTO")
            context.view_layer.update()
            if result != {"FINISHED"}:
                raise ValueError("Automatic weights cancelled")
            added = [mod for mod in target.modifiers if mod.as_pointer() not in modifiers]
            if target.parent != rig or not any(mod.type == "ARMATURE" and mod.object == rig for mod in added):
                raise ValueError("Automatic binding did not create the expected rig link")
            inspect_weights(target, rig)
            if any(abs(target.matrix_world[row][column] - world[row][column]) > 1e-4
                   for row in range(4) for column in range(4)):
                raise ValueError("Automatic binding changed the mesh transform")
        except Exception:
            for mod in list(target.modifiers):
                if mod.as_pointer() not in modifiers:
                    target.modifiers.remove(mod)
            for group in list(target.vertex_groups):
                if group.name not in groups:
                    target.vertex_groups.remove(group)
            target.parent = parent
            target.parent_type = parent_type
            target.parent_bone = parent_bone
            target.matrix_parent_inverse = inverse
            target.matrix_basis = basis
            context.view_layer.update()
            raise
    return rig


def pose_rig(context):
    value = settings(context.scene)
    rig = _rig(context, value)
    with preserve_context(context, restore_on_success=False):
        _select_only(context, [rig], rig)
        if bpy.ops.object.mode_set(mode="POSE") != {"FINISHED"}:
            raise ValueError("Cannot enter Pose Mode")
    return rig


def export_fbx(context, filepath, bake_anim=False):
    path = core.validate_export_path(bpy.path.abspath(filepath))
    value = settings(context.scene)
    with preserve_context(context):
        context.view_layer.update()
        target = _target(context, value)
        rig = _rig(context, value)
        modifiers = [mod for mod in target.modifiers if mod.type == "ARMATURE"]
        if (len(modifiers) != 1 or modifiers[0].object != rig or not modifiers[0].show_viewport
                or not modifiers[0].show_render or target.parent not in (None, rig) or rig.parent):
            raise ValueError("Bind the mesh to only its Easy Rig armature before export")
        inspect_weights(target, rig)
        _select_only(context, [target, rig], rig)
        result = bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, use_visible=False, use_active_collection=False,
            object_types={"MESH", "ARMATURE"}, add_leaf_bones=False,
            axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS", use_mesh_modifiers=True,
            use_armature_deform_only=True, bake_anim=bool(bake_anim),
            bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
            use_custom_props=False)
        if result != {"FINISHED"}:
            raise ValueError("FBX export cancelled")
    return path


class _EasyRigAction:
    bl_options = {"REGISTER", "UNDO"}
    action = None

    def execute(self, context):
        try:
            self.action(context)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        return {"FINISHED"}


class WB_OT_easy_rig_add_joint(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_add_joint"
    bl_label = "Add Joint at Cursor / 커서에 관절"
    action = staticmethod(add_joint)


class WB_OT_easy_rig_remove_joint(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_remove_joint"
    bl_label = "Delete Joint / 관절 삭제"
    action = staticmethod(remove_joint)


class WB_OT_easy_rig_reset_guides(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_reset_guides"
    bl_label = "Reset Guides Only / 가이드만 초기화"
    action = staticmethod(reset_guides)

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)


class WB_OT_easy_rig_select_joint(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_select_joint"
    bl_label = "Select Guide / 가이드 선택"

    @staticmethod
    def action(context):
        value = settings(context.scene)
        guide = _validate_object(context, _active_joint(value).guide, "EMPTY")
        if not _owned(guide, value, "GUIDE"):
            raise ValueError("Guide is not owned by this setup")
        _object_mode(context)
        _select_only(context, [guide], guide)


class WB_OT_easy_rig_build(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_build"
    bl_label = "Build Rig / 뼈대 생성"
    action = staticmethod(build_armature)


class WB_OT_easy_rig_bind(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_bind"
    bl_label = "Bind Auto Weights / 자동 웨이트"
    action = staticmethod(bind_automatic)


class WB_OT_easy_rig_pose(_EasyRigAction, Operator):
    bl_idname = "worldbuilder.easy_rig_pose"
    bl_label = "Pose / 포즈"
    action = staticmethod(pose_rig)


class WB_OT_easy_rig_export(Operator, ExportHelper):
    bl_idname = "worldbuilder.easy_rig_export"
    bl_label = "Unity Generic FBX"
    filename_ext = ".fbx"
    filter_glob: StringProperty(default="*.fbx", options={"HIDDEN"})
    bake_anim: BoolProperty(name="Animation / 애니메이션", default=False)

    def execute(self, context):
        try:
            export_fbx(context, self.filepath, self.bake_anim)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, "FBX exported; Unity Rig > Animation Type: Generic")
        return {"FINISHED"}


class WB_UL_easy_rig_joints(UIList):
    def draw_item(self, context, layout, _data, item, _icon, _active, _prop, index):
        layout.label(text=f"{index}: {item.name}", icon="EMPTY_AXIS" if item.guide else "ERROR")
        layout.label(text=f"< {item.parent_index}")


class WB_PT_easy_rigging(Panel):
    bl_label = "Easy Rig / 간편 리깅"
    bl_idname = "WB_PT_easy_rigging"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        value = settings(context.scene)
        if value is None:
            return
        ko = localization.language(context.scene) == "ko"
        layout.prop(value, "target", text="1. 대상 메시" if ko else "1. Mesh")
        layout.label(text="2. 커서에 관절 추가 후 G로 이동" if ko else "2. Add at cursor; G to move guides")
        layout.template_list("WB_UL_easy_rig_joints", "", value, "joints", value, "active_index", rows=4)
        row = layout.row(align=True)
        row.operator("worldbuilder.easy_rig_add_joint", text="추가" if ko else "Add", icon="ADD")
        row.operator("worldbuilder.easy_rig_remove_joint", text="삭제" if ko else "Delete", icon="REMOVE")
        if 0 <= value.active_index < len(value.joints):
            item = value.joints[value.active_index]
            layout.prop(item, "name", text="이름" if ko else "Name")
            layout.prop_search(item, "parent_name", value, "joints", text="부모 관절" if ko else "Parent Joint")
            layout.prop(item, "parent_index", text="부모 번호 (-1: 루트)" if ko else "Parent Index (-1: root)")
            layout.operator("worldbuilder.easy_rig_select_joint", text="가이드 선택" if ko else "Select Guide")
        layout.operator("worldbuilder.easy_rig_reset_guides", text="가이드만 초기화..." if ko else "Reset Guides Only...")
        layout.operator("worldbuilder.easy_rig_build", text="3. 뼈대 생성" if ko else "3. Build Rig")
        if value.armature:
            layout.label(text=value.armature.name, icon="ARMATURE_DATA")
            layout.label(text="재생성: 기존 뼈대를 직접 삭제" if ko else "Rebuild: manually delete old rig")
        layout.operator("worldbuilder.easy_rig_bind", text="4. 자동 웨이트 연결" if ko else "4. Bind Auto Weights")
        layout.label(text="기존 리그/웨이트는 변경하지 않음" if ko else "Existing rigs/weights are never replaced")
        layout.operator("worldbuilder.easy_rig_pose", text="5. 포즈" if ko else "5. Pose")
        layout.operator("worldbuilder.easy_rig_export", text="6. Unity Generic FBX", icon="EXPORT")
        layout.label(text="Unity: Rig > Generic", icon="INFO")


CLASSES = (WBEasyRigJoint, WBEasyRigSettings, WB_UL_easy_rig_joints,
           WB_OT_easy_rig_add_joint, WB_OT_easy_rig_remove_joint, WB_OT_easy_rig_reset_guides,
           WB_OT_easy_rig_select_joint, WB_OT_easy_rig_build, WB_OT_easy_rig_bind,
           WB_OT_easy_rig_pose, WB_OT_easy_rig_export, WB_PT_easy_rigging)


def register():
    global _property_registered
    if _registered:
        return
    if hasattr(bpy.types.Scene, "worldbuilder_easy_rigging"):
        raise RuntimeError("Easy Rig settings are already registered")
    try:
        for cls in CLASSES:
            bpy.utils.register_class(cls)
            _registered.append(cls)
        bpy.types.Scene.worldbuilder_easy_rigging = PointerProperty(type=WBEasyRigSettings)
        _property_registered = True
    except Exception:
        unregister()
        raise


def unregister():
    global _property_registered
    if _property_registered and hasattr(bpy.types.Scene, "worldbuilder_easy_rigging"):
        del bpy.types.Scene.worldbuilder_easy_rigging
    _property_registered = False
    for cls in reversed(_registered):
        bpy.utils.unregister_class(cls)
    _registered.clear()
