"""WorldBuilder Toolkit — Blender extension entry point.

The distributable archive keeps ``worldbuilder_chunks`` as the implementation package;
this root module satisfies the extension layout requirement (``__init__.py`` next to
``blender_manifest.toml``) and forwards the Blender lifecycle hooks.
"""
from . import worldbuilder_chunks


def register():
    worldbuilder_chunks.register()


def unregister():
    worldbuilder_chunks.unregister()
