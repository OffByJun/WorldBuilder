# WorldBuilder Entities/DOTS

WorldBuilder uses Entities for high-count world objects. Player input, UI, quests, and other presentation-heavy code may remain GameObject-based and communicate through the command/snapshot boundary.

## Ownership

- `WorldEntityAuthoring` owns stable identity, prefab id, kind, persistence, and initial simulation data.
- `WorldEntityRuntimeAuthoring` owns the authoritative `WorldGridSettings` reference and prefab catalog.
- Bakers convert authoring objects and prefabs into entity data. Authoring components do not participate in runtime queries.
- `WorldEntitySpawnSystem` consumes a compact dynamic-buffer command queue.
- `WorldEntityVelocitySystem`, `WorldEntityLifetimeSystem`, and `WorldEntityChunkOwnershipSystem` are Burst/jobified hot-path systems.
- `WorldEntityRegionActivationSystem` enables only region-resident entities whose region is loaded.

## Scene setup

1. Create a SubScene for entity authoring content.
2. Add one `WorldEntityRuntimeAuthoring` and assign the authoritative grid asset.
3. Add entity prefabs to its catalog. Every prefab must have `WorldEntityAuthoring`, and catalog/prefab ids must match.
4. Put `WorldEntityAuthoring` on high-count resources, dropped items, creatures, projectiles, and effects.
5. Use Entities Graphics authoring components for rendering and Unity Physics authoring components for DOTS collision.
6. Connect the existing `ChunkStreamingService` to `WorldEntityRegionObserver`, or use `WorldEntityRegionFocus` as a standalone focus bridge.

## Blender authoring

Structure Library assets declare a `placementKind`. `ENTITY` assets place objects with the `ENTITY` role instead of `INSTANCE`, and export an `entity` block carrying `prefabId`, `kind`, `flags`, and `lifetimeSeconds` alongside the usual asset id and matrix. Every placement also carries an authoring `layer` index; layers are a vertical authoring aid only and never affect chunk ownership or streaming.

`ChunkImportPipeline` refuses to import an `ENTITY` placement whose registry prefab lacks `WorldEntityAuthoring`, or whose `WorldEntityAuthoring.PrefabId` differs from the Blender `prefabId`. Accepted placements are parented under an `Entities` node on the chunk prefab and tagged with `ChunkEntityPlacement`, which preserves the stable id, asset id, prefab id, kind, and authoring layer. Put that node in a SubScene to bake it.

`WorldBuilder > World > Entity Catalog` cross-checks open `WorldEntityRuntimeAuthoring` catalogs against imported placements: duplicate or mismatched prefab ids, catalog prefabs missing `WorldEntityAuthoring`, and placements whose prefab id is absent from the catalog.

## Runtime boundary

Use `WorldEntityCommandQueue.TrySpawn` from GameObject/VContainer code. Do not instantiate entity prefabs as GameObjects at runtime. Save code calls `WorldEntitySnapshotService.TryCapture`; only entities marked `Persistent` are captured.

Systems that simulate world entities must require the enableable `WorldEntityActive` component. Region activation is therefore data-only and does not create one manager or trigger per entity.

Resource-node, field-spawn, tool validation, dropped-item, partial inventory transfer, respawn, and persistence details are documented in `Resources.md`.

## Deliberate exclusions

- The player, UI, inventory UI, quests, and cinematic cameras are not automatically converted.
- Complex creature behavior, combat, harvesting, and item pickup are feature systems layered on this foundation.
- Netcode is not installed. `Replicated` is a data contract for a future networking module, not an active replication implementation.
