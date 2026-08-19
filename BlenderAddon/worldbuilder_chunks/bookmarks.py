"""Named viewpoints and direct chunk jumps for large worlds."""

import bpy
from bpy.props import CollectionProperty, FloatProperty, FloatVectorProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList
from mathutils import Vector

from . import contract, layers, localization, overlay, state


class WBBookmark(PropertyGroup):
    name: StringProperty(name="Name", default="Bookmark")
    location: FloatVectorProperty(name="Location", size=3, subtype="TRANSLATION")
    chunk_x: IntProperty(name="Chunk X")
    chunk_z: IntProperty(name="Chunk Z")
    layer_index: IntProperty(name="Layer", min=0)
    view_distance: FloatProperty(name="View Distance", default=40.0, min=0.001)


class WBBookmarkSettings(PropertyGroup):
    items: CollectionProperty(type=WBBookmark)
    active_index: IntProperty(default=0)
    jump_chunk_x: IntProperty(name="Chunk X")
    jump_chunk_z: IntProperty(name="Chunk Z")


def settings(scene):
    return getattr(scene, "worldbuilder_bookmarks", None)


def active(scene):
    value = settings(scene)
    if value is None or not value.items or value.active_index >= len(value.items):
        return None
    return value.items[value.active_index]


def _view(context):
    space = context.space_data
    return space.region_3d if space is not None and space.type == "VIEW_3D" else None


def focus_point(context, point, distance=None) -> bool:
    """Move the viewport and 3D cursor to a world point without changing the camera angle."""
    region = _view(context)
    context.scene.cursor.location = Vector(point)
    if region is None:
        return False
    region.view_location = Vector(point)
    if distance is not None:
        region.view_distance = max(0.001, float(distance))
    overlay.invalidate_all()
    return True


def go_to_chunk(context, coordinate) -> None:
    grid = context.scene.worldbuilder_chunks
    center_x, center_y = contract.chunk_center_xy(coordinate, grid.origin_x, grid.origin_y, grid.chunk_size)
    z = layers.active_floor_z(grid)
    state.set_active_chunk(grid, coordinate)
    focus_point(context, Vector((center_x, center_y, z)), grid.chunk_size)


class WB_UL_bookmarks(UIList):
    def draw_item(self, context, layout, _data, item, _icon, _active, _prop, _index):
        layout.label(text=item.name, icon="BOOKMARKS")
        layout.label(text=f"{contract.chunk_name((item.chunk_x, item.chunk_z))} {contract.layer_name(item.layer_index)}")


class WB_OT_bookmark_add(Operator):
    bl_idname = "worldbuilder.bookmark_add"
    bl_label = "Save Current View"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        grid = context.scene.worldbuilder_chunks
        region = _view(context)
        point = Vector(region.view_location) if region is not None else context.scene.cursor.location.copy()
        item = value.items.add()
        coordinate = contract.chunk_coord_from_xy(point.x, point.y, grid.origin_x, grid.origin_y, grid.chunk_size)
        item.name = contract.chunk_name(coordinate)
        item.location = point
        item.chunk_x, item.chunk_z = coordinate
        item.layer_index = contract.clamp_layer(
            contract.layer_index_for_z(point.z, grid.layer_base_z, grid.layer_height), grid.layer_count)
        item.view_distance = region.view_distance if region is not None else grid.chunk_size
        value.active_index = len(value.items) - 1
        self.report({"INFO"}, f"Saved {item.name}")
        return {"FINISHED"}


class WB_OT_bookmark_remove(Operator):
    bl_idname = "worldbuilder.bookmark_remove"
    bl_label = "Remove Bookmark"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is None or not value.items:
            return {"CANCELLED"}
        value.items.remove(value.active_index)
        value.active_index = max(0, min(value.active_index, len(value.items) - 1))
        return {"FINISHED"}


class WB_OT_bookmark_jump(Operator):
    bl_idname = "worldbuilder.bookmark_jump"
    bl_label = "Jump to Bookmark"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        item = active(context.scene)
        if item is None:
            self.report({"ERROR"}, "No bookmark selected")
            return {"CANCELLED"}
        grid = context.scene.worldbuilder_chunks
        layers.set_active_layer(context.scene, item.layer_index)
        state.set_active_chunk(grid, (item.chunk_x, item.chunk_z))
        focus_point(context, Vector(item.location), item.view_distance)
        self.report({"INFO"}, f"{item.name} · {contract.layer_name(item.layer_index)}")
        return {"FINISHED"}


class WB_OT_bookmark_update(Operator):
    bl_idname = "worldbuilder.bookmark_update"
    bl_label = "Update to Current View"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        item = active(context.scene)
        region = _view(context)
        if item is None or region is None:
            return {"CANCELLED"}
        grid = context.scene.worldbuilder_chunks
        point = Vector(region.view_location)
        item.location = point
        item.chunk_x, item.chunk_z = contract.chunk_coord_from_xy(
            point.x, point.y, grid.origin_x, grid.origin_y, grid.chunk_size)
        item.layer_index = grid.active_layer
        item.view_distance = region.view_distance
        return {"FINISHED"}


class WB_OT_goto_chunk(Operator):
    bl_idname = "worldbuilder.goto_chunk"
    bl_label = "Go To Chunk"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        value = settings(context.scene)
        if value is None:
            return {"CANCELLED"}
        coordinate = (value.jump_chunk_x, value.jump_chunk_z)
        go_to_chunk(context, coordinate)
        self.report({"INFO"}, contract.chunk_name(coordinate))
        return {"FINISHED"}


class WB_PT_bookmarks(Panel):
    bl_label = "Bookmarks"
    bl_idname = "WB_PT_bookmarks"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        value = settings(context.scene)
        if value is None:
            return
        translate = lambda key: localization.tr(key, context.scene)

        box = layout.box()
        row = box.row(align=True)
        row.prop(value, "jump_chunk_x")
        row.prop(value, "jump_chunk_z")
        box.operator("worldbuilder.goto_chunk", text=translate("goto_chunk"), icon="VIEW_PAN")

        layout.template_list("WB_UL_bookmarks", "", value, "items", value, "active_index", rows=4)
        row = layout.row(align=True)
        row.operator("worldbuilder.bookmark_add", text=translate("bookmark_add"), icon="ADD")
        row.operator("worldbuilder.bookmark_remove", text="", icon="REMOVE")
        item = active(context.scene)
        if item is None:
            return
        layout.prop(item, "name", text=translate("bookmark_name"))
        row = layout.row(align=True)
        row.operator("worldbuilder.bookmark_jump", text=translate("bookmark_jump"), icon="VIEW_CAMERA")
        row.operator("worldbuilder.bookmark_update", text=translate("bookmark_update"))


CLASSES = (WBBookmark, WBBookmarkSettings, WB_UL_bookmarks, WB_OT_bookmark_add, WB_OT_bookmark_remove,
           WB_OT_bookmark_jump, WB_OT_bookmark_update, WB_OT_goto_chunk, WB_PT_bookmarks)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_bookmarks = PointerProperty(type=WBBookmarkSettings)


def unregister():
    if hasattr(bpy.types.Scene, "worldbuilder_bookmarks"):
        del bpy.types.Scene.worldbuilder_bookmarks
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
