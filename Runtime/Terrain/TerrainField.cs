using System;
using Unity.Mathematics;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Heightfield services shared by generation, erosion and PCG: builds a heightmap for
    /// a region from shape params, and converts heights into voxel-store density.
    /// </summary>
    public static class TerrainField
    {
        public sealed class HeightMap
        {
            public readonly float[] Heights;
            public readonly int Size;
            public readonly Vector2 Origin;      // world XZ of cell [0,0]
            public readonly float CellSize;

            public HeightMap(float[] heights, int size, Vector2 origin, float cellSize)
            {
                Heights = heights;
                Size = size;
                Origin = origin;
                CellSize = cellSize;
            }

            public float At(int x, int z) => Heights[z * Size + x];

            public void Set(int x, int z, float value) => Heights[z * Size + x] = value;

            public float SampleWorld(Vector2 worldXz)
            {
                float fx = (worldXz.x - Origin.x) / CellSize;
                float fz = (worldXz.y - Origin.y) / CellSize;
                int x0 = Mathf.FloorToInt(fx);
                int z0 = Mathf.FloorToInt(fz);
                float tx = fx - x0;
                float tz = fz - z0;
                float h00 = AtClamped(x0, z0);
                float h10 = AtClamped(x0 + 1, z0);
                float h01 = AtClamped(x0, z0 + 1);
                float h11 = AtClamped(x0 + 1, z0 + 1);
                return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
            }

            private float AtClamped(int x, int z)
            {
                x = Mathf.Clamp(x, 0, Size - 1);
                z = Mathf.Clamp(z, 0, Size - 1);
                return At(x, z);
            }
        }

        public static float HeightAt(FbmNoise noise, TerrainShapeParams p, float2 worldXz)
        {
            float baseNoise = noise.Warped2D(
                worldXz,
                1f / Mathf.Max(1f, p.featureScale),
                p.octaves,
                p.persistence,
                p.lacunarity,
                p.warpStrength / Mathf.Max(1f, p.featureScale),
                p.warpFrequency * Mathf.Max(1f, p.featureScale));

            float height = p.baseHeight + baseNoise * p.heightAmplitude;

            if (p.ridgeWeight > 0f)
            {
                float ridge = noise.Ridged2D(worldXz, 1f / (Mathf.Max(1f, p.featureScale) * 0.37f), 3, 0.5f, 2.2f);
                height += ridge * p.ridgeWeight * p.heightAmplitude * 0.5f;
            }

            if (p.terraceBlend > 0f)
            {
                float stepMeters = 4f;
                float terraced = MathF.Round(height / stepMeters) * stepMeters;
                height = Mathf.Lerp(height, terraced, p.terraceBlend);
            }

            if (p.islandRadius > 0f)
            {
                float distance = math.length(worldXz);
                float falloff = 1f - math.smoothstep(p.islandRadius * 0.55f, p.islandRadius, distance);
                height = math.lerp(p.bottomClampY, height, falloff);
            }

            return height;
        }

        /// <summary>Builds a padded heightmap covering [origin .. origin+size*cellSize].</summary>
        public static HeightMap BuildHeightMap(TerrainShapeParams parameters, Vector2 origin, int size, float cellSize)
        {
            var noise = new FbmNoise(parameters.seed);
            float[] heights = new float[size * size];
            var map = new HeightMap(heights, size, origin, cellSize);
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    heights[z * size + x] = HeightAt(noise, parameters,
                        new float2(origin.x + x * cellSize, origin.y + z * cellSize));
                }
            }
            return map;
        }

        /// <summary>
        /// Writes density (solidity, matching VoxelPaintTool semantics: 1 = solid) into the
        /// store for every chunk overlapping the heightmap footprint between bottom clamp
        /// and the highest surface.
        /// Returns the number of chunks written.
        /// </summary>
        public static int WriteDensity(VoxelStoreAsset store, HeightMap heights, TerrainShapeParams p,
            float chunkSize, int resolution)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            resolution = Mathf.Clamp(resolution, 8, 64);

            int minX = FloorDiv(Mathf.FloorToInt(heights.Origin.x), Mathf.RoundToInt(chunkSize));
            int minZ = FloorDiv(Mathf.FloorToInt(heights.Origin.y), Mathf.RoundToInt(chunkSize));
            int spanChunks = Mathf.CeilToInt((heights.Size - 1) * heights.CellSize / chunkSize);

            int written = 0;
            float sharpness = p.surfaceSharpness / Mathf.Max(0.001f, chunkSize / resolution);

            for (int cx = minX; cx <= minX + spanChunks; cx++)
            {
                for (int cz = minZ; cz <= minZ + spanChunks; cz++)
                {
                    // Determine vertical extent of interest for this chunk column.
                    float minHeight = float.MaxValue;
                    float maxHeight = float.MinValue;
                    for (int sz = 0; sz <= resolution; sz++)
                    {
                        for (int sx = 0; sx <= resolution; sx++)
                        {
                            float wx = (cx * chunkSize) + Mathf.Min(sx * (chunkSize / resolution), chunkSize);
                            float wz = (cz * chunkSize) + Mathf.Min(sz * (chunkSize / resolution), chunkSize);
                            float h = heights.SampleWorld(new Vector2(wx, wz));
                            if (h < minHeight) minHeight = h;
                            if (h > maxHeight) maxHeight = h;
                        }
                    }

                    if (maxHeight <= p.bottomClampY) continue;

                    int minYLayer = FloorDiv(Mathf.FloorToInt(Mathf.Min(minHeight, p.bottomClampY)), Mathf.RoundToInt(chunkSize));
                    int maxYLayer = FloorDiv(Mathf.CeilToInt(maxHeight), Mathf.RoundToInt(chunkSize));

                    // Precompute the surface height per voxel COLUMN once — identical
                    // results to sampling per voxel, but resolution² lookups instead of
                    // resolution³ (16× fewer at resolution 16).
                    var columnHeights = new float[resolution, resolution];
                    for (int sz = 0; sz < resolution; sz++)
                    {
                        for (int sx = 0; sx < resolution; sx++)
                        {
                            float wx = cx * chunkSize + sx * (chunkSize / resolution);
                            float wz = cz * chunkSize + sz * (chunkSize / resolution);
                            columnHeights[sx, sz] = heights.SampleWorld(new Vector2(wx, wz));
                        }
                    }

                    for (int cy = minYLayer; cy <= maxYLayer; cy++)
                    {
                        Vector3Int coord = new Vector3Int(cx, cy, cz);
                        var entry = store.GetOrCreate(coord);
                        Vector3 origin = new Vector3(coord.x * chunkSize, coord.y * chunkSize, coord.z * chunkSize);
                        float spacing = chunkSize / resolution;

                        bool changed = false;
                        for (int x = 0; x < resolution; x++)
                        {
                            for (int z = 0; z < resolution; z++)
                            {
                                float surface = columnHeights[x, z];

                                for (int y = 0; y < resolution; y++)
                                {
                                    float worldY = origin.y + y * spacing;
                                    float density = (surface - worldY) * sharpness + 0.5f;
                                    if (worldY < p.bottomClampY) density = 1f;
                                    density = Mathf.Clamp01(density);
                                    float previous = store.GetDensity(entry, x, y, z);
                                    if (!Mathf.Approximately(previous, density))
                                    {
                                        store.SetDensity(entry, x, y, z, density);
                                        changed = true;
                                    }
                                }
                            }
                        }
                        if (changed) written++;
                    }
                }
            }
            return written;
        }

        private static int FloorDiv(int value, int divisor) =>
            divisor <= 0 ? value : (value >= 0 ? value / divisor : (value - divisor + 1) / divisor);
    }
}
