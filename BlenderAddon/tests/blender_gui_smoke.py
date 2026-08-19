"""Foreground Blender UI registration/screenshot smoke. Blender exits automatically."""
from pathlib import Path
import json
import os
import sys
import tempfile
import traceback
import bpy

ADDON_ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ADDON_ROOT))
import worldbuilder_chunks

OUTPUT=Path(os.environ.get("WB_GUI_SMOKE_OUTPUT",Path(tempfile.gettempdir())/"worldbuilder_gui_smoke"))
OUTPUT.mkdir(parents=True,exist_ok=True)

def run():
    result={"blenderVersion":list(bpy.app.version),"background":bpy.app.background,"panels":{},"operators":{},"success":False}
    try:
        if bpy.app.background:raise RuntimeError("GUI smoke must run without --background")
        worldbuilder_chunks.register()
        bpy.context.scene.worldbuilder_chunks.ui_language="KO"
        panels=("WB_PT_toolkit_overview","WB_PT_world_grid","WB_PT_vertical_layers","WB_PT_water_depth","WB_PT_bookmarks","WB_PT_entity_catalog","WB_PT_traversal","WB_PT_active_chunk","WB_PT_chunk_terrain","WB_PT_sculpt_session","WB_PT_structure_library","WB_PT_reef_generator","WB_PT_rule_scatter","WB_PT_splines","WB_PT_bake","WB_PT_stamps")
        for name in panels:result["panels"][name]=hasattr(bpy.types,name)
        operators=("worldbuilder.validate_chunks","worldbuilder.generate_chunk_terrain","worldbuilder.sculpt_session_begin","worldbuilder.structure_place_surface","worldbuilder.generate_reef_asset","worldbuilder.generate_reef_sheet","worldbuilder.export_dirty_chunks","worldbuilder.scatter_preview","worldbuilder.spline_generate_mesh","worldbuilder.bake_run","worldbuilder.stamp_place")
        for value in operators:
            namespace,name=value.split(".");result["operators"][value]=hasattr(getattr(bpy.ops,namespace),name)
        pair=next(((window,area) for window in bpy.context.window_manager.windows for area in window.screen.areas if area.type=="VIEW_3D"),None)
        if pair is None:raise RuntimeError("No VIEW_3D area is available")
        window,area=pair
        area.spaces.active.show_region_ui=True
        sidebar=next((region for region in area.regions if region.type=="UI"),None)
        area.tag_redraw()
        with bpy.context.temp_override(window=window,area=area):
            bpy.ops.screen.screenshot(filepath=str(OUTPUT/"worldbuilder-ui.png"))
        if not all(result["panels"].values()) or not all(result["operators"].values()):raise RuntimeError("A required panel or operator was not registered")
        result["success"]=True
    except Exception as error:
        result["error"]=str(error);result["traceback"]=traceback.format_exc()
    finally:
        (OUTPUT/"result.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
        print("WorldBuilder GUI smoke",result)
        bpy.ops.wm.quit_blender()
    return None

bpy.app.timers.register(run,first_interval=1.0)
