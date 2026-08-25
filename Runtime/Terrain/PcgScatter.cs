using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Data-driven ecology rules: which prefabs appear where, gated by elevation, slope,
    /// biome and a deterministic noise mask. The engine turns rules into placements.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Terrain/Scatter Rule Set", fileName = "ScatterRuleSet")]
    public sealed class ScatterRuleSet : ScriptableObject
    {
        [Serializable]
        public sealed class Rule
        {
            public string name = "Rule";
            public List<GameObject> prefabs = new List<GameObject>();

            [Header("Conditions")]
            public float minElevation = -999f;
            public float maxElevation = 999f;
            [Range(0f, 90f)] public float maxSlopeDegrees = 35f;
            public bool anyBiome = true;
            public BiomeType biome;

            [Header("Density & Placement")]
            [Tooltip("Average instances per square meter.")]
            public float densityPerSquareMeter = 0.01f;
            public float noiseMaskScale = 0.008f;
            [Range(0f, 1f)] public float noiseThreshold = 0.45f;
            public bool alignToNormal = true;
            public Vector2 scaleRange = new Vector2(1f, 1f);
        }

        public List<Rule> rules = new List<Rule>();
    }

    public struct PcgPlacement
    {
        public GameObject Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public BiomeType Biome;
    }

    public interface ITerrainQuery
    {
        /// <summary>Surface height at the XZ point; false when there is no terrain.</summary>
        bool TryHeight(Vector2 worldXz, out float height);
        /// <summary>Surface slope in degrees at the XZ point.</summary>
        float Slope(Vector2 worldXz);
        BiomeType BiomeAt(Vector2 worldXz);
    }

    public static class PcgScatterEngine
    {
        /// <summary>
        /// Produces deterministic placements for the rectangular world region.
        /// Same seed + same rule set + same samplers → identical output.
        /// </summary>
        public static List<PcgPlacement> Generate(ScatterRuleSet ruleSet, ITerrainQuery query,
            Rect worldBoundsXz, int seed)
        {
            if (ruleSet == null) throw new ArgumentNullException(nameof(ruleSet));
            if (query == null) throw new ArgumentNullException(nameof(query));

            var results = new List<PcgPlacement>();
            var noise = new FbmNoise(seed);

            for (int r = 0; r < ruleSet.rules.Count; r++)
            {
                ScatterRuleSet.Rule rule = ruleSet.rules[r];
                if (rule == null || rule.prefabs == null || rule.prefabs.Count == 0) continue;
                if (rule.densityPerSquareMeter <= 0f) continue;

                // Poisson-ish: one jittered candidate per cell of size sqrt(1/density).
                float cellSize = Mathf.Sqrt(1f / rule.densityPerSquareMeter);
                int cellsX = Mathf.Max(1, Mathf.CeilToInt(worldBoundsXz.width / cellSize));
                int cellsZ = Mathf.Max(1, Mathf.CeilToInt(worldBoundsXz.height / cellSize));

                for (int cz = 0; cz < cellsZ; cz++)
                {
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        var random = new Unity.Mathematics.Random(
                            (uint)(seed ^ (r * 73856093) ^ (cx * 19349663) ^ (cz * 83492791)));

                        // One jittered candidate per cell; keep it with probability density*cell².
                        float acceptProbability = Mathf.Clamp01(rule.densityPerSquareMeter * cellSize * cellSize);
                        if (random.NextFloat() >= acceptProbability) continue;

                        float px = worldBoundsXz.xMin + (cx + random.NextFloat()) * cellSize;
                        float pz = worldBoundsXz.yMin + (cz + random.NextFloat()) * cellSize;
                        var xz = new float2(px, pz);

                        float mask = noise.Value2D(xz, rule.noiseMaskScale, 3, 0.5f, 2f) * 0.5f + 0.5f;
                        if (mask < rule.noiseThreshold) continue;

                        if (!query.TryHeight(new Vector2(px, pz), out float height)) continue;
                        float slope = query.Slope(new Vector2(px, pz));
                        if (slope > rule.maxSlopeDegrees) continue;

                        BiomeType biome = query.BiomeAt(new Vector2(px, pz));
                        if (!rule.anyBiome && biome != rule.biome) continue;
                        if (height < rule.minElevation || height > rule.maxElevation) continue;

                        GameObject prefab = rule.prefabs[random.NextInt(0, rule.prefabs.Count)];
                        if (prefab == null) continue;

                        float scale = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, random.NextFloat());
                        Quaternion rotation = Quaternion.Euler(0f, random.NextFloat() * 360f, 0f);
                        if (rule.alignToNormal)
                        {
                            Vector3 normal = SurfaceNormal(query, new Vector2(px, pz));
                            rotation = Quaternion.FromToRotation(Vector3.up, normal) * rotation;
                        }

                        results.Add(new PcgPlacement
                        {
                            Prefab = prefab,
                            Position = new Vector3(px, height, pz),
                            Rotation = rotation,
                            Scale = Vector3.one * scale,
                            Biome = biome
                        });
                    }
                }
            }
            return results;
        }

        private static Vector3 SurfaceNormal(ITerrainQuery query, Vector2 xz)
        {
            const float delta = 0.5f;
            float hl = query.TryHeight(xz - new Vector2(delta, 0f), out float l) ? l : query.TryHeight(xz, out float c0) ? c0 : 0f;
            float hr = query.TryHeight(xz + new Vector2(delta, 0f), out float r) ? r : hl;
            float hd = query.TryHeight(xz - new Vector2(0f, delta), out float d) ? d : query.TryHeight(xz, out float c1) ? c1 : 0f;
            float hu = query.TryHeight(xz + new Vector2(0f, delta), out float u) ? u : hd;
            Vector3 normal = new Vector3(hl - hr, 2f * delta, hd - hu);
            return normal.normalized;
        }
    }
}
