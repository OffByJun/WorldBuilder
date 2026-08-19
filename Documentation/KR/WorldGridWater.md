# WorldGrid, Blender Bridge, Streaming, Water

## Assembly boundaries

- `WorldBuilder.Runtime`: immutable grid math, streaming contracts, baked water data and queries.
- `WorldBuilder.Authoring`: scene components edited by designers. These are never searched by runtime queries.
- `WorldBuilder.Baking`: deterministic conversion and validation. It may read Authoring data and writes Runtime data.
- `WorldBuilder.Editor`: import hooks, Scene View handles and bake UI. It is excluded from players.
- `WorldBuilder.EditModeTests`: coordinate, manifest, streaming, bake and query regression tests.

Rendering, buoyancy and underwater presentation must depend on `IWaterQueryService`; the query assembly has no dependency on those systems.

## Authoritative grid

Create exactly one asset with `Create > WorldBuilder > World Grid Settings`.

Default contract:

- Authoring chunk: 128 m
- Chunks per region: 4 (512 m region)
- Query cell: 32 m
- World origin: shared Blender/Unity origin

Coordinates are X/Z value types. Conversion uses floor division, including negative coordinates. A cell owns its minimum boundary and excludes its maximum boundary.

## Blender chunk manifest v1

The schema is `Documentation/BlenderChunkManifest.schema.json`.

- Geometry inside FBX is stored relative to the chunk origin.
- `localOrigin` must be `(0, 0, 0)`.
- `chunkSize` must equal the authoritative grid asset.
- `version` must be `1`.
- `contentHash` is used for dirty-import detection.

Files named `*.chunk.json` are validated by the Unity importer. Invalid versions, origins and sizes are rejected in the Console before a bake.

## Streaming

`ChunkStreamingService` calculates required regions from the authoritative grid. `DirectReferenceRegionLoader` is the initial loader and places a referenced region prefab at its calculated region origin. Addressables can be added later by implementing `IRegionContentLoader` without changing the streaming service.

## Water workflow

Add any combination of:

- `OceanWaterBody`
- `RiverWaterBody`
- `LakeWaterBody`
- `LocalWaterVolume`
- `AirOverrideVolume`

River and lake points, box extents and ocean level are visible in Scene View. Each component owns a persistent stable ID. Duplicate IDs are bake errors.

Run `Tools > WorldBuilder > Water > Bake Scene Query Data`. The bake sorts bodies by stable ID, partitions exact-test candidates into query cells, writes compact arrays and stores a deterministic SHA-256 hash.

Runtime lookup order is priority-based, not type-based. An ocean is the O(1) baseline (`position.y < seaLevel`); only the current query cell's river segments, lake polygons and box overrides receive exact tests. No segment GameObjects, Trigger Colliders, scene searches or LINQ are used by `WaterQueryService`.

For nested spaces, assign increasing priorities, for example:

- Ocean: 0
- River/Lake: 10
- Local water: 20
- Air override: 100
- Water inside an air override: 200
