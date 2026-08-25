using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Bakes per-chunk splatmaps from a <see cref="HighResBiomeMap"/>: each biome maps to
    /// one of four splat layers (RGBA channels), with bilinear blending at borders.
    /// </summary>
    public static class SplatBaker
    {
        [Serializable]
        public sealed class LayerMapping
        {
            public BiomeType biome = BiomeType.Forest;
            [Range(0, 3)] public int layer;
        }

        public static Color32[] Bake(HighResBiomeMap biomeMap, Vector3Int chunkCoord,
            int textureSize, float chunkSize, LayerMapping[] mapping)
        {
            if (biomeMap == null) throw new ArgumentNullException(nameof(biomeMap));
            if (textureSize <= 0) throw new ArgumentOutOfRangeException(nameof(textureSize));
            if (mapping == null || mapping.Length == 0)
                mapping = DefaultMapping();

            var pixels = new Color32[textureSize * textureSize];

            for (int ty = 0; ty < textureSize; ty++)
            {
                for (int tx = 0; tx < textureSize; tx++)
                {
                    // Splatmap V runs bottom-up like the mesh UV (world XZ).
                    float u = (tx + 0.5f) / textureSize;
                    float v = (ty + 0.5f) / textureSize;

                    // Sample a 2×2 neighbourhood of biome colors for smoother borders.
                    Color c00 = SampleWeight(biomeMap, chunkCoord, textureSize, chunkSize, mapping, tx - 1, ty - 1);
                    Color c10 = SampleWeight(biomeMap, chunkCoord, textureSize, chunkSize, mapping, tx + 1, ty - 1);
                    Color c01 = SampleWeight(biomeMap, chunkCoord, textureSize, chunkSize, mapping, tx - 1, ty + 1);
                    Color c11 = SampleWeight(biomeMap, chunkCoord, textureSize, chunkSize, mapping, tx + 1, ty + 1);
                    Color cc = SampleWeight(biomeMap, chunkCoord, textureSize, chunkSize, mapping, tx, ty);

                    Vector4 weights = (ToVector(cc) * 4f + ToVector(c00) + ToVector(c10) +
                                       ToVector(c01) + ToVector(c11)) / 8f;

                    pixels[ty * textureSize + tx] = Normalize(weights);
                }
            }
            return pixels;
        }

        public static LayerMapping[] DefaultMapping() =>
            new[]
            {
                new LayerMapping { biome = BiomeType.Beach, layer = 0 },          // sand
                new LayerMapping { biome = BiomeType.Forest, layer = 1 },         // grass
                new LayerMapping { biome = BiomeType.Rocky, layer = 2 },          // rock
                new LayerMapping { biome = BiomeType.Ocean, layer = 3 },          // seabed
                new LayerMapping { biome = BiomeType.CoralReef, layer = 3 },      // seabed
                new LayerMapping { biome = BiomeType.KelpForest, layer = 3 },     // seabed
                new LayerMapping { biome = BiomeType.AbyssalTrench, layer = 3 },  // seabed
                new LayerMapping { biome = BiomeType.Cave, layer = 2 }            // rock
            };

        public static void ApplyLayer(BiomeType biome, int layer, LayerMapping[] mapping)
        {
            for (int i = 0; i < mapping.Length; i++)
            {
                if (mapping[i].biome == biome)
                {
                    mapping[i].layer = Mathf.Clamp(layer, 0, 3);
                    return;
                }
            }
        }

        private static Color SampleWeight(HighResBiomeMap map, Vector3Int chunkCoord, int textureSize,
            float chunkSize, LayerMapping[] mapping, int tx, int tz)
        {
            // Convert texel position to world XZ.
            float worldX = chunkCoord.x * chunkSize + (tx + 0.5f) / textureSize * chunkSize;
            float worldZ = chunkCoord.z * chunkSize + (tz + 0.5f) / textureSize * chunkSize;
            BiomeType biome = map.SampleBiome(worldX, worldZ, chunkSize);

            int layer = 1; // unmapped biomes fall back to grass
            for (int i = 0; i < mapping.Length; i++)
            {
                if (mapping[i] != null && mapping[i].biome == biome)
                {
                    layer = mapping[i].layer;
                    break;
                }
            }

            return ChannelColor(layer);
        }

        private static Color ChannelColor(int layer)
        {
            return layer switch
            {
                0 => new Color(1f, 0f, 0f, 0f),
                1 => new Color(0f, 1f, 0f, 0f),
                2 => new Color(0f, 0f, 1f, 0f),
                _ => new Color(0f, 0f, 0f, 1f)
            };
        }

        private static Vector4 ToVector(Color c) => new Vector4(c.r, c.g, c.b, c.a);

        private static Color32 Normalize(Vector4 w)
        {
            w = Vector4.Max(w, Vector4.zero);
            float total = w.x + w.y + w.z + w.w;
            if (total <= 0.0001f) return new Color32(0, 255, 0, 0); // default grass

            w /= total;
            byte ToByte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
            return new Color32(ToByte(w.x), ToByte(w.y), ToByte(w.z), ToByte(w.w));
        }
    }
}
