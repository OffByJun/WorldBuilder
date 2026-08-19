# Migration from separate add-ons

Disable or remove these older packages before enabling the unified build:

- `WorldBuilder Chunks 1.1.0`
- `Nex Stylized Rock Generator 0.1.0`
- `Nex Stylized Terrain Toolkit 0.1.0`

The unified build preserves these scene properties:

```text
Scene.worldbuilder_chunks
Object.worldbuilder_chunk
Scene.nexrock_settings
Scene.nex_terrain_settings
```

Therefore existing `.blend` settings should remain readable when the old add-ons are replaced rather than installed alongside the unified package.

Generated collections are also preserved:

```text
NexRock_Generated
NexTerrain_Generated
NexTerrain_Scatter
CH_+0000_-0001
```

Recommended replacement procedure:

1. Save and close Blender.
2. Back up the old `worldbuilder_chunks` directory.
3. Remove or disable standalone Rock and Terrain add-ons.
4. Copy the new `worldbuilder_chunks` directory into the WorldBuilder package.
5. Reopen Blender and enable `WorldBuilder Toolkit`.
6. Reload `WorldGrid.profile.json`.
