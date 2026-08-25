using System;
using Unity.Mathematics;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>One-click cave archetypes for CaveShapeParams.</summary>
    public enum CavePreset
    {
        LimestoneCaves,
        LavaTubes,
        FloodedGrotto,
        AbyssalNetwork
    }

    /// <summary>
    /// Authoring parameters for procedural cave carving. Pure serializable fields so the
    /// whole shape can be versioned, diffed and hashed alongside TerrainShapeParams.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Terrain/Cave Shape Params", fileName = "CaveShapeParams")]
    public sealed class CaveShapeParams : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Combined with the terrain seed to decorrelate cave noise from the surface.")]
        public int seedOffset = 911;

        [Header("Vertical Range")]
        [Tooltip("Lowest world Y where carving may occur.")]
        public float minY = -46f;
        [Tooltip("Highest world Y where carving may occur.")]
        public float maxY = 26f;
        [Tooltip("Minimum solid cover in meters below the heightmap surface before carving starts.")]
        [Min(0.5f)] public float surfaceProtectDepth = 6f;

        [Header("Tunnels (spaghetti)")]
        [Tooltip("Horizontal feature size of tunnel noise in meters.")]
        public float tunnelScale = 42f;
        [Tooltip("Band width on |noise|; larger = wider tunnels.")]
        [Range(0.02f, 0.5f)] public float tunnelWidth = 0.16f;
        [Tooltip("Domain warp strength in meters — higher means more winding passages.")]
        public float tunnelWinding = 24f;
        [Tooltip("Squashes tunnels vertically; >1 produces walkable flat tubes.")]
        [Range(0.25f, 4f)] public float tunnelVerticalSquash = 1.6f;

        [Header("Rooms (caverns)")]
        [Tooltip("Feature size of cavern blobs in meters.")]
        public float roomScale = 90f;
        [Tooltip("Base threshold for room carving; lower = more and bigger rooms.")]
        [Range(0.3f, 0.9f)] public float roomThreshold = 0.62f;
        [Tooltip("Threshold offset at max depth (negative = caverns grow deeper).")]
        [Range(-0.25f, 0.25f)] public float roomDepthBias = -0.08f;

        [Header("Strength")]
        [Tooltip("Multiplier applied to the combined carve field before subtraction.")]
        [Range(0.5f, 2f)] public float carveSharpness = 1.15f;

        private void Reset() => seedOffset = UnityEngine.Random.Range(1, 999999);
    }

    public static class CavePresets
    {
        public static void Apply(CaveShapeParams p, CavePreset preset)
        {
            switch (preset)
            {
                case CavePreset.LimestoneCaves:
                    p.seedOffset = UnityEngine.Random.Range(1, 999999);
                    p.minY = -40f;
                    p.maxY = 30f;
                    p.surfaceProtectDepth = 6f;
                    p.tunnelScale = 38f;
                    p.tunnelWidth = 0.17f;
                    p.tunnelWinding = 28f;
                    p.tunnelVerticalSquash = 1.5f;
                    p.roomScale = 85f;
                    p.roomThreshold = 0.60f;
                    p.roomDepthBias = -0.07f;
                    p.carveSharpness = 1.15f;
                    break;

                case CavePreset.LavaTubes:
                    p.seedOffset = UnityEngine.Random.Range(1, 999999);
                    p.minY = -20f;
                    p.maxY = 34f;
                    p.surfaceProtectDepth = 5f;
                    p.tunnelScale = 55f;
                    p.tunnelWidth = 0.13f;
                    p.tunnelWinding = 12f;
                    p.tunnelVerticalSquash = 2.6f;
                    p.roomScale = 120f;
                    p.roomThreshold = 0.74f;
                    p.roomDepthBias = -0.04f;
                    p.carveSharpness = 1.3f;
                    break;

                case CavePreset.FloodedGrotto:
                    p.seedOffset = UnityEngine.Random.Range(1, 999999);
                    p.minY = -44f;
                    p.maxY = 8f;
                    p.surfaceProtectDepth = 7f;
                    p.tunnelScale = 46f;
                    p.tunnelWidth = 0.19f;
                    p.tunnelWinding = 32f;
                    p.tunnelVerticalSquash = 1.2f;
                    p.roomScale = 70f;
                    p.roomThreshold = 0.52f;
                    p.roomDepthBias = -0.10f;
                    p.carveSharpness = 1.2f;
                    break;

                case CavePreset.AbyssalNetwork:
                    p.seedOffset = UnityEngine.Random.Range(1, 999999);
                    p.minY = -48f;
                    p.maxY = 18f;
                    p.surfaceProtectDepth = 8f;
                    p.tunnelScale = 72f;
                    p.tunnelWidth = 0.22f;
                    p.tunnelWinding = 44f;
                    p.tunnelVerticalSquash = 1.35f;
                    p.roomScale = 110f;
                    p.roomThreshold = 0.58f;
                    p.roomDepthBias = -0.09f;
                    p.carveSharpness = 1.1f;
                    break;
            }
        }
    }

    /// <summary>
    /// Procedural cave carving over an existing density store: intersecting warped
    /// "spaghetti" tunnel bands plus depth-biased cavern rooms. Fully deterministic and
    /// subtractive, so it composes with heightfield generation, erosion and runtime digs.
    /// </summary>
    public static class CaveField
    {
        private const float EdgeFadeBand = 0.12f;

        /// <summary>
        /// Carve amount in [0..1] for a world point given its column's heightmap surface.
        /// 0 leaves the voxel untouched.
        /// </summary>
        public static float CarveAmountAt(FbmNoise noise, CaveShapeParams p, float3 world,
            float surfaceHeight)
        {
            if (world.y < p.minY || world.y > p.maxY) return 0f;

            float cover = surfaceHeight - world.y;
            if (cover < p.surfaceProtectDepth) return 0f;

            // 0 at the top of the range, 1 at the bottom.
            float depth01 = Mathf.InverseLerp(p.maxY, p.minY, world.y);
            float edge = Mathf.Min(depth01, 1f - depth01);
            float edgeFade = Mathf.Clamp01(edge / EdgeFadeBand);

            float carve = Mathf.Max(TunnelField(noise, p, world), RoomField(noise, p, world, depth01));
            return Mathf.Clamp01(carve * edgeFade * p.carveSharpness);
        }

        /// <summary>
        /// Subtracts the cave field from every solid voxel inside the heightmap footprint.
        /// Returns the number of voxels changed. Existing chunks are modified in place;
        /// missing chunks are skipped (nothing to carve).
        /// </summary>
        public static int Carve(VoxelStoreAsset store, TerrainField.HeightMap heights,
            TerrainShapeParams shape, CaveShapeParams caves, float chunkSize)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (heights == null) throw new ArgumentNullException(nameof(heights));
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (caves == null) throw new ArgumentNullException(nameof(caves));

            int resolution = store.Resolution;
            var noise = new FbmNoise(shape.seed + caves.seedOffset);
            int chunkCells = Mathf.RoundToInt(chunkSize);

            int minX = FloorDiv(Mathf.FloorToInt(heights.Origin.x), chunkCells);
            int minZ = FloorDiv(Mathf.FloorToInt(heights.Origin.y), chunkCells);
            int spanChunks = Mathf.CeilToInt((heights.Size - 1) * heights.CellSize / chunkSize);

            int changed = 0;

            for (int cx = minX; cx <= minX + spanChunks; cx++)
            {
                for (int cz = minZ; cz <= minZ + spanChunks; cz++)
                {
                    int minYLayer = FloorDiv(Mathf.FloorToInt(caves.minY), chunkCells);
                    int maxYLayer = FloorDiv(Mathf.CeilToInt(Mathf.Min(caves.maxY, MaxColumnHeight(heights, cx, cz, chunkSize))), chunkCells);

                    for (int cy = minYLayer; cy <= maxYLayer; cy++)
                    {
                        Vector3Int coord = new Vector3Int(cx, cy, cz);
                        if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) continue;

                        Vector3 origin = new Vector3(cx * chunkSize, cy * chunkSize, cz * chunkSize);
                        float spacing = chunkSize / resolution;

                        for (int x = 0; x < resolution; x++)
                        {
                            for (int z = 0; z < resolution; z++)
                            {
                                float worldX = origin.x + x * spacing;
                                float worldZ = origin.z + z * spacing;
                                float surface = heights.SampleWorld(new Vector2(worldX, worldZ));

                                for (int y = 0; y < resolution; y++)
                                {
                                    float density = entry.density[
                                        x + resolution * (y + resolution * z)];
                                    if (density <= 0f) continue;

                                    float carve = CarveAmountAt(noise, caves,
                                        new float3(worldX, origin.y + y * spacing, worldZ), surface);
                                    if (carve <= 0f) continue;

                                    float updated = Mathf.Max(0f, density - carve);
                                    if (Mathf.Approximately(density, updated)) continue;

                                    store.SetDensity(entry, x, y, z, updated);
                                    changed++;
                                }
                            }
                        }
                    }
                }
            }
            return changed;
        }

        /// <summary>Intersecting warped |noise| bands — classic winding spaghetti tunnels.</summary>
        private static float TunnelField(FbmNoise noise, CaveShapeParams p, float3 world)
        {
            float scale = Mathf.Max(1f, p.tunnelScale);
            float frequency = 1f / scale;

            float3 warp = new float3(
                noise.Value3D(world + new float3(57.19f, 13.77f, 91.53f),
                    1f / (scale * 1.7f), 2, 0.5f, 2f),
                noise.Value3D(world + new float3(-83.21f, 41.05f, -27.63f),
                    1f / (scale * 1.9f), 2, 0.5f, 2f),
                noise.Value3D(world + new float3(11.87f, -95.42f, 64.18f),
                    1f / (scale * 1.6f), 2, 0.5f, 2f));

            float3 samplePoint = (world + warp * p.tunnelWinding) *
                                 new float3(1f, p.tunnelVerticalSquash, 1f);

            float n1 = noise.Value3D(samplePoint, frequency, 2, 0.5f, 2f);
            float n2 = noise.Value3D(samplePoint + new float3(133.7f, -71.3f, 55.9f), frequency, 2, 0.5f, 2f);

            float band = Mathf.Max(Mathf.Abs(n1), Mathf.Abs(n2));
            return 1f - Mathf.SmoothStep(p.tunnelWidth * 0.55f, p.tunnelWidth, band);
        }

        /// <summary>Low-frequency fBm blobs; threshold eases with depth via roomDepthBias.</summary>
        private static float RoomField(FbmNoise noise, CaveShapeParams p, float3 world, float depth01)
        {
            float scale = Mathf.Max(1f, p.roomScale);
            float value = noise.Value3D(
                world * new float3(1f, 0.82f, 1f) + new float3(-217.31f, 88.14f, 149.02f),
                1f / scale, 3, 0.5f, 2f);
            value = value * 0.5f + 0.5f;

            float threshold = Mathf.Clamp01(p.roomThreshold + p.roomDepthBias * depth01);
            return 1f - Mathf.SmoothStep(threshold - 0.06f, threshold + 0.02f, value);
        }

        private static float MaxColumnHeight(TerrainField.HeightMap heights, int cx, int cz,
            float chunkSize)
        {
            const int samples = 8;
            float maxHeight = float.MinValue;
            for (int sz = 0; sz <= samples; sz++)
            {
                for (int sx = 0; sx <= samples; sx++)
                {
                    float wx = cx * chunkSize + Mathf.Min(sx * (chunkSize / samples), chunkSize);
                    float wz = cz * chunkSize + Mathf.Min(sz * (chunkSize / samples), chunkSize);
                    float h = heights.SampleWorld(new Vector2(wx, wz));
                    if (h > maxHeight) maxHeight = h;
                }
            }
            return maxHeight;
        }

        private static int FloorDiv(int value, int divisor) =>
            divisor <= 0 ? value : (value >= 0 ? value / divisor : (value - divisor + 1) / divisor);
    }
}
