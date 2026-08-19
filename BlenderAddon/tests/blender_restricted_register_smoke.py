"""Match Blender Extension enable/disable registration restrictions."""
from pathlib import Path
import sys

from _bpy_restrict_state import RestrictBlend

ADDON_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADDON_ROOT))

import worldbuilder_chunks

with RestrictBlend():
    worldbuilder_chunks.register()

worldbuilder_chunks.unregister()
print("WB_RESTRICT_REGISTER_OK")
