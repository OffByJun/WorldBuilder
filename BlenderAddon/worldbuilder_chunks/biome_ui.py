"""Biome definition properties, operators, and WorldBuilder sidebar UI."""

from __future__ import annotations

import uuid

import bpy
from bpy.props import BoolProperty, CollectionProperty, FloatProperty, FloatVectorProperty, IntProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList

from . import biome, biome_contract


class WBBiomeDefinition(PropertyGroup):
    stable_id: StringProperty(name="Stable ID")
    name: StringProperty(name="Name", default="Biome")
    attribute_name: StringProperty(name="Attribute")
    color: FloatVectorProperty(name="Color", subtype="COLOR", size=4, min=0.0, max=1.0,
                               default=(0.4, 0.6, 0.4, 1.0))
    enabled: BoolProperty(name="Enabled", default=True)
    export_enabled: BoolProperty(name="Export", default=True)
    sort_order: IntProperty(name="Order", default=0)


class WBBiomeSettings(PropertyGroup):
    layers: CollectionProperty(type=WBBiomeDefinition)
    active_index: IntProperty(default=0)
    validation_message: StringProperty(default="Not validated")


def _settings(context):
    return context.scene.worldbuilder_biomes


def _target(context):
    return context.active_object


def _active_definition(context):
    settings = _settings(context)
    return settings.layers[settings.active_index] if 0 <= settings.active_index < len(settings.layers) else None


def _all_targets():
    return [obj for obj in bpy.data.objects if biome.is_biome_target(obj)]


def _new_definition(settings, name: str, color=(0.4, 0.6, 0.4, 1.0)):
    attribute = biome_contract.attribute_name(name)
    if any(item.attribute_name == attribute for item in settings.layers):
        raise ValueError(f"Biome attribute already exists: {attribute}")
    definition = settings.layers.add()
    definition.stable_id = uuid.uuid4().hex
    definition.name = name.strip()
    definition.attribute_name = attribute
    definition.color = color
    definition.enabled = True
    definition.export_enabled = True
    definition.sort_order = len(settings.layers) - 1
    settings.active_index = len(settings.layers) - 1
    return definition


class WB_UL_biomes(UIList):
    def draw_item(self, _context, layout, _data, item, _icon, _active_data, _active_propname, _index):
        row = layout.row(align=True)
        row.prop(item, "enabled", text="")
        row.prop(item, "color", text="")
        row.label(text=item.name)
        row.prop(item, "export_enabled", text="", icon="EXPORT")


class WB_OT_mark_biome_target(Operator):
    bl_idname = "worldbuilder.mark_biome_target"
    bl_label = "Mark Active as Biome Target"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        try:
            biome.mark_biome_target(_target(context))
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, "Active object is now a biome target")
        return {"FINISHED"}


class WB_OT_initialize_default_biomes(Operator):
    bl_idname = "worldbuilder.initialize_default_biomes"
    bl_label = "Initialize Default Biomes"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        obj = _target(context)
        if obj is None or obj.type != "MESH":
            self.report({"ERROR"}, "Select a Mesh object")
            return {"CANCELLED"}
        biome.mark_biome_target(obj)
        settings = _settings(context)
        try:
            for name, color in biome_contract.DEFAULT_BIOMES:
                attribute = biome_contract.attribute_name(name)
                definition = next((item for item in settings.layers if item.attribute_name == attribute), None)
                if definition is None:
                    definition = _new_definition(settings, name, color)
                biome.ensure_biome_attribute(obj.data, definition)
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, "Default biome layers initialized")
        return {"FINISHED"}


class WB_OT_add_biome(Operator):
    bl_idname = "worldbuilder.add_biome_layer"
    bl_label = "Add Biome Layer"
    bl_options = {"REGISTER", "UNDO"}
    biome_name: StringProperty(name="Name", default="New Biome")

    def invoke(self, context, _event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        try:
            definition = _new_definition(_settings(context), self.biome_name)
            obj = _target(context)
            if biome.is_biome_target(obj):
                biome.ensure_biome_attribute(obj.data, definition)
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        return {"FINISHED"}


class WB_OT_remove_biome(Operator):
    bl_idname = "worldbuilder.remove_biome_layer"
    bl_label = "Remove Biome Layer and Stored Attributes"
    bl_options = {"REGISTER", "UNDO"}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        settings = _settings(context)
        definition = _active_definition(context)
        if definition is None:
            return {"CANCELLED"}
        for obj in _all_targets():
            attribute = obj.data.attributes.get(definition.attribute_name)
            if attribute is not None:
                obj.data.attributes.remove(attribute)
                obj.data.update()
        settings.layers.remove(settings.active_index)
        settings.active_index = min(settings.active_index, len(settings.layers) - 1)
        return {"FINISHED"}


class WB_OT_rename_biome(Operator):
    bl_idname = "worldbuilder.rename_biome_layer"
    bl_label = "Rename Biome Layer"
    bl_options = {"REGISTER", "UNDO"}
    new_name: StringProperty(name="New Name")

    def invoke(self, context, _event):
        definition = _active_definition(context)
        self.new_name = definition.name if definition else ""
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        definition = _active_definition(context)
        if definition is None:
            return {"CANCELLED"}
        try:
            new_attribute = biome_contract.attribute_name(self.new_name)
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        if any(item != definition and item.attribute_name == new_attribute for item in _settings(context).layers):
            self.report({"ERROR"}, f"Attribute already exists: {new_attribute}")
            return {"CANCELLED"}
        for obj in _all_targets():
            attribute = obj.data.attributes.get(definition.attribute_name)
            collision = obj.data.attributes.get(new_attribute)
            if attribute is not None and collision is not None and collision != attribute:
                self.report({"ERROR"}, f"{obj.name} already contains {new_attribute}")
                return {"CANCELLED"}
        old_attribute = definition.attribute_name
        definition.name = self.new_name.strip()
        definition.attribute_name = new_attribute
        for obj in _all_targets():
            attribute = obj.data.attributes.get(old_attribute)
            if attribute is not None:
                attribute.name = new_attribute
        return {"FINISHED"}


class WB_OT_duplicate_biome(Operator):
    bl_idname = "worldbuilder.duplicate_biome_layer"
    bl_label = "Duplicate Biome Layer"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        source = _active_definition(context)
        if source is None:
            return {"CANCELLED"}
        settings = _settings(context)
        base, number = source.name + " Copy", 2
        name = base
        while any(item.attribute_name == biome_contract.attribute_name(name) for item in settings.layers):
            name, number = f"{base} {number}", number + 1
        target = _new_definition(settings, name, tuple(source.color))
        target.enabled, target.export_enabled = source.enabled, source.export_enabled
        for obj in _all_targets():
            source_attribute = obj.data.attributes.get(source.attribute_name)
            if source_attribute is None:
                continue
            target_attribute = biome.ensure_biome_attribute(obj.data, target)
            for index, item in enumerate(source_attribute.data):
                target_attribute.data[index].value = item.value
            obj.data.update()
        return {"FINISHED"}


class _WBWeightOperation:
    mode = "CLEAR"

    def execute(self, context):
        obj, definition = _target(context), _active_definition(context)
        if not biome.is_biome_target(obj) or definition is None:
            self.report({"ERROR"}, "Select a biome target and active layer")
            return {"CANCELLED"}
        attribute = biome.ensure_biome_attribute(obj.data, definition)
        if self.mode == "NORMALIZE":
            biome.normalize_all_weights(obj.data, biome.biome_definitions(context.scene))
        else:
            for item in attribute.data:
                if self.mode == "CLEAR": item.value = 0.0
                elif self.mode == "FILL": item.value = biome_contract.clamp_weight(self.value)
                elif self.mode == "INVERT": item.value = 1.0 - biome_contract.clamp_weight(item.value)
            obj.data.update()
        biome.invalidate_biome_cache(obj)
        return {"FINISHED"}


class WB_OT_clear_biome(_WBWeightOperation, Operator):
    bl_idname = "worldbuilder.clear_biome_layer"; bl_label = "Clear Biome Layer"; bl_options = {"REGISTER", "UNDO"}
    mode = "CLEAR"


class WB_OT_fill_biome(_WBWeightOperation, Operator):
    bl_idname = "worldbuilder.fill_biome_layer"; bl_label = "Fill Biome Layer"; bl_options = {"REGISTER", "UNDO"}
    mode = "FILL"
    value: FloatProperty(name="Value", default=1.0, min=0.0, max=1.0)


class WB_OT_invert_biome(_WBWeightOperation, Operator):
    bl_idname = "worldbuilder.invert_biome_layer"; bl_label = "Invert Biome Layer"; bl_options = {"REGISTER", "UNDO"}
    mode = "INVERT"


class WB_OT_normalize_biomes(_WBWeightOperation, Operator):
    bl_idname = "worldbuilder.normalize_biome_layers"; bl_label = "Normalize All Biomes"; bl_options = {"REGISTER", "UNDO"}
    mode = "NORMALIZE"


class WB_OT_validate_biomes(Operator):
    bl_idname = "worldbuilder.validate_biomes"
    bl_label = "Validate Biomes"
    bl_options = {"REGISTER"}

    def execute(self, context):
        issues = biome.validate_biome_data(context.scene, _target(context))
        _settings(context).validation_message = issues[0] if issues else "Valid"
        if issues:
            for issue in issues: print(f"[WorldBuilder][Biome][ERROR] {issue}")
            self.report({"ERROR"}, f"Biome validation failed: {len(issues)} error(s)")
            return {"CANCELLED"}
        self.report({"INFO"}, "Biome validation passed")
        return {"FINISHED"}


class WB_OT_set_active_biome(Operator):
    bl_idname = "worldbuilder.set_active_biome"
    bl_label = "Set Active Biome"
    bl_options = {"REGISTER", "UNDO"}
    layer_index: IntProperty(default=-1)

    def execute(self, context):
        settings = _settings(context)
        if self.layer_index < 0 or self.layer_index >= len(settings.layers):
            self.report({"ERROR"}, "Biome layer index is out of range")
            return {"CANCELLED"}
        settings.active_index = self.layer_index
        return {"FINISHED"}


class WB_PT_biomes(Panel):
    bl_label = "Biomes"
    bl_idname = "WB_PT_biomes"
    bl_space_type = "VIEW_3D"; bl_region_type = "UI"; bl_category = "WorldBuilder"
    bl_parent_id = "WB_PT_toolkit_overview"; bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        layout, settings, obj = self.layout, _settings(context), _target(context)
        layout.label(text=f"Active Target: {obj.name if obj else 'None'}")
        valid_target = biome.is_biome_target(obj)
        layout.label(text="Biome Target" if valid_target else "Not a Biome Target",
                     icon="CHECKMARK" if valid_target else "ERROR")
        if obj is not None and obj.type == "MESH" and not valid_target:
            layout.operator("worldbuilder.mark_biome_target")
        layout.template_list("WB_UL_biomes", "", settings, "layers", settings, "active_index", rows=5)
        row = layout.row(align=True)
        row.operator("worldbuilder.add_biome_layer", text="", icon="ADD")
        row.operator("worldbuilder.remove_biome_layer", text="", icon="REMOVE")
        row.operator("worldbuilder.duplicate_biome_layer", text="", icon="DUPLICATE")
        definition = _active_definition(context)
        if definition is not None:
            layout.label(text=f"Attribute: {definition.attribute_name}")
            layout.operator("worldbuilder.rename_biome_layer")
        actions = layout.column(align=True); actions.enabled = valid_target and definition is not None
        painter = getattr(context.scene, "worldbuilder_biome_brush", None)
        if painter is not None:
            paint_box = layout.box(); paint_box.label(text="3D Biome Painter")
            start = paint_box.column(); start.enabled = valid_target and definition is not None
            tool=start.operator("wm.tool_set_by_id",text="Start Biome Paint",icon="BRUSH_DATA")
            tool.name="worldbuilder.biome_paint_tool"
            row = paint_box.row(align=True); row.prop(painter, "radius"); row.prop(painter, "strength")
            row = paint_box.row(align=True); row.prop(painter, "falloff"); row.prop(painter, "mode")
            paint_box.prop(painter, "auto_normalize")
            paint_box.prop(painter, "front_faces_only")
            paint_box.prop(painter, "symmetry_x")
            paint_box.prop(painter, "sample_spacing")
            paint_box.prop(painter, "preview_mode")
            paint_box.label(text=f"Changed Vertices: {painter.changed_vertices}")
        row = actions.row(align=True)
        row.operator("worldbuilder.fill_biome_layer"); row.operator("worldbuilder.clear_biome_layer")
        row = actions.row(align=True)
        row.operator("worldbuilder.invert_biome_layer"); row.operator("worldbuilder.normalize_biome_layers")
        layout.operator("worldbuilder.initialize_default_biomes")
        layout.operator("worldbuilder.validate_biomes", icon="CHECKMARK")
        layout.label(text=f"Validation: {settings.validation_message}")


CLASSES = (WBBiomeDefinition, WBBiomeSettings, WB_UL_biomes, WB_OT_mark_biome_target,
           WB_OT_initialize_default_biomes, WB_OT_add_biome, WB_OT_remove_biome,
           WB_OT_rename_biome, WB_OT_duplicate_biome, WB_OT_clear_biome, WB_OT_fill_biome,
           WB_OT_invert_biome, WB_OT_normalize_biomes, WB_OT_validate_biomes,
           WB_OT_set_active_biome, WB_PT_biomes)


def register() -> None:
    for cls in CLASSES: bpy.utils.register_class(cls)
    bpy.types.Scene.worldbuilder_biomes = PointerProperty(type=WBBiomeSettings)
    if biome.depsgraph_update_post not in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.append(biome.depsgraph_update_post)


def unregister() -> None:
    if biome.depsgraph_update_post in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.remove(biome.depsgraph_update_post)
    biome.invalidate_biome_cache()
    if hasattr(bpy.types.Scene, "worldbuilder_biomes"): del bpy.types.Scene.worldbuilder_biomes
    for cls in reversed(CLASSES): bpy.utils.unregister_class(cls)
