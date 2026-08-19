# Biome data layer

Biome definitions are stored once per Blender Scene in `Scene.worldbuilder_biomes`.
Each WorldBuilder terrain mesh stores one independent `POINT/FLOAT` mesh attribute per biome:

```text
WB_BIOME_SAND
WB_BIOME_ROCK
WB_BIOME_KELP
```

Biome targets carry these custom properties:

```python
obj["wb_biome_target"] = True
obj["wb_biome_schema_version"] = 1
```

Generated and stylized terrain is tagged automatically. Use **WorldBuilder > Biomes > Initialize Default Biomes**
to create the default definitions and attributes on the active target.

`biome.py` is the only supported access layer for attribute reads, writes, validation, normalization, and
world-position sampling. `sample_biome_weight_world()` uses a cached evaluated-surface BVH and rejects
topology-changing modifiers whose evaluated vertices cannot map safely back to the authored POINT attributes.

This stage intentionally does not include biome painting, scatter rules, or Unity biome-file export. It provides
the deterministic `build_biome_manifest()` data foundation for those later stages.
