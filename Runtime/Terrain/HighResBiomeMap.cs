using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Whittaker-style biome classification from elevation, temperature and humidity.
    /// Deterministic and allocation-free.
    /// </summary>
    public static class BiomeClassifier
    {
        /// <summary>Highest valid BiomeType id — storage clamps against this.</summary>
        public const int LastBiomeId = (int)BiomeType.AbyssalTrench;

        public sealed class ClimateInputs
        {
            public float TemperatureNoiseScale = 0.0016f;
            public float HumidityNoiseScale = 0.0023f;
            [Range(0f, 1f)] public float ElevationTemperatureWeight = 0.6f;
            public float SeaLevel = 0f;
        }

        /// <summary>Classify a world column. Returns one of the nine BiomeType values.</summary>
        public static BiomeType Classify(FbmNoise noise, ClimateInputs inputs, float2 worldXz,
            float elevation)
        {
            float temperature = noise.Value2D(worldXz + new float2(913.7f, 211.3f),
                inputs.TemperatureNoiseScale, 3, 0.5f, 2f);
            float humidity = noise.Value2D(worldXz + new float2(-407.9f, 664.1f),
                inputs.HumidityNoiseScale, 3, 0.5f, 2f);

            // Higher is colder.
            float altitudeCooling = Mathf.InverseLerp(60f, -20f, elevation) * inputs.ElevationTemperatureWeight;
            temperature = Mathf.Clamp01((temperature + 1f) * 0.5f) * (1f - inputs.ElevationTemperatureWeight)
                          + altitudeCooling * inputs.ElevationTemperatureWeight;
            humidity = Mathf.Clamp01((humidity + 1f) * 0.5f);

            return FromClimate(elevation, temperature, humidity, inputs.SeaLevel);
        }

        public static BiomeType FromClimate(float elevation, float temperature01, float humidity01,
            float seaLevel)
        {
            // Seafloor bands: trench → deep → reef → kelp → ocean floor.
            if (elevation < seaLevel - 60f) return BiomeType.AbyssalTrench;
            if (elevation < seaLevel - 18f) return BiomeType.DeepSea;
            if (elevation < seaLevel - 8f) return BiomeType.CoralReef;
            if (elevation < seaLevel - 2f) return BiomeType.KelpForest;
            if (elevation < seaLevel) return BiomeType.Ocean;
            if (elevation < seaLevel + 3.5f) return BiomeType.Beach;
            if (elevation > 55f && temperature01 < 0.45f) return BiomeType.Rocky;

            // Whittaker-ish split of the land band.
            bool cold = temperature01 < 0.42f;
            bool wet = humidity01 >= 0.5f;

            if (cold) return wet ? BiomeType.Forest : BiomeType.Rocky;
            return wet ? BiomeType.Forest : BiomeType.Beach;
        }

        /// <summary>
        /// Classify a point that may sit underground: enclosed spaces are always
        /// <see cref="BiomeType.Cave"/>; open points fall back to climate classification.
        /// </summary>
        public static BiomeType FromEnvironment(float elevation, float temperature01,
            float humidity01, float seaLevel, bool isEnclosed)
        {
            if (isEnclosed) return BiomeType.Cave;
            return FromClimate(elevation, temperature01, humidity01, seaLevel);
        }

        public static Color DebugColor(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Ocean: return new Color(0.05f, 0.25f, 0.55f);
                case BiomeType.DeepSea: return new Color(0.02f, 0.10f, 0.30f);
                case BiomeType.Beach: return new Color(0.90f, 0.82f, 0.50f);
                case BiomeType.Rocky: return new Color(0.55f, 0.52f, 0.48f);
                case BiomeType.Cave: return new Color(0.23f, 0.18f, 0.28f);
                case BiomeType.CoralReef: return new Color(0.95f, 0.45f, 0.55f);
                case BiomeType.KelpForest: return new Color(0.10f, 0.42f, 0.35f);
                case BiomeType.AbyssalTrench: return new Color(0.01f, 0.04f, 0.14f);
                default: return new Color(0.20f, 0.55f, 0.22f);   // Forest
            }
        }
    }

    /// <summary>
    /// High-resolution biome storage: N×N classification samples per chunk, queryable at
    /// runtime and baked into vertex colors during meshing.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Terrain/High-Res Biome Map", fileName = "HighResBiomeMap")]
    public sealed class HighResBiomeMap : ScriptableObject
    {
        [SerializeField] private int cellsPerChunk = 8;
        [SerializeField] private List<Vector3Int> chunkKeys = new List<Vector3Int>();
        [SerializeField] private List<byte[]> biomeIds = new List<byte[]>();

        [NonSerialized] private Dictionary<Vector3Int, byte[]> lookup;

        private void OnEnable() => RebuildLookup();

        private Dictionary<Vector3Int, byte[]> Lookup
        {
            get { if (lookup == null) RebuildLookup(); return lookup; }
        }

        public int CellsPerChunk => cellsPerChunk;
        public IReadOnlyList<Vector3Int> Keys => chunkKeys;

        public void Configure(int perChunk, List<Vector3Int> keys, List<byte[]> ids)
        {
            cellsPerChunk = Mathf.Max(1, perChunk);
            chunkKeys = keys ?? new List<Vector3Int>();
            biomeIds = ids ?? new List<byte[]>();
            lookup = null;
        }

        public bool TryGetChunk(Vector3Int coord, out byte[] cells) =>
            Lookup.TryGetValue(coord, out cells) && cells != null;

        public void SetChunk(Vector3Int coord, byte[] cells)
        {
            int index = chunkKeys.IndexOf(coord);
            if (index < 0)
            {
                chunkKeys.Add(coord);
                biomeIds.Add(cells);
            }
            else
            {
                biomeIds[index] = cells;
            }
            lookup = null;
        }

        /// <summary>Nearest-cell biome id for queries that need the enum, not a color.</summary>
        public BiomeType SampleBiome(float worldX, float worldZ, float chunkSize)
        {
            int ix = Mathf.FloorToInt(worldX / chunkSize * cellsPerChunk);
            int iz = Mathf.FloorToInt(worldZ / chunkSize * cellsPerChunk);
            Vector3Int chunk = new Vector3Int(FloorDiv(ix, cellsPerChunk), 0, FloorDiv(iz, cellsPerChunk));
            if (!TryGetChunk(chunk, out byte[] cells)) return BiomeType.Forest;
            byte id = cells[Mod(iz, cellsPerChunk) * cellsPerChunk + Mod(ix, cellsPerChunk)];
            return (BiomeType)Mathf.Clamp(id, 0, BiomeClassifier.LastBiomeId);
        }

        /// <summary>Bilinear-blended biome color for smooth borders.</summary>
        public Color SampleColor(float worldX, float worldZ, float chunkSize)
        {
            float fx = worldX / chunkSize * cellsPerChunk;
            float fz = worldZ / chunkSize * cellsPerChunk;
            int ix = Mathf.FloorToInt(fx);
            int iz = Mathf.FloorToInt(fz);
            float tx = fx - ix;
            float tz = fz - iz;

            Vector3Int chunk = new Vector3Int(FloorDiv(ix, cellsPerChunk), 0, FloorDiv(iz, cellsPerChunk));
            if (!TryGetChunk(chunk, out byte[] cells)) return Color.gray;

            Color SampleCell(int cx, int cz)
            {
                Vector3Int c = new Vector3Int(FloorDiv(cx, cellsPerChunk), chunk.y, FloorDiv(cz, cellsPerChunk));
                if (!TryGetChunk(c, out byte[] data)) return Color.clear;
                byte id = data[Mod(cz, cellsPerChunk) * cellsPerChunk + Mod(cx, cellsPerChunk)];
                return BiomeClassifier.DebugColor((BiomeType)Mathf.Clamp(id, 0, BiomeClassifier.LastBiomeId));
            }

            Color c00 = SampleCell(ix, iz);
            Color c10 = SampleCell(ix + 1, iz);
            Color c01 = SampleCell(ix, iz + 1);
            Color c11 = SampleCell(ix + 1, iz + 1);

            float w00 = c00.a > 0f ? (1 - tx) * (1 - tz) : 0f;
            float w10 = c10.a > 0f ? tx * (1 - tz) : 0f;
            float w01 = c01.a > 0f ? (1 - tx) * tz : 0f;
            float w11 = c11.a > 0f ? tx * tz : 0f;
            float total = w00 + w10 + w01 + w11;
            if (total <= 0f) return Color.gray;

            Color rgb = c00 * w00 + c10 * w10 + c01 * w01 + c11 * w11;
            rgb /= total;
            rgb.a = 1f;
            return rgb;
        }

        private static int FloorDiv(int value, int divisor) =>
            divisor <= 0 ? value : (value >= 0 ? value / divisor : (value - divisor + 1) / divisor);

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        private void RebuildLookup()
        {
            lookup = new Dictionary<Vector3Int, byte[]>(chunkKeys.Count);
            for (int i = 0; i < chunkKeys.Count && i < biomeIds.Count; i++)
                lookup[chunkKeys[i]] = biomeIds[i];
        }
    }
}
