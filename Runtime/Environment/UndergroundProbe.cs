using System;
using UnityEngine;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Environment
{
    /// <summary>Result of an upward enclosure ray through the voxel density field.</summary>
    public readonly struct EnclosureSample
    {
        /// <summary>True when solid cover blocks the path to open sky.</summary>
        public bool IsEnclosed { get; }
        /// <summary>Meters of open air between the point and the ceiling (0 when buried).</summary>
        public float CoverThickness { get; }
        /// <summary>Meters up to open sky through solids; <see cref="RayUnresolved"/> when the ray ran out.</summary>
        public float DepthBelowSurface { get; }

        public static float RayUnresolved => float.MaxValue;

        public EnclosureSample(bool isEnclosed, float coverThickness, float depthBelowSurface)
        {
            IsEnclosed = isEnclosed;
            CoverThickness = coverThickness;
            DepthBelowSurface = depthBelowSurface;
        }

        public static EnclosureSample OpenAir(float maxRay) =>
            new EnclosureSample(false, maxRay, 0f);
    }

    /// <summary>
    /// Vertical raycast through the voxel density field (no physics): determines whether a
    /// world point sits under a cave ceiling, inside a flooded grotto, or in open air.
    /// </summary>
    public static class UndergroundProbe
    {
        /// <summary>
        /// Marches upward from the position until a solid cover or open sky is found.
        /// </summary>
        /// <param name="maxRay">Maximum upward search distance in meters.</param>
        /// <param name="step">March resolution in meters.</param>
        public static EnclosureSample Probe(VoxelWorldSampler sampler, Vector3 position,
            float maxRay = 160f, float step = 0.5f)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step));
            maxRay = Mathf.Max(step, maxRay);

            const float iso = SurfaceNetsMesher.IsoLevel;
            bool startedSolid = sampler.Sample(position.x, position.y, position.z) >= iso;
            float airGap = 0f;

            for (float y = position.y + step; y <= position.y + maxRay; y += step)
            {
                bool solid = sampler.Sample(position.x, y, position.z) >= iso;

                if (solid)
                {
                    if (!startedSolid && airGap <= 0f) airGap = Mathf.Max(0f, y - position.y);
                    continue;
                }

                // Air reached after being covered (or buried) — open sky lies above.
                if (startedSolid || airGap > 0f)
                    return new EnclosureSample(true, airGap, y - position.y);
            }

            if (startedSolid)
                return new EnclosureSample(true, 0f, EnclosureSample.RayUnresolved);

            return airGap > 0f
                ? new EnclosureSample(true, airGap, maxRay)
                : EnclosureSample.OpenAir(maxRay);
        }
    }

    /// <summary>High-level gameplay environment bucket.</summary>
    public enum EnvironmentDomain
    {
        OpenAir,
        Underwater,
        Underground,
        FloodedCave
    }

    /// <summary>
    /// One-call classification combining water queries with voxel enclosure probing —
    /// lets gameplay distinguish "underwater", "in a cave" and "in a flooded cave".
    /// </summary>
    public static class EnvironmentClassifier
    {
        public static EnvironmentDomain Classify(IWaterQueryService water, VoxelWorldSampler sampler,
            Vector3 position, float maxCoverRay = 160f, float submergedEpsilon = 0.05f)
        {
            WaterSample waterSample = water != null ? water.Sample(position) : WaterSample.Air;
            bool submerged = waterSample.IsInWater && waterSample.Depth > submergedEpsilon;

            if (submerged)
            {
                bool enclosed = sampler != null &&
                                UndergroundProbe.Probe(sampler, position, maxCoverRay).IsEnclosed;
                return enclosed ? EnvironmentDomain.FloodedCave : EnvironmentDomain.Underwater;
            }

            if (sampler != null && UndergroundProbe.Probe(sampler, position, maxCoverRay).IsEnclosed)
                return EnvironmentDomain.Underground;

            return EnvironmentDomain.OpenAir;
        }

        /// <summary>
        /// Classifies many positions in one call; <paramref name="results"/> must be at
        /// least as long as <paramref name="positions"/>. Returns the count written.
        /// </summary>
        public static int ClassifyBatch(IWaterQueryService water, VoxelWorldSampler sampler,
            Vector3[] positions, EnvironmentDomain[] results, float maxCoverRay = 160f,
            float submergedEpsilon = 0.05f)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length < positions.Length)
                throw new ArgumentException("Results must fit every position.", nameof(results));

            for (int i = 0; i < positions.Length; i++)
                results[i] = Classify(water, sampler, positions[i], maxCoverRay, submergedEpsilon);
            return positions.Length;
        }
    }
}
