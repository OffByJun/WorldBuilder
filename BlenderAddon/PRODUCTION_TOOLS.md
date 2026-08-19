# Production Authoring Contracts

All editable source data stays separate from generated data.

| Source | Generated output | Runtime/export contract |
|---|---|---|
| `WB_BIOME_*` POINT/FLOAT attributes | none | sampled and exported without material replacement |
| Scatter layer | `WB_SCATTER_<stable_id>` linked objects | stable sample/instance IDs and `INSTANCE` role |
| WorldBuilder Curve | hidden terrain basis and `WB_SPLINE_GENERATED` meshes | source spline ID and generated-kind tags |
| Authoring mesh | `WB_BAKE_<profile_id>` duplicates | LOD/collider tags and schema-1 bake JSON |
| Selection | `.wbstamp.json` | asset IDs plus relative transforms |
| Analysis request | in-memory cell cache | optional schema-1 JSON export |
| Bake preset | named FLOAT_COLOR attribute | explicit RGBA channel contract custom property |

Generation and analysis are explicit operations. Draw callbacks only render cached information. Legacy terrain scatter remains available and is not deleted by the rule-scatter implementation.

Unity's `BakeManifestCodec` validates `.bake.json` files against the authoritative `WorldGridSettings`, including stable IDs, ordered LOD records, collider references, profile SHA-256, and the named RGBA vertex-attribute contract. The editor importer reports invalid manifests without modifying authoring assets.

## Tests

- `python -m unittest discover -s BlenderAddon/tests -p "test_*.py"`
- `blender --background --factory-startup --python BlenderAddon/tests/blender_biome_smoke.py`
- `blender --background --factory-startup --python BlenderAddon/tests/blender_biome_performance_smoke.py`
- `blender --background --factory-startup --python BlenderAddon/tests/blender_production_smoke.py`
- Set `WB_SMOKE_OUTPUT` before running `blender_smoke.py` for the FBX round-trip.
