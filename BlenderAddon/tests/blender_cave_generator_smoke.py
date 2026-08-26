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

    tunnels = [obj for obj in created if obj.type == "MESH"]
    markers = [obj for obj in created if obj.get("wb_cave_entrance") is not None]
    assert len(tunnels) == int(settings.tunnel_count), (preset, len(tunnels))
    assert len(markers) == len(tunnels), (preset, len(markers))  # markers on by default
    assert all(marker.name.startswith("CaveEntrance_") for marker in markers), preset
    triangles = sum(len(obj.data.polygons) for obj in tunnels)
    assert triangles > 200, (preset, triangles)

    # Closed manifold-ish: every tube carries the cave biome attribute at weight 1.
    for obj in tunnels:
        attribute = obj.data.attributes.get("WB_BIOME_CAVE")
        assert attribute is not None, obj.name
        assert all(item.value == 1.0 for item in attribute.data), obj.name
        assert obj.get("wb_role") == "TERRAIN", obj.name
        assert obj.get("wb_shader_family") == "CAVE", obj.name

    # Determinism: same seed rebuilds identical vertex counts.
    first_counts = [len(obj.data.vertices) for obj in tunnels]
    cave_generator.clear_generated(bpy.context)
    _, recreated = cave_generator.generate(bpy.context, settings)
    recreated_tunnels = [obj for obj in recreated if obj.type == "MESH"]
    assert [len(obj.data.vertices) for obj in recreated_tunnels] == first_counts, preset
    assert any(obj.get("wb_cave_entrance") is not None for obj in recreated), preset

    # Bounds: every vertex stays inside a sane envelope of the requested volume.
    # Room bulges legitimately exceed the walk-path margin by up to room_scale × radius.
    bulge = settings.radius * settings.room_scale * (1.0 + settings.radius_variance)
    centre = bpy.context.scene.cursor.location.copy() if hasattr(bpy.context.scene, "cursor") else None
    limit_x = settings.width * 0.5 + bulge
    limit_y = settings.depth * 0.5 + bulge
    limit_z = settings.height * 0.5 + bulge
    for obj in recreated_tunnels:
        for vertex in obj.data.vertices:
            if centre is not None:
                local = vertex.co - centre
                assert abs(local.x) <= limit_x, (preset, "x", vertex.co)
                assert abs(local.y) <= limit_y, (preset, "y", vertex.co)
                assert abs(local.z) <= limit_z, (preset, "z", vertex.co)

    results.append((preset, len(tunnels), len(markers), triangles))
    cave_generator.clear_generated(bpy.context)
    assert not any(obj.get("wb_cave_generated") for obj in bpy.data.objects), preset

print("WB_CAVE_GENERATOR_OK", results)
worldbuilder_chunks.unregister()
