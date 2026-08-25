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

            [Header("Underwater Gate (needs an IWaterAwareTerrainQuery)")]
            [Tooltip("Enable to restrict placement by water depth instead of elevation only.")]
            public bool useDepthGate;
            [Tooltip("Minimum water depth in meters at the placement point.")]
            public float minDepth;
            [Tooltip("Maximum water depth in meters; 999 disables the upper bound.")]
            public float maxDepth = 999f;
            [Tooltip("Maximum sampled flow speed (m/s); protects corals from torrent zones.")]
            public float maxFlowSpeed = 999f;

            [Header("Density & Placement")]
            [Tooltip("Average instances per square meter.")]
            public float densityPerSquareMeter = 0.01f;
            public float noiseMaskScale = 0.008f;
            [Range(0f, 1f)] public float noiseThreshold = 0.45f;
            public bool alignToNormal = true;
            public Vector2 scaleRange = new Vector2(1f, 1f);

            [Header("Growth Stages")]
            [Tooltip("Optional respawn stages (sprout → mature). When set, one entry replaces prefabs per placement.")]
            public List<GameObject> growthStages = new List<GameObject>();
        }

        public List<Rule> rules = new List<Rule>();
    }

    /// <summary>
    /// Optional extension for terrain queries that know about water — enables the
    /// underwater depth/flow gates on scatter rules.
    /// </summary>
    public interface IWaterAwareTerrainQuery
    {
        bool TrySampleWater(Vector3 worldXzAtTerrainHeight, out WorldBuilder.Runtime.Water.WaterSample sample);
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

                        if (!PassesWaterGates(query, rule, px, pz, height)) continue;

                        GameObject prefab = rule.prefabs[random.NextInt(0, rule.prefabs.Count)];
                        if (prefab == null) continue;
                        if (rule.growthStages != null && rule.growthStages.Count > 0)
                        {
                            GameObject staged = rule.growthStages[random.NextInt(0, rule.growthStages.Count)];
                            if (staged != null) prefab = staged;
                        }

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

        private static bool PassesWaterGates(ITerrainQuery query, ScatterRuleSet.Rule rule,
            float px, float pz, float terrainHeight)
        {
            bool needsDepth = rule.useDepthGate;
            bool needsFlow = rule.maxFlowSpeed < 999f;
            if (!needsDepth && !needsFlow) return true;
            if (query is not IWaterAwareTerrainQuery waterQuery) return false;

            if (!waterQuery.TrySampleWater(new Vector3(px, terrainHeight, pz),
                    out WorldBuilder.Runtime.Water.WaterSample sample))
                return false; // gate requested but point is dry

            if (needsDepth && (sample.Depth < rule.minDepth || sample.Depth > rule.maxDepth)) return false;
            if (needsFlow && sample.FlowSpeed > rule.maxFlowSpeed) return false;
            return true;
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

    /// <summary>
    /// Interior-capable terrain probe: finds walkable floors INSIDE a volume (cave
    /// caverns, grottos), unlike the top-down <see cref="ITerrainQuery"/>.
    /// </summary>
    public interface IVolumeQuery
    {
        /// <summary>
        /// Marches down from the candidate until it enters open air and then lands on
        /// solid ground. Returns false when the column never reaches a cavity floor.
        /// </summary>
        bool TryFloor(Vector3 candidateTop, float maxDepth, out Vector3 floorPoint, out Vector3 normal);

        BiomeType BiomeAt(Vector3 position);
    }

    public static class VoxelVolumeScatter
    {
        /// <summary>
        /// Deterministic placements on cavity floors inside the volume — ore veins, glow
        /// moss and bat roosts inside carved caves. Same rule semantics as the surface
        /// engine (biome gate, slope via floor normal, density cells, growth stages).
        /// </summary>
        public static List<PcgPlacement> Generate(ScatterRuleSet ruleSet, IVolumeQuery query,
            Bounds volume, int seed)
        {
            if (ruleSet == null) throw new ArgumentNullException(nameof(ruleSet));
            if (query == null) throw new ArgumentNullException(nameof(query));

            var results = new List<PcgPlacement>();

            for (int r = 0; r < ruleSet.rules.Count; r++)
            {
                ScatterRuleSet.Rule rule = ruleSet.rules[r];
                if (rule == null || (rule.prefabs == null || rule.prefabs.Count == 0) &&
                    (rule.growthStages == null || rule.growthStages.Count == 0)) continue;
                if (rule.densityPerSquareMeter <= 0f) continue;

                float cellSize = Mathf.Sqrt(1f / rule.densityPerSquareMeter);
                int cellsX = Mathf.Max(1, Mathf.CeilToInt(volume.size.x / cellSize));
                int cellsZ = Mathf.Max(1, Mathf.CeilToInt(volume.size.z / cellSize));

                for (int cz = 0; cz < cellsZ; cz++)
                {
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        var random = new Unity.Mathematics.Random(
                            (uint)(seed ^ (r * 73856093) ^ (cx * 19349663) ^ (cz * 83492791)));
                        float acceptProbability =
                            Mathf.Clamp01(rule.densityPerSquareMeter * cellSize * cellSize);
                        if (random.NextFloat() >= acceptProbability) continue;

                        float px = volume.min.x + (cx + random.NextFloat()) * cellSize;
                        float pz = volume.min.z + (cz + random.NextFloat()) * cellSize;
                        float topY = volume.max.y - random.NextFloat() * volume.size.y * 0.25f;

                        const float maxFloorDepth = 512f;
                        if (!query.TryFloor(new Vector3(px, topY, pz), maxFloorDepth,
                                out Vector3 floor, out Vector3 normal)) continue;
                        if (floor.y < volume.min.y) continue;

                        BiomeType biome = query.BiomeAt(floor);
                        if (!rule.anyBiome && biome != rule.biome) continue;
                        if (Vector3.Dot(normal, Vector3.up) <
                            Mathf.Cos(rule.maxSlopeDegrees * Mathf.Deg2Rad)) continue;

                        GameObject prefab = random.NextInt(0, 2) == 0 &&
                                            rule.prefabs != null && rule.prefabs.Count > 0
                            ? rule.prefabs[random.NextInt(0, rule.prefabs.Count)]
                            : null;
                        if (rule.growthStages != null && rule.growthStages.Count > 0)
                            prefab = rule.growthStages[random.NextInt(0, rule.growthStages.Count)] ?? prefab;
                        if (prefab == null) continue;

                        Quaternion rotation = Quaternion.Euler(0f, random.NextFloat() * 360f, 0f);
                        if (rule.alignToNormal && normal.sqrMagnitude > 1e-5f)
                            rotation = Quaternion.FromToRotation(Vector3.up, normal) * rotation;

                        results.Add(new PcgPlacement
                        {
                            Prefab = prefab,
                            Position = floor,
                            Rotation = rotation,
                            Scale = Vector3.one *
                                    Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, random.NextFloat()),
                            Biome = biome
                        });
                    }
                }
            }
            return results;
        }
    }

    /// <summary>Voxel-density implementation of <see cref="IVolumeQuery"/>.</summary>
    public sealed class VoxelVolumeQuery : IVolumeQuery
    {
        private readonly VoxelWorldSampler sampler;
        private readonly HighResBiomeMap biomes;
        private readonly float chunkSize;

        public VoxelVolumeQuery(VoxelWorldSampler sampler, float chunkSize, HighResBiomeMap biomes = null)
        {
            this.sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            this.chunkSize = chunkSize;
            this.biomes = biomes;
        }

        public bool TryFloor(Vector3 candidateTop, float maxDepth, out Vector3 floorPoint,
            out Vector3 normal)
        {
            normal = Vector3.up;
            floorPoint = default;
            float spacing = chunkSize / sampler.SamplePointResolution;

            bool inAir = false;
            float previousY = candidateTop.y;
            for (float y = candidateTop.y - spacing; y >= candidateTop.y - maxDepth; y -= spacing)
            {
                float density = sampler.Sample(candidateTop.x, y, candidateTop.z);
                if (!inAir)
                {
                    if (density < SurfaceNetsMesher.IsoLevel) inAir = true; // entered a cavity
                }
                else if (density >= SurfaceNetsMesher.IsoLevel)
                {
                    // Land on the last AIR sample so placed objects sit on the floor,
                    // not half-buried in the transition band.
                    floorPoint = new Vector3(candidateTop.x, previousY, candidateTop.z);
                    normal = DensityGradient(new Vector3(candidateTop.x, y, candidateTop.z));
                    return true;
                }
                previousY = y;
            }
            return false;
        }

        public BiomeType BiomeAt(Vector3 position) =>
            biomes != null
                ? biomes.SampleBiome(position.x, position.z, chunkSize)
                : BiomeType.Forest;

        private Vector3 DensityGradient(Vector3 position)
        {
            const float epsilon = 0.35f;
            float dx = sampler.Sample(position.x + epsilon, position.y, position.z) -
                       sampler.Sample(position.x - epsilon, position.y, position.z);
            float dy = sampler.Sample(position.x, position.y + epsilon, position.z) -
                       sampler.Sample(position.x, position.y - epsilon, position.z);
            float dz = sampler.Sample(position.x, position.y, position.z + epsilon) -
                       sampler.Sample(position.x, position.y, position.z - epsilon);
            Vector3 gradient = new Vector3(dx, dy, dz);
            return gradient.sqrMagnitude > 1e-10f ? (-gradient).normalized : Vector3.up;
        }
    }
}
