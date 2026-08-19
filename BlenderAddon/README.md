# WorldBuilder Toolkit 1.9.0

Unified Blender add-on for the Survival / WorldBuilder pipeline.

It combines:

1. **WorldBuilder Chunks 1.1**
   - Unity-authored `WorldGrid.profile.json`
   - Chunk and Region GPU overlays
   - Active chunk selection
   - Ownership, bounds validation, Dirty state, and FBX export
   - Query-cell and streaming previews

2. **Stylized Terrain Toolkit 0.1**
   - Procedural reef, ridge, canyon, plateau, and island terrain
   - Chunked terrain generation
   - Existing-mesh stylization
   - Faceted material palette
   - Linked prop scattering

3. **Stylized Rock Generator 0.1**
   - Boulder, pillar, terrace, jagged, slab, cluster, and arch assets
   - Low-poly faceted material assignment
   - Optional sand, coral, and seaweed details
   - Twelve-asset reference sheet generation

4. **Production authoring tools**
   - Cached biome painting with POINT/FLOAT weights
   - Deterministic rule scatter, tombstones, and exclusion volumes
   - Curve authoring, basis-driven terrain carve, cave/cliff/path meshes
   - Chunk seam validation and safe equal-resolution stitching
   - Non-destructive LOD/collider bake and deterministic manifests
   - Portable asset stamps, cached analysis, and configurable vertex-color bake
   - Authoritative selected-chunk terrain generation and welded multi-chunk Sculpt sessions
   - Korean/English production UI and Collection Instance structure library

5. **Reference-driven Reef Formation Builder**
   - Layered mound, terrace, spire, and natural-arch silhouettes
   - Dedicated faceted cliff blocks with editable module composition
   - Standalone seaweed and coral patch Collection Assets for Rule Scatter
   - Ledge-aware branching seaweed, tube sponge, branching coral, and pebble dressing
   - `WB_Sway` vertex colors and seaweed shader metadata for Unity animation
   - Deterministic twelve-asset variation-sheet generation

All UI is under:

```text
3D Viewport > N Panel > WorldBuilder
```

## Installation in the Survival repository

Back up the existing folder, then replace:

```text
Packages/com.emiteat.worldbuilder/BlenderAddon/worldbuilder_chunks/
```

with the `worldbuilder_chunks` folder from this package.

Do not keep the old standalone Rock Generator or Terrain Toolkit enabled at the same time. Their operator and property identifiers are included in this unified add-on and duplicate registration can fail.

## Blender extension installation

Use the separate `worldbuilder_toolkit-1.9.0.zip` build:

```text
Edit > Preferences > Extensions > Install from Disk
```

## Recommended workflow

1. Load `WorldGrid.profile.json` and confirm **Synced**.
2. Generate or stylize terrain.
3. Generate a rock set or a twelve-rock sheet.
4. In **Populate Terrain**, click **Use Generated Rocks**.
5. Paint biome weights and generate rule-based scatter previews.
6. Use splines for terrain carve and generated path/cave/cliff meshes.
7. Validate chunk seams, then bake LOD/collider outputs.
8. Assign generated terrain and props to `CH_*` collections.
9. Validate chunk ownership and bounds.
10. Export the required chunk set.

## Important integration behavior

- The add-on is one Blender package and one registration lifecycle.
- Rock and terrain panels share the `WorldBuilder` sidebar.
- Terrain scatter can directly select `NexRock_Generated`.
- Chunk visualization remains a GPU overlay and does not create grid mesh objects.
- Export ownership and overlay ownership continue to share `exporter.object_chunk()` and `exporter.world_bounds()`.

The procedural terrain generator still has its own generation width and chunk count. It does **not** silently overwrite the authoritative Unity grid profile. Generated objects must be assigned to WorldBuilder `CH_*` collections before export.

## Compatibility

- Target: Blender 4.3+
- Python syntax tested in the build environment
- Coordinate and profile unit tests included
- Blender 5.1.2 headless runtime, save/reload, generated-data, and FBX export smoke tests pass
- Blender 4.3 remains the minimum target but was not installed on the verification machine

## Deliberate limits

- Generated spline meshes are clipped deterministically at chunk planes with interpolated position, UV, normal, and material ownership. Arbitrary user geometry is still validated rather than silently modified.
- Automatic seam topology reconstruction and UV stitching are not performed. Position stitching is enabled only when edge resolution and correspondence are safe.
- `MULTI_CONVEX` is not exposed because no VHACD dependency is bundled.
- AO, cavity, and curvature vertex sources are approximation channels, not ray-traced physical bakes.
- Stamp object records resolve portable `assetId` values through a versioned registry. Optional terrain-height deltas and biome samples are matched in world space against an explicitly selected patch target; unmatched samples are skipped rather than changing topology.
- LOD sources touching chunk boundaries fall back to their original density when Decimate cannot prove boundary preservation. This favors seam correctness over triangle reduction.

## Smoke test

1. Enable only this add-on.
2. Open the `WorldBuilder` N-panel.
3. Load the example grid profile.
4. Verify positive and negative chunk boundaries.
5. Generate one rock and one terrain chunk set.
6. Click **Use Generated Rocks**, then scatter onto the active terrain.
7. Assign a generated object to an active chunk.
8. Validate and export one test chunk.

## License

MIT. See `LICENSE`.
