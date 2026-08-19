# Changelog

## 1.9.0

- World Analysis is now layer aware. Cells are keyed per vertical layer and drawn at that layer's floor, so caves and stacked corridors no longer collapse into one top-down heat map. Export schema is version 2 and carries a `layer` field.
- Added Traversal Check: player-scale walk and swim probes reporting no-ground, steep, low-ceiling, narrow, and blocked results over the active layer band.
- Added gameplay depth bands with a scene sea level, cursor and object depth readouts, band-floor snapping, and a viewport overlay.
- Added bookmarks: named viewpoints storing chunk and layer, plus a direct go-to-chunk jump.
- Walk probes find the lowest surface in the band instead of the first one hit, so a cave floor under a roof is reported instead of the roof. Probes also extend slightly past the band edges so geometry snapped exactly to a layer boundary registers.
- Removed the traversal step-height setting: headroom is measured from the standing surface, which the old offset systematically under-reported.

## 1.8.0

- Rule Scatter can now emit DOTS entities. A scatter asset entry follows its Structure Library registration by default, so an asset registered as an entity mass-places as one with biome, slope, and height rules applied.
- Added an entity catalog mirror. Load the JSON exported from Unity's Entity Catalog tool to pick prefab ids by name; the pick copies kind, flags, and lifetime so Blender cannot export an entity block that diverges from the Unity prefab.
- Export validation warns when an entity prefab id is missing from the loaded catalog.
- Scatter placement resolution is hoisted out of the per-instance loop.

## 1.7.0

- Added authoring-only vertical layers: uniform Z bands with active-layer stepping, isolation, floor snapping, per-layer selection moves, and a chunk grid overlay that follows the active layer.
- Added an `ENTITY` placement role. Structure Library assets can now declare a DOTS prefab id, `WorldEntityKind`, and entity flags, and placements export an `entity` block plus a `layer` index.
- Layers and entity metadata never change chunk ownership; Unity streaming stays 2D.

## 1.6.0

- Added a reference-driven Reef Formation Builder with mound, layered terrace, spire, and natural-arch composition presets.
- Added standalone rock, seaweed-patch, coral-patch, and complete-reef Collection Asset modes plus deterministic twelve-asset sheets.
- Added ledge-aware sea-life placement, branching low-poly seaweed, tube and branching coral, ground pebbles, and `WB_Sway` vertex animation weights.
- Prepared generated collections directly for the existing bilingual Structure Asset Library and CHUNK/REGION placement workflow.

## 1.5.3

- Made chunk collection and Active Chunk panel queries strictly read-only; missing stable IDs are now created only by explicit validation/export operations.

## 1.5.2

- Removed registration-time Sculpt cleanup scheduling entirely; stale proxies are now cleaned only when a Sculpt Session begins with normal `bpy.data` access.

## 1.5.1

- Fixed Blender Extension installation under `_RestrictData` by deferring stale Sculpt Session cleanup until normal file data access is available.

## 1.5.0

- Added Korean/English production-panel localization without translating stable runtime identifiers.
- Added authoritative Active/Selected/Rectangle chunk terrain generation using deterministic global-coordinate noise and exact shared edges.
- Added welded multi-chunk Sculpt Sessions with outer-boundary masks, topology-change rejection, persistent sculpt deltas, Apply, and Cancel.
- Added a versioned Collection Asset Library, native Blender asset marking, Korean/English display names, CHUNK/REGION ownership, cursor placement, and modal surface placement.
- Connected Collection Instances to the existing `placements.json` and Unity `BlenderAssetRegistry` contract without duplicating source mesh geometry.

## 1.4.0

- Added deterministic triangle clipping for cross-chunk spline output with UV, normal, and material interpolation.
- Added cached scatter terrain sampling, direct-deletion tombstones, bounded terrain carve evaluation, and reusable basis caches.
- Added atomic Bake rollback, chunk-boundary-safe LOD fallback, cancellable foreground Bake progress, and Unity `.bake.json` LODGroup/collider prefab assembly.
- Added a versioned portable Stamp Asset Registry and registry-backed linked-library resolution.
- Improved cave radius variation, cliff overhang/roughness, and authored riverbed cross sections.
- Added foreground Blender GUI registration/screenshot QA plus extended production and performance smoke coverage.

## 1.3.0

- Added biome paint math, cached object-mode painting, brush overlay, and 100k-vertex cache benchmark.
- Added deterministic rule scatter, manual-state tombstones, linked instances, and box/sphere/curve exclusions.
- Added WorldBuilder spline contracts, basis-driven terrain carve/raise, and cave/cliff/path mesh generation.
- Added seam validation/stitch, non-destructive LOD/collider bake manifests, asset stamps, cached analysis, and configurable vertex attributes.

## 1.2.0

- Combined WorldBuilder Chunks, Stylized Terrain Toolkit, and Stylized Rock Generator into one add-on lifecycle.
- Unified all panels under `View3D > Sidebar > WorldBuilder`.
- Added a top-level toolkit overview and quick actions.
- Added terrain integration button for the `NexRock_Generated` collection.
- Preserved existing scene/object property identifiers for migration.
- Preserved shared chunk ownership and bounds calculations used by export and overlay.
