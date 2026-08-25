"""Generate every cave preset and verify geometry, determinism and biome attributes."""
from pathlib import Path
import sys

import bpy

ADDON_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADDON_ROOT))

import worldbuilder_chunks
from worldbuilder_chunks import cave_generator

worldbuilder_chunks.register()
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
settings = scene.worldbuilder_cave

results = []
for index, preset in enumerate(("LIMESTONE", "LAVA_TUBES", "FLOODED_GROTTO", "ABYSSAL_NETWORK")):
    settings.preset = preset
    settings.seed = 900 + index
    collection, created = cave_generator.generate(bpy.context, settings)

    assert len(created) == int(settings.tunnel_count), (preset, len(created))
    triangles = sum(len(obj.data.polygons) for obj in created)
    assert triangles > 200, (preset, triangles)

    # Closed manifold-ish: every tube carries the cave biome attribute at weight 1.
    for obj in created:
        attribute = obj.data.attributes.get("WB_BIOME_CAVE")
        assert attribute is not None, obj.name
        assert all(item.value == 1.0 for item in attribute.data), obj.name
        assert obj.get("wb_role") == "TERRAIN", obj.name
        assert obj.get("wb_shader_family") == "CAVE", obj.name

    # Determinism: same seed rebuilds identical vertex counts.
    first_counts = [len(obj.data.vertices) for obj in created]
    cave_generator.clear_generated(bpy.context)
    _, recreated = cave_generator.generate(bpy.context, settings)
    assert [len(obj.data.vertices) for obj in recreated] == first_counts, preset

    # Bounds: every vertex stays inside a sane envelope of the requested volume.
    # Room bulges legitimately exceed the walk-path margin by up to room_scale × radius.
    bulge = settings.radius * settings.room_scale * (1.0 + settings.radius_variance)
    centre = bpy.context.scene.cursor.location.copy() if hasattr(bpy.context.scene, "cursor") else None
    limit_x = settings.width * 0.5 + bulge
    limit_y = settings.depth * 0.5 + bulge
    limit_z = settings.height * 0.5 + bulge
    for obj in recreated:
        for vertex in obj.data.vertices:
            if centre is not None:
                local = vertex.co - centre
                assert abs(local.x) <= limit_x, (preset, "x", vertex.co)
                assert abs(local.y) <= limit_y, (preset, "y", vertex.co)
                assert abs(local.z) <= limit_z, (preset, "z", vertex.co)

    results.append((preset, len(created), triangles))
    cave_generator.clear_generated(bpy.context)
    assert not any(obj.get("wb_cave_generated") for obj in bpy.data.objects), preset

print("WB_CAVE_GENERATOR_OK", results)
worldbuilder_chunks.unregister()
