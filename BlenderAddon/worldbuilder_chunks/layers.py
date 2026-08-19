"""Authoring-only vertical layers for tall worlds.

Layers slice Blender Z into uniform bands so vertical navigation, isolation, and
snapping stay cheap. They never change chunk ownership: Unity keeps its 2D chunk
grid and only receives the layer index as placement metadata.
"""

import bpy
from bpy.types import Operator, Panel
from mathutils import Vector

from . import contract, exporter, localization, overlay

HIDDEN_MARKER = "wb_layer_hidden"


def settings(scene):
    return getattr(scene, "worldbuilder_chunks", None)


def active_floor_z(value) -> float:
    if value is None:
        return 0.0
    return contract.layer_floor_z(value.active_layer, value.layer_base_z, value.layer_height)


def active_bounds_z(value) -> tuple[float, float]:
    if value is None:
        return 0.0, 0.0
    return contract.layer_bounds_z(value.active_layer, value.layer_base_z, value.layer_height)


def object_layer(obj, value) -> int:
    return exporter.object_layer(obj, value)


def clamp_to_active_layer(location, value) -> Vector:
    minimum, maximum = active_bounds_z(value)
    return Vector((location.x, location.y, min(max(location.z, minimum), maximum)))


def visible_in_isolation(index: int, value) -> bool:
    mode = value.layer_isolate
    if mode == "OFF":
        return True
    if mode == "ACTIVE":
        return index == value.active_layer
    return index <= value.active_layer


def apply_isolation(scene) -> int:
    value = settings(scene)
    if value is None:
        return 0
    changed = 0
    for obj in scene.objects:
        if exporter.object_role(obj) == "GLOBAL":
            continue
        visible = visible_in_isolation(object_layer(obj, value), value)
        if visible:
            if obj.get(HIDDEN_MARKER, False):
                del obj[HIDDEN_MARKER]
                obj.hide_viewport = False
                changed += 1
            continue
        if not obj.hide_viewport:
            obj.hide_viewport = True
            obj[HIDDEN_MARKER] = True
            changed += 1
    overlay.invalidate_all()
    return changed


def release_isolation(scene) -> int:
    changed = 0
    for obj in scene.objects:
        if obj.get(HIDDEN_MARKER, False):
            del obj[HIDDEN_MARKER]
            obj.hide_viewport = False
            changed += 1
    overlay.invalidate_all()
    return changed


def set_active_layer(scene, index: int) -> int:
    value = settings(scene)
    if value is None:
        return 0
    value.active_layer = contract.clamp_layer(index, value.layer_count)
    if value.layer_isolate != "OFF":
        apply_isolation(scene)
    overlay.invalidate_all()
    return value.active_layer


class WB_OT_layer_step(Operator):
    bl_idname = "worldbuilder.layer_step"
    bl_label = "Step Layer"
    bl_options = {"REGISTER", "UNDO"}

    delta: bpy.props.IntProperty(default=1)
    move_cursor: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        index = set_active_layer(context.scene, value.active_layer + self.delta)
        if self.move_cursor:
            floor, ceiling = contract.layer_bounds_z(
                index, value.layer_base_z, value.layer_height
            )
            cursor = context.scene.cursor.location
            context.scene.cursor.location = Vector(
                (cursor.x, cursor.y, (floor + ceiling) * 0.5)
            )
        self.report({"INFO"}, contract.layer_name(index))
        return {"FINISHED"}


class WB_OT_layer_set_from_selection(Operator):
    bl_idname = "worldbuilder.layer_set_from_selection"
    bl_label = "Layer from Active Object"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is None or context.object is None:
            return {"CANCELLED"}
        index = set_active_layer(context.scene, object_layer(context.object, value))
        self.report({"INFO"}, contract.layer_name(index))
        return {"FINISHED"}


class WB_OT_layer_isolate(Operator):
    bl_idname = "worldbuilder.layer_isolate"
    bl_label = "Apply Layer Isolation"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        changed = apply_isolation(context.scene)
        self.report({"INFO"}, f"{changed} objects updated")
        return {"FINISHED"}


class WB_OT_layer_show_all(Operator):
    bl_idname = "worldbuilder.layer_show_all"
    bl_label = "Show All Layers"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is not None:
            value.layer_isolate = "OFF"
        changed = release_isolation(context.scene)
        self.report({"INFO"}, f"{changed} objects restored")
        return {"FINISHED"}


class WB_OT_layer_snap_selection(Operator):
    bl_idname = "worldbuilder.layer_snap_selection"
    bl_label = "Snap Selection to Layer Floor"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        floor = active_floor_z(value)
        count = 0
        for obj in context.selected_objects:
            if obj.parent is not None:
                continue
            obj.location.z += floor - obj.matrix_world.translation.z
            overlay.mark_object_dirty(obj, context.scene)
            count += 1
        self.report({"INFO"}, f"{count} objects snapped to {contract.layer_name(value.active_layer)}")
        return {"FINISHED"}


class WB_OT_layer_move_selection(Operator):
    bl_idname = "worldbuilder.layer_move_selection"
    bl_label = "Move Selection by Layer"
    bl_options = {"REGISTER", "UNDO"}

    delta: bpy.props.IntProperty(default=1)

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        offset = self.delta * value.layer_height
        count = 0
        for obj in context.selected_objects:
            if obj.parent is not None:
                continue
            obj.location.z += offset
            overlay.mark_object_dirty(obj, context.scene)
            count += 1
        if value.layer_isolate != "OFF":
            apply_isolation(context.scene)
        self.report({"INFO"}, f"{count} objects moved {self.delta:+d} layer")
        return {"FINISHED"}


class WB_OT_layer_frame(Operator):
    bl_idname = "worldbuilder.layer_frame"
    bl_label = "Frame Active Layer"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        space = context.space_data
        if value is None or space is None or space.type != "VIEW_3D":
            return {"CANCELLED"}
        floor, ceiling = active_bounds_z(value)
        region = space.region_3d
        region.view_location = Vector(
            (region.view_location.x, region.view_location.y, (floor + ceiling) * 0.5)
        )
        context.scene.cursor.location.z = floor
        overlay.invalidate_all()
        return {"FINISHED"}


class WB_PT_layers(Panel):
    bl_label = "Vertical Layers"
    bl_idname = "WB_PT_vertical_layers"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"

    def draw(self, context):
        layout = self.layout
        value = settings(context.scene)
        if value is None:
            return
        translate = lambda key: localization.tr(key, context.scene)

        column = layout.column(align=True)
        column.prop(value, "layer_height", text=translate("layer_height"))
        column.prop(value, "layer_base_z", text=translate("layer_base"))
        column.prop(value, "layer_count", text=translate("layer_count"))

        floor, ceiling = active_bounds_z(value)
        box = layout.box()
        box.label(
            text=f"{contract.layer_name(value.active_layer)}  Z {floor:.2f} .. {ceiling:.2f}",
            icon="MOD_ARRAY",
        )
        row = box.row(align=True)
        row.operator("worldbuilder.layer_step", text="", icon="TRIA_DOWN").delta = -1
        row.prop(value, "active_layer", text=translate("active_layer_index"))
        row.operator("worldbuilder.layer_step", text="", icon="TRIA_UP").delta = 1
        row = box.row(align=True)
        row.operator("worldbuilder.layer_set_from_selection", text=translate("layer_from_object"))
        row.operator("worldbuilder.layer_frame", text=translate("layer_frame"))

        column = layout.column(align=True)
        column.prop(value, "layer_isolate", text=translate("layer_isolate"))
        row = column.row(align=True)
        row.operator("worldbuilder.layer_isolate", text=translate("layer_apply_isolate"))
        row.operator("worldbuilder.layer_show_all", text=translate("layer_show_all"))

        column = layout.column(align=True)
        column.label(text=translate("layer_selection"))
        row = column.row(align=True)
        row.operator("worldbuilder.layer_move_selection", text="", icon="TRIA_DOWN").delta = -1
        row.operator("worldbuilder.layer_snap_selection", text=translate("layer_snap"))
        row.operator("worldbuilder.layer_move_selection", text="", icon="TRIA_UP").delta = 1

        column = layout.column(align=True)
        column.prop(value, "layer_follow_grid", text=translate("layer_follow_grid"))
        column.prop(value, "show_layer_bands", text=translate("layer_bands"))
        column.prop(value, "layer_lock_placement", text=translate("layer_lock"))


CLASSES = (
    WB_OT_layer_step,
    WB_OT_layer_set_from_selection,
    WB_OT_layer_isolate,
    WB_OT_layer_show_all,
    WB_OT_layer_snap_selection,
    WB_OT_layer_move_selection,
    WB_OT_layer_frame,
    WB_PT_layers,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
