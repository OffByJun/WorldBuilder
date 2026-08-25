bl_info = {
    "name": "WorldBuilder Toolkit",
    "author": "Emiteat / Nex EngineWorks",
    "version": (1, 9, 0),
    "blender": (4, 3, 0),
    "location": "View3D > Sidebar > WorldBuilder",
    "description": "Unity chunk workflow plus stylized terrain and underwater rock generation",
    "category": "Import-Export",
}

import os

import bpy
from bpy.app.handlers import persistent
from bpy.props import (
    BoolProperty,
    EnumProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup
from bpy_extras import view3d_utils
from mathutils import Vector

from . import (
    asset_library,
    bake,
    bookmarks,
    biome_painter,
    biome_ui,
    cave_generator,
    chunk_terrain,
    contract,
    entity_catalog,
    exporter,
    finishing_tools,
    layers,
    localization,
    overlay,
    profile,
    reef_generator,
    scatter,
    sculpt_session,
    seam_ui,
    spline_authoring,
    state,
    rock_generator,
    terrain_toolkit,
    traversal,
    water,
)

_feature_modules = (entity_catalog, layers, water, bookmarks, traversal, biome_painter, chunk_terrain, cave_generator, sculpt_session, asset_library, reef_generator, scatter, spline_authoring, seam_ui, bake, finishing_tools)


def _settings(context):
    return context.scene.worldbuilder_chunks


def _resolved_profile_path(settings) -> str:
    return bpy.path.abspath(settings.profile_path) if settings.profile_path else ""


def _grid_value_changed(self, context):
    if not profile.is_applying():
        profile.mark_modified(self)
    overlay.invalidate_all()


def _display_changed(self, context):
    overlay.invalidate_all()


def _profile_path_changed(self, context):
    if not profile.is_applying():
        self.profile_status = "INVALID"
        self.profile_message = "Press Reload Profile to validate and synchronize"
    overlay.invalidate_all()


def _layer_changed(self, context):
    scene = getattr(context, "scene", None)
    if scene is not None:
        if self.layer_isolate == "OFF":
            layers.release_isolation(scene)
        else:
            layers.apply_isolation(scene)
    overlay.invalidate_all()


def _entity_pick_changed(self, context):
    scene = getattr(context, "scene", None)
    if scene is not None and entity_catalog.apply_pick(self, scene, self.entity_catalog_pick):
        _object_changed(self, context)


def _object_changed(self, context):
    obj = getattr(self, "id_data", None)
    if isinstance(obj, bpy.types.Object):
        overlay.mark_object_dirty(obj, context.scene if context else None)
    else:
        overlay.invalidate_all()


class WBChunkSceneSettings(PropertyGroup):
    ui_language: EnumProperty(name="Language",items=(("AUTO","Auto","Follow Blender language"),("KO","한국어","WorldBuilder production panels use Korean"),("EN","English","WorldBuilder production panels use English")),default="AUTO")
    profile_path: StringProperty(
        name="Grid Profile",
        description="Unity-authored WorldGrid.profile.json",
        subtype="FILE_PATH",
        update=_profile_path_changed,
    )
    profile_status: EnumProperty(
        name="Profile Status",
        items=(
            ("SYNCED", "Synced", "Blender values match the loaded Unity profile"),
            ("MODIFIED", "Modified", "Values or source file changed after loading"),
            ("INVALID", "Invalid", "Profile is missing or invalid"),
        ),
        default="INVALID",
    )
    profile_message: StringProperty(default="Load WorldGrid.profile.json")
    profile_snapshot_json: StringProperty(default="", options={"HIDDEN"})
    profile_hash: StringProperty(default="", options={"HIDDEN"})
    profile_mtime_ns: StringProperty(default="0", options={"HIDDEN"})
    developer_override: BoolProperty(
        name="Developer Override",
        description="Allow manual grid values and export while the profile is not synced",
        default=False,
        update=_display_changed,
    )

    world_id: StringProperty(name="World ID", default="World_01", update=_grid_value_changed)
    chunk_size: FloatProperty(
        name="Chunk Size",
        default=128.0,
        min=0.001,
        unit="LENGTH",
        update=_grid_value_changed,
    )
    chunks_per_region: IntProperty(
        name="Chunks / Region", default=4, min=1, update=_grid_value_changed
    )
    query_cell_size: FloatProperty(
        name="Query Cell Size",
        default=32.0,
        min=0.001,
        unit="LENGTH",
        update=_grid_value_changed,
    )
    origin_x: FloatProperty(
        name="Origin X", default=0.0, unit="LENGTH", update=_grid_value_changed
    )
    origin_y: FloatProperty(
        name="Origin Z / Blender Y",
        default=0.0,
        unit="LENGTH",
        update=_grid_value_changed,
    )

    export_root: StringProperty(name="Export Root", subtype="DIR_PATH")

    show_chunk_grid: BoolProperty(name="Chunk Grid", default=True, update=_display_changed)
    show_region_grid: BoolProperty(name="Region Grid", default=True, update=_display_changed)
    show_coordinates: BoolProperty(name="Coordinates", default=True, update=_display_changed)
    show_active_chunk: BoolProperty(name="Active Chunk", default=True, update=_display_changed)
    show_object_ownership: BoolProperty(
        name="Object Ownership", default=True, update=_display_changed
    )
    show_boundary_errors: BoolProperty(
        name="Boundary Errors", default=True, update=_display_changed
    )
    show_query_cells: BoolProperty(name="Query Cells", default=False, update=_display_changed)
    show_streaming_preview: BoolProperty(
        name="Streaming Preview", default=False, update=_display_changed
    )

    grid_center_mode: EnumProperty(
        name="Grid Center",
        items=(
            ("VIEWPORT", "Viewport Camera", "Center the grid around the viewport ray on the XY plane"),
            ("ACTIVE_OBJECT", "Active Object", "Center around the active object"),
            ("CURSOR", "3D Cursor", "Center around the 3D cursor"),
            ("WORLD_ORIGIN", "World Origin", "Center around the profile world origin"),
        ),
        default="VIEWPORT",
        update=_display_changed,
    )
    grid_radius: IntProperty(
        name="Radius", default=6, min=1, max=64, update=_display_changed
    )
    label_radius: IntProperty(
        name="Label Radius",
        default=2,
        min=0,
        max=12,
        description="Draw chunk labels only this many chunks from the grid center",
        update=_display_changed,
    )
    overlay_z: FloatProperty(
        name="Overlay Height",
        default=0.05,
        unit="LENGTH",
        description="Small Z offset for the floor overlay",
        update=_display_changed,
    )

    streaming_focus: EnumProperty(
        name="Streaming Focus",
        items=(
            ("ACTIVE_OBJECT", "Active Object", "Use the active object as streaming focus"),
            ("CURSOR", "3D Cursor", "Use the 3D cursor as streaming focus"),
        ),
        default="ACTIVE_OBJECT",
        update=_display_changed,
    )
    streaming_region_radius: IntProperty(
        name="Region Radius", default=1, min=0, max=8, update=_display_changed
    )

    layer_height: FloatProperty(
        name="Layer Height",
        default=16.0,
        min=0.001,
        unit="LENGTH",
        description="Vertical size of one authoring layer along Blender Z",
        update=_grid_value_changed,
    )
    layer_base_z: FloatProperty(
        name="Layer Base Z",
        default=0.0,
        unit="LENGTH",
        description="Blender Z of layer 0's floor",
        update=_grid_value_changed,
    )
    layer_count: IntProperty(
        name="Layer Count", default=8, min=1, max=256, update=_display_changed
    )
    active_layer: IntProperty(
        name="Active Layer", default=0, min=0, update=_layer_changed
    )
    layer_follow_grid: BoolProperty(
        name="Grid Follows Layer",
        default=True,
        description="Draw the chunk grid at the active layer floor instead of the world floor",
        update=_display_changed,
    )
    show_layer_bands: BoolProperty(
        name="Layer Bands", default=True, update=_display_changed
    )
    layer_isolate: EnumProperty(
        name="Isolate",
        items=(
            ("OFF", "Off", "Show every layer"),
            ("ACTIVE", "Active Layer", "Show only objects owned by the active layer"),
            ("ACTIVE_BELOW", "Active and Below", "Show the active layer and everything under it"),
        ),
        default="OFF",
        update=_layer_changed,
    )
    layer_lock_placement: BoolProperty(
        name="Lock Placement to Layer",
        default=False,
        description="Clamp new placements to the active layer band",
        update=_display_changed,
    )

    has_active_chunk: BoolProperty(default=False, options={"HIDDEN"})
    active_chunk_x: IntProperty(default=0, options={"HIDDEN"})
    active_chunk_z: IntProperty(default=0, options={"HIDDEN"})
    selected_chunks_json: StringProperty(default="[]", options={"HIDDEN"})
    dirty_chunks_json: StringProperty(default="[]", options={"HIDDEN"})
    validation_error_objects_json: StringProperty(default="[]", options={"HIDDEN"})


class WBChunkObjectSettings(PropertyGroup):
    role: EnumProperty(
        name="Role",
        default="AUTO",
        items=(
            ("AUTO", "Auto", "Infer role from the object name"),
            ("GEOMETRY", "Geometry", "Static rendered geometry"),
            ("COLLISION", "Collision", "Collision-only geometry"),
            ("INSTANCE", "Instance", "Unity prefab placement"),
            ("ENTITY", "Entity", "Unity DOTS entity placement"),
            ("MARKER", "Marker", "Unity marker placement"),
            ("GLOBAL", "Global", "Exclude from chunk export"),
        ),
        update=_object_changed,
    )
    stable_id: StringProperty(name="Stable ID", update=_object_changed)
    asset_id: StringProperty(name="Asset ID", update=_object_changed)
    marker_type: StringProperty(name="Marker Type", update=_object_changed)
    entity_prefab_id: IntProperty(
        name="Entity Prefab ID",
        default=0,
        min=0,
        description="Must match WorldEntityAuthoring.PrefabId on the Unity prefab",
        update=_object_changed,
    )
    entity_kind: EnumProperty(
        name="Entity Kind",
        items=tuple((value, value, "") for value in contract.ENTITY_KINDS),
        default="Generic",
        update=_object_changed,
    )
    entity_catalog_pick: EnumProperty(
        name="Catalog",
        items=entity_catalog.enum_items,
        update=_entity_pick_changed,
    )
    entity_persistent: BoolProperty(
        name="Persistent", default=False, update=_object_changed
    )
    entity_region_streamed: BoolProperty(
        name="Region Streamed", default=True, update=_object_changed
    )
    entity_replicated: BoolProperty(
        name="Replicated", default=False, update=_object_changed
    )
    entity_lifetime: FloatProperty(
        name="Lifetime (s)", default=0.0, min=0.0, update=_object_changed
    )
    override_layer: BoolProperty(
        name="Layer Override", default=False, update=_object_changed
    )
    layer_index: IntProperty(name="Layer", default=0, min=0, update=_object_changed)
    allow_cross_chunk: BoolProperty(
        name="Allow Cross Chunk", default=False, update=_object_changed
    )
    override_chunk: BoolProperty(
        name="Object Override", default=False, update=_object_changed
    )
    chunk_x: IntProperty(name="Chunk X", default=0, update=_object_changed)
    chunk_z: IntProperty(name="Chunk Z", default=0, update=_object_changed)


class WB_OT_reload_profile(Operator):
    bl_idname = "worldbuilder.reload_grid_profile"
    bl_label = "Reload Profile"
    bl_options = {"REGISTER"}

    def execute(self, context):
        settings = _settings(context)
        path = _resolved_profile_path(settings)
        if not path:
            settings.profile_status = "INVALID"
            settings.profile_message = "Select WorldGrid.profile.json"
            self.report({"ERROR"}, settings.profile_message)
            return {"CANCELLED"}
        result = profile.load_file(path)
        profile.apply_to_settings(settings, result)
        overlay.initialize_owner_cache(context.scene)
        overlay.invalidate_all()
        if not result.valid:
            self.report({"ERROR"}, f"Invalid grid profile: {result.error}")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Synced grid profile: {settings.world_id}")
        return {"FINISHED"}


class WB_OT_open_profile_folder(Operator):
    bl_idname = "worldbuilder.open_grid_profile_folder"
    bl_label = "Open Profile Folder"

    def execute(self, context):
        path = _resolved_profile_path(_settings(context))
        folder = os.path.dirname(path) if path else ""
        if not folder or not os.path.isdir(folder):
            self.report({"ERROR"}, "Profile folder does not exist")
            return {"CANCELLED"}
        bpy.ops.wm.path_open(filepath=folder)
        return {"FINISHED"}


class WB_OT_validate(Operator):
    bl_idname = "worldbuilder.validate_chunks"
    bl_label = "Validate World"
    bl_options = {"REGISTER"}

    def execute(self, context):
        settings = _settings(context)
        issues = exporter.validate_scene(context.scene, settings)
        errors = [issue for issue in issues if issue[0] == "ERROR"]
        warnings = [issue for issue in issues if issue[0] == "WARNING"]
        state.set_validation_error_names(
            settings, [obj.name for _, _, obj in errors if obj is not None]
        )
        if errors:
            bpy.ops.object.select_all(action="DESELECT")
            for _, _, obj in errors:
                if obj is not None and obj.name in context.view_layer.objects:
                    obj.select_set(True)
            self.report(
                {"ERROR"},
                f"Validation failed: {len(errors)} error(s), {len(warnings)} warning(s). See Console.",
            )
        else:
            self.report({"INFO"}, f"Validation passed: {len(warnings)} warning(s).")
        for severity, message, obj in issues:
            print(f"[WorldBuilder][{severity}] {obj.name + ': ' if obj else ''}{message}")
        overlay.invalidate_all()
        return {"CANCELLED"} if errors else {"FINISHED"}


class WB_OT_validate_active_chunk(Operator):
    bl_idname = "worldbuilder.validate_active_chunk"
    bl_label = "Validate Chunk"
    bl_options = {"REGISTER"}

    def execute(self, context):
        settings = _settings(context)
        coordinate = overlay.active_chunk(context, settings)
        objects = exporter.objects_in_chunk(context.scene, settings, coordinate)
        issues = exporter.validate_scene(context.scene, settings, objects)
        errors = [issue for issue in issues if issue[0] == "ERROR"]
        existing = state.validation_error_names(settings)
        existing.difference_update(obj.name for obj in objects)
        existing.update(obj.name for _, _, obj in errors if obj is not None)
        state.set_validation_error_names(settings, existing)
        for severity, message, obj in issues:
            print(f"[WorldBuilder][{severity}] {obj.name + ': ' if obj else ''}{message}")
        overlay.invalidate_all()
        if errors:
            self.report({"ERROR"}, f"{contract.chunk_name(coordinate)}: {len(errors)} error(s)")
            return {"CANCELLED"}
        self.report({"INFO"}, f"{contract.chunk_name(coordinate)} validated")
        return {"FINISHED"}


class WB_OT_assign_cursor_chunk(Operator):
    bl_idname = "worldbuilder.assign_cursor_chunk"
    bl_label = "Assign Selection to Cursor Chunk"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if not context.selected_objects:
            self.report({"WARNING"}, "Select at least one object.")
            return {"CANCELLED"}
        settings = _settings(context)
        cursor = context.scene.cursor.location
        coordinate = contract.chunk_coord_from_xy(
            cursor.x,
            cursor.y,
            settings.origin_x,
            settings.origin_y,
            settings.chunk_size,
        )
        exporter.assign_objects_to_chunk(
            context.scene, context.selected_objects, coordinate, explicit_override=False
        )
        state.mark_dirty(settings, coordinate)
        state.set_active_chunk(settings, coordinate)
        overlay.initialize_owner_cache(context.scene)
        overlay.invalidate_all()
        self.report(
            {"INFO"},
            f"Assigned {len(context.selected_objects)} object(s) to {contract.chunk_name(coordinate)}.",
        )
        return {"FINISHED"}


class WB_OT_create_cursor_chunk(Operator):
    bl_idname = "worldbuilder.create_cursor_chunk"
    bl_label = "Create Cursor Chunk Collection"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = _settings(context)
        cursor = context.scene.cursor.location
        coordinate = contract.chunk_coord_from_xy(
            cursor.x,
            cursor.y,
            settings.origin_x,
            settings.origin_y,
            settings.chunk_size,
        )
        collection = exporter.ensure_chunk_collection(context.scene, coordinate)
        state.set_active_chunk(settings, coordinate)
        overlay.invalidate_all()
        self.report({"INFO"}, f"Ready: {collection.name}")
        return {"FINISHED"}


class WB_OT_create_active_collection(Operator):
    bl_idname = "worldbuilder.create_active_chunk_collection"
    bl_label = "Create Collection"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = _settings(context)
        coordinate = overlay.active_chunk(context, settings)
        collection = exporter.ensure_chunk_collection(context.scene, coordinate)
        self.report({"INFO"}, f"Ready: {collection.name}")
        overlay.invalidate_all()
        return {"FINISHED"}


class WB_OT_assign_active_chunk(Operator):
    bl_idname = "worldbuilder.assign_selection_active_chunk"
    bl_label = "Assign Selection"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if not context.selected_objects:
            self.report({"WARNING"}, "Select at least one object")
            return {"CANCELLED"}
        settings = _settings(context)
        coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
        exporter.assign_objects_to_chunk(
            context.scene, context.selected_objects, coordinate, explicit_override=False
        )
        state.mark_dirty(settings, coordinate)
        overlay.initialize_owner_cache(context.scene)
        overlay.invalidate_all()
        self.report({"INFO"}, f"Assigned selection to {contract.chunk_name(coordinate)}")
        return {"FINISHED"}


class WB_OT_select_active_chunk_objects(Operator):
    bl_idname = "worldbuilder.select_active_chunk_objects"
    bl_label = "Select Objects"
    bl_options = {"REGISTER"}

    def execute(self, context):
        settings = _settings(context)
        coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
        objects = exporter.objects_in_chunk(context.scene, settings, coordinate)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in objects:
            if obj.name in context.view_layer.objects:
                obj.select_set(True)
        if objects and objects[0].name in context.view_layer.objects:
            context.view_layer.objects.active = objects[0]
        self.report({"INFO"}, f"Selected {len(objects)} object(s)")
        overlay.invalidate_all()
        return {"FINISHED"}


class WB_OT_focus_active_chunk(Operator):
    bl_idname = "worldbuilder.focus_active_chunk"
    bl_label = "Focus Chunk"

    def execute(self, context):
        settings = _settings(context)
        coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
        center_x, center_y = contract.chunk_center_xy(
            coordinate, settings.origin_x, settings.origin_y, settings.chunk_size
        )
        if context.space_data.type == "VIEW_3D":
            context.space_data.region_3d.view_location = Vector((center_x, center_y, 0.0))
            overlay.invalidate_all()
            return {"FINISHED"}
        return {"CANCELLED"}


class _WBExportBase:
    force = False
    selected_only = False

    def execute(self, context):
        results, issues = exporter.export_dirty_chunks(
            context,
            _settings(context),
            force=self.force,
            selected_only=self.selected_only,
        )
        errors = [issue for issue in issues if issue[0] == "ERROR"]
        if errors:
            for severity, message, obj in issues:
                print(f"[WorldBuilder][{severity}] {obj.name + ': ' if obj else ''}{message}")
            self.report({"ERROR"}, f"Export blocked by {len(errors)} error(s).")
            return {"CANCELLED"}
        exported = sum(1 for _, status, _ in results if status == "EXPORTED")
        skipped = sum(1 for _, status, _ in results if status == "SKIPPED")
        overlay.invalidate_all()
        self.report({"INFO"}, f"Chunks: {exported} exported, {skipped} unchanged.")
        return {"FINISHED"}


class WB_OT_export_dirty(_WBExportBase, Operator):
    bl_idname = "worldbuilder.export_dirty_chunks"
    bl_label = "Export Dirty Chunks"
    bl_options = {"REGISTER"}


class WB_OT_export_selected(_WBExportBase, Operator):
    bl_idname = "worldbuilder.export_selected_chunks"
    bl_label = "Export Selected Objects' Chunks"
    bl_options = {"REGISTER"}
    selected_only = True


class WB_OT_export_all(_WBExportBase, Operator):
    bl_idname = "worldbuilder.export_all_chunks"
    bl_label = "Force Export All"
    bl_options = {"REGISTER"}
    force = True


class WB_OT_export_active_chunk(Operator):
    bl_idname = "worldbuilder.export_active_chunk"
    bl_label = "Export Chunk"
    bl_options = {"REGISTER"}

    def execute(self, context):
        settings = _settings(context)
        coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
        results, issues = exporter.export_coordinates(context, settings, [coordinate])
        errors = [issue for issue in issues if issue[0] == "ERROR"]
        if errors:
            for severity, message, obj in issues:
                print(f"[WorldBuilder][{severity}] {obj.name + ': ' if obj else ''}{message}")
            self.report({"ERROR"}, "Chunk export blocked")
            return {"CANCELLED"}
        overlay.invalidate_all()
        status = results[0][1] if results else "SKIPPED"
        self.report({"INFO"}, f"{contract.chunk_name(coordinate)}: {status}")
        return {"FINISHED"}


class WB_OT_chunk_select_tool(Operator):
    """Modal XY-plane chunk picker. It creates no scene geometry."""

    bl_idname = "worldbuilder.chunk_select_tool"
    bl_label = "Chunk Select Tool"
    bl_options = {"REGISTER", "UNDO", "BLOCKING"}

    _window_region = None

    def _find_window_region(self, context):
        if context.area is None or context.area.type != "VIEW_3D":
            return None
        for region in context.area.regions:
            if region.type == "WINDOW":
                return region
        return None

    def _coord_from_event(self, context, event):
        region = self._window_region
        rv3d = context.space_data.region_3d
        if region is None or rv3d is None:
            return None
        x = event.mouse_x - region.x
        y = event.mouse_y - region.y
        if x < 0 or y < 0 or x >= region.width or y >= region.height:
            return None
        origin = view3d_utils.region_2d_to_origin_3d(region, rv3d, (x, y))
        direction = view3d_utils.region_2d_to_vector_3d(region, rv3d, (x, y))
        settings = _settings(context)
        if abs(direction.z) <= 1e-8:
            return None
        distance = (overlay.plane_z(settings) - origin.z) / direction.z
        point = origin + direction * distance
        return contract.chunk_coord_from_xy(
            point.x,
            point.y,
            settings.origin_x,
            settings.origin_y,
            settings.chunk_size,
        )

    def invoke(self, context, event):
        self._window_region = self._find_window_region(context)
        if self._window_region is None:
            self.report({"ERROR"}, "Run this tool from a 3D View")
            return {"CANCELLED"}
        context.window_manager.modal_handler_add(self)
        context.window.cursor_modal_set("CROSSHAIR")
        self.report(
            {"INFO"},
            "Click: active | Shift: multi | Ctrl: create collection | Double-click: select objects | F: focus | Esc: exit",
        )
        return {"RUNNING_MODAL"}

    def modal(self, context, event):
        settings = _settings(context)
        if event.type in {"ESC", "RIGHTMOUSE"}:
            context.window.cursor_modal_restore()
            return {"FINISHED"}

        if event.type in {"MIDDLEMOUSE", "WHEELUPMOUSE", "WHEELDOWNMOUSE"}:
            return {"PASS_THROUGH"}

        if event.type == "F" and event.value == "PRESS":
            coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
            center_x, center_y = contract.chunk_center_xy(
                coordinate, settings.origin_x, settings.origin_y, settings.chunk_size
            )
            context.space_data.region_3d.view_location = Vector((center_x, center_y, 0.0))
            overlay.invalidate_all()
            return {"RUNNING_MODAL"}

        if event.type == "LEFTMOUSE" and event.value in {"PRESS", "DOUBLE_CLICK"}:
            coordinate = self._coord_from_event(context, event)
            if coordinate is None:
                return {"RUNNING_MODAL"}
            state.set_active_chunk(settings, coordinate)

            if event.value == "DOUBLE_CLICK":
                objects = exporter.objects_in_chunk(context.scene, settings, coordinate)
                bpy.ops.object.select_all(action="DESELECT")
                for obj in objects:
                    if obj.name in context.view_layer.objects:
                        obj.select_set(True)
                if objects and objects[0].name in context.view_layer.objects:
                    context.view_layer.objects.active = objects[0]
            elif event.ctrl:
                bpy.ops.object.select_all(action="DESELECT")
                exporter.ensure_chunk_collection(context.scene, coordinate)
            elif event.shift:
                bpy.ops.object.select_all(action="DESELECT")
                selected = state.selected_chunks(settings)
                if coordinate in selected:
                    selected.remove(coordinate)
                else:
                    selected.add(coordinate)
                state.set_selected_chunks(settings, selected)
            else:
                bpy.ops.object.select_all(action="DESELECT")
                state.set_selected_chunks(settings, [coordinate])

            overlay.invalidate_all()
            return {"RUNNING_MODAL"}

        return {"RUNNING_MODAL"}

    def cancel(self, context):
        context.window.cursor_modal_restore()


class WB_PT_toolkit_overview(Panel):
    bl_label = "WorldBuilder Toolkit"
    bl_idname = "WB_PT_toolkit_overview"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"

    def draw(self, context):
        layout = self.layout
        settings = _settings(context)
        layout.prop(settings,"ui_language",text=localization.tr("language",context.scene))

        status, _ = profile.current_status(
            settings, _resolved_profile_path(settings) or None
        )
        icon = {"SYNCED": "CHECKMARK", "MODIFIED": "ERROR", "INVALID": "CANCEL"}[status]
        header=layout.box();header.label(text="WorldBuilder Toolkit 1.6.0",icon="WORLD");header.label(text=f"Grid Profile: {status.title()}", icon=icon)
        dirty=state.dirty_chunks(settings);errors=state.validation_error_names(settings);coordinate=state.explicit_active_chunk(settings) or overlay.active_chunk(context,settings)
        health=layout.box();health.label(text="Pipeline Health");health.label(text=f"Active {contract.chunk_name(coordinate)} | Dirty {len(dirty)} | Errors {len(errors)}")
        if bpy.app.version<(4,3,0):health.label(text=f"Unsupported Blender {bpy.app.version_string}; requires 4.3+",icon="CANCEL")
        elif bpy.app.version>=(5,2,0):health.label(text=f"Blender {bpy.app.version_string}: newer than verified 5.1",icon="ERROR")
        else:health.label(text=f"Blender {bpy.app.version_string} compatible",icon="CHECKMARK")

        terrain_count = sum(
            1 for obj in bpy.data.objects
            if obj.get("nex_stylized_terrain_generated") and obj.get("nex_stylized_terrain_kind") == "TERRAIN"
        )
        rock_collection = bpy.data.collections.get("NexRock_Generated")
        rock_count = len(rock_collection.objects) if rock_collection is not None else 0
        stats = layout.box()
        stats.label(text=f"Generated Terrain Objects: {terrain_count}")
        stats.label(text=f"Generated Rock Objects: {rock_count}")

        quick = layout.box()
        quick.label(text="Quick Actions")
        row = quick.row(align=True)
        row.operator("nex.generate_stylized_terrain", text="Terrain", icon="MESH_GRID")
        row.operator("nexrock.generate", text="Rock", icon="MESH_ICOSPHERE")
        row=quick.row(align=True);row.operator("worldbuilder.validate_chunks",text="Validate",icon="CHECKMARK");row.operator("worldbuilder.export_dirty_chunks",text="Export Dirty",icon="EXPORT")
        quick.label(text="Long Bake jobs show progress; press Esc to cancel and roll back",icon="INFO")
        if settings.profile_path:
            quick.operator("worldbuilder.reload_grid_profile", text="Reload Grid Profile", icon="FILE_REFRESH")


class WB_PT_world(Panel):
    bl_label = "World Grid"
    bl_idname = "WB_PT_world_grid"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout = self.layout
        value = _settings(context)

        profile_box = layout.box()
        profile_box.label(text="Unity Grid Profile")
        profile_box.prop(value, "profile_path", text="")
        status, message = profile.current_status(
            value, _resolved_profile_path(value) or None
        )
        icon = {"SYNCED": "CHECKMARK", "MODIFIED": "ERROR", "INVALID": "CANCEL"}[status]
        profile_box.label(text=f"Status: {status.title()}", icon=icon)
        if message:
            profile_box.label(text=message)
        row = profile_box.row(align=True)
        row.operator("worldbuilder.reload_grid_profile", icon="FILE_REFRESH")
        row.operator("worldbuilder.open_grid_profile_folder", icon="FILE_FOLDER")
        profile_box.prop(value, "developer_override")

        grid_box = layout.box()
        grid_box.label(text="Authoritative Grid Values")
        grid_box.enabled = value.developer_override
        grid_box.prop(value, "world_id")
        grid_box.prop(value, "chunk_size")
        grid_box.prop(value, "chunks_per_region")
        grid_box.prop(value, "query_cell_size")
        row = grid_box.row(align=True)
        row.prop(value, "origin_x")
        row.prop(value, "origin_y")

        center_box = layout.box()
        center_box.label(text="Grid Center")
        center_box.prop(value, "grid_center_mode", text="")
        center_box.prop(value, "grid_radius")
        center_box.prop(value, "label_radius")

        display_box = layout.box()
        display_box.label(text="Display")
        column = display_box.column(align=True)
        column.prop(value, "show_chunk_grid")
        column.prop(value, "show_region_grid")
        column.prop(value, "show_coordinates")
        column.prop(value, "show_active_chunk")
        column.prop(value, "show_object_ownership")
        column.prop(value, "show_boundary_errors")
        column.prop(value, "show_query_cells")
        column.prop(value, "show_streaming_preview")
        if value.show_streaming_preview:
            display_box.prop(value, "streaming_focus")
            display_box.prop(value, "streaming_region_radius")
        display_box.prop(value, "overlay_z")


class WB_PT_active_chunk(Panel):
    bl_label = "Active Chunk"
    bl_idname = "WB_PT_active_chunk"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"

    def draw(self, context):
        layout = self.layout
        settings = _settings(context)
        coordinate = state.explicit_active_chunk(settings) or overlay.active_chunk(context, settings)
        region = contract.region_coord(
            coordinate[0], coordinate[1], settings.chunks_per_region
        )
        objects = exporter.objects_in_chunk(context.scene, settings, coordinate)
        dirty = coordinate in state.dirty_chunks(settings)
        layout.label(text=f"Coordinate     {contract.signed(coordinate[0])}, {contract.signed(coordinate[1])}")
        layout.label(text=f"Region         {contract.signed(region[0])}, {contract.signed(region[1])}")
        layout.label(text=f"Objects        {len(objects)}")
        layout.label(text=f"Dirty          {'Yes' if dirty else 'No'}")
        layout.operator("worldbuilder.chunk_select_tool", icon="RESTRICT_SELECT_OFF")
        row = layout.row(align=True)
        row.operator("worldbuilder.select_active_chunk_objects")
        row.operator("worldbuilder.focus_active_chunk", text="Focus (F)")
        row = layout.row(align=True)
        row.operator("worldbuilder.create_active_chunk_collection")
        row.operator("worldbuilder.assign_selection_active_chunk")
        row = layout.row(align=True)
        row.operator("worldbuilder.validate_active_chunk")
        row.operator("worldbuilder.export_active_chunk")


class WB_PT_object(Panel):
    bl_label = "Chunk Object"
    bl_idname = "WB_PT_chunk_object"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"

    @classmethod
    def poll(cls, context):
        return context.object is not None

    def draw(self, context):
        layout = self.layout
        obj = context.object
        settings = _settings(context)
        value = obj.worldbuilder_chunk
        status = exporter.object_bounds_status(
            obj, settings, exporter.chunk_collection_map(context.scene)
        )

        info = layout.box()
        info.label(text=f"Object: {obj.name}")
        info.label(text=f"Role: {status.role}")
        info.label(text=f"Owner: {contract.chunk_name(status.owner)}")
        source_name = {
            "PIVOT": "Pivot",
            "COLLECTION": "Collection",
            "OVERRIDE": "Override",
        }.get(status.ownership_source, status.ownership_source)
        info.label(text=f"Ownership: {source_name}")
        if status.crosses_chunk:
            if status.allow_cross_chunk:
                info.label(text="Bounds: Cross-chunk allowed", icon="ERROR")
            else:
                info.label(text="Bounds: Boundary violation", icon="CANCEL")
        elif status.role in exporter.GEOMETRY_ROLES:
            info.label(text="Bounds: Inside owner chunk", icon="CHECKMARK")

        info.label(text=f"Layer: {contract.layer_name(exporter.object_layer(obj, settings))}")

        layout.prop(value, "role")
        layout.prop(value, "stable_id")
        if status.role in exporter.ASSET_ROLES:
            layout.prop(value, "asset_id")
        if status.role == "ENTITY":
            box = layout.box()
            box.label(text="DOTS Entity", icon="OUTLINER_OB_POINTCLOUD")
            entity_catalog.draw_picker(box, context.scene, value, "entity_catalog_pick")
            box.prop(value, "entity_prefab_id")
            box.prop(value, "entity_kind")
            row = box.row(align=True)
            row.prop(value, "entity_persistent", toggle=True)
            row.prop(value, "entity_region_streamed", toggle=True)
            row.prop(value, "entity_replicated", toggle=True)
            box.prop(value, "entity_lifetime")
        if status.role == "MARKER":
            layout.prop(value, "marker_type")
        if value.role in {"AUTO", "GEOMETRY", "COLLISION"}:
            layout.prop(value, "allow_cross_chunk")
        layout.prop(value, "override_chunk")
        if value.override_chunk:
            row = layout.row(align=True)
            row.prop(value, "chunk_x")
            row.prop(value, "chunk_z")
        layout.prop(value, "override_layer")
        if value.override_layer:
            layout.prop(value, "layer_index")


class WB_PT_export(Panel):
    bl_label = "Unity Export"
    bl_idname = "WB_PT_unity_export"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_world_grid"

    def draw(self, context):
        layout = self.layout
        settings = _settings(context)
        status, _ = profile.current_status(
            settings, _resolved_profile_path(settings) or None
        )
        layout.prop(settings, "export_root")
        if status != "SYNCED" and not settings.developer_override:
            layout.label(text="Export blocked: grid profile is not Synced", icon="LOCKED")
        layout.operator("worldbuilder.validate_chunks", icon="CHECKMARK")
        layout.operator("worldbuilder.export_dirty_chunks", icon="EXPORT")
        layout.operator("worldbuilder.export_selected_chunks")
        layout.operator("worldbuilder.export_all_chunks")


_classes = (
    WBChunkSceneSettings,
    WBChunkObjectSettings,
    WB_OT_reload_profile,
    WB_OT_open_profile_folder,
    WB_OT_validate,
    WB_OT_validate_active_chunk,
    WB_OT_assign_cursor_chunk,
    WB_OT_create_cursor_chunk,
    WB_OT_create_active_collection,
    WB_OT_assign_active_chunk,
    WB_OT_select_active_chunk_objects,
    WB_OT_focus_active_chunk,
    WB_OT_export_dirty,
    WB_OT_export_selected,
    WB_OT_export_all,
    WB_OT_export_active_chunk,
    WB_OT_chunk_select_tool,
    WB_PT_toolkit_overview,
    WB_PT_world,
    WB_PT_active_chunk,
    WB_PT_object,
    WB_PT_export,
)


@persistent
def _load_post(_):
    scene = getattr(bpy.context, "scene", None)
    overlay.initialize_owner_cache(scene)
    overlay.invalidate_all()


def register():
    registered_root_classes = []
    rock_registered = False
    terrain_registered = False
    biome_registered = False
    registered_features = []
    try:
        localization.register()
        for cls in _classes:
            bpy.utils.register_class(cls)
            registered_root_classes.append(cls)
        bpy.types.Scene.worldbuilder_chunks = PointerProperty(type=WBChunkSceneSettings)
        bpy.types.Object.worldbuilder_chunk = PointerProperty(type=WBChunkObjectSettings)

        rock_generator.register()
        rock_registered = True
        terrain_toolkit.register()
        terrain_registered = True
        biome_ui.register()
        biome_registered = True
        for module in _feature_modules:
            module.register()
            registered_features.append(module)

        overlay.register_handlers()
        if overlay.depsgraph_update_post not in bpy.app.handlers.depsgraph_update_post:
            bpy.app.handlers.depsgraph_update_post.append(overlay.depsgraph_update_post)
        if _load_post not in bpy.app.handlers.load_post:
            bpy.app.handlers.load_post.append(_load_post)
        overlay.initialize_owner_cache(getattr(bpy.context, "scene", None))
    except Exception:
        overlay.unregister_handlers()
        for module in reversed(registered_features):
            try:
                module.unregister()
            except Exception:
                pass
        if biome_registered:
            biome_ui.unregister()
        if terrain_registered:
            terrain_toolkit.unregister()
        if rock_registered:
            rock_generator.unregister()
        if hasattr(bpy.types.Object, "worldbuilder_chunk"):
            del bpy.types.Object.worldbuilder_chunk
        if hasattr(bpy.types.Scene, "worldbuilder_chunks"):
            del bpy.types.Scene.worldbuilder_chunks
        for cls in reversed(registered_root_classes):
            try:
                bpy.utils.unregister_class(cls)
            except RuntimeError:
                pass
        localization.unregister()
        raise


def unregister():
    overlay.unregister_handlers()
    if overlay.depsgraph_update_post in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.remove(overlay.depsgraph_update_post)
    if _load_post in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(_load_post)

    for module in reversed(_feature_modules):
        module.unregister()
    biome_ui.unregister()
    terrain_toolkit.unregister()
    rock_generator.unregister()

    if hasattr(bpy.types.Object, "worldbuilder_chunk"):
        del bpy.types.Object.worldbuilder_chunk
    if hasattr(bpy.types.Scene, "worldbuilder_chunks"):
        del bpy.types.Scene.worldbuilder_chunks
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    localization.unregister()
