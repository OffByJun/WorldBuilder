using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Wraps any water service with a global underground water table: points below
    /// <c>waterTableY</c> sample as still water unless a real body already wins.
    /// Feed this into <see cref="Environment.EnvironmentClassifier"/> and carved caves
    /// below the table automatically classify as FloodedCave.
    /// </summary>
    public sealed class GroundwaterService : IWaterQueryService
    {
        private const int TableBodyId = -777;
        private const int TablePriority = -100; // lose against every authored body

        private readonly IWaterQueryService inner;
        private readonly Func<float> waterTableProvider;

        public GroundwaterService(IWaterQueryService inner, float waterTableY)
            : this(inner, () => waterTableY)
        {
        }

        public GroundwaterService(IWaterQueryService inner, Func<float> waterTableProvider)
        {
            this.inner = inner;
            this.waterTableProvider = waterTableProvider ?? throw new ArgumentNullException(nameof(waterTableProvider));
        }

        public float WaterTableY => waterTableProvider();

        public WaterSample Sample(Vector3 position)
        {
            if (inner != null)
            {
                WaterSample authored = inner.Sample(position);
                if (authored.IsInWater && position.y <= authored.SurfaceHeight) return authored;
            }

            float table = waterTableProvider();
            if (position.y >= table) return inner != null ? inner.Sample(position) : WaterSample.Air;

            // Below the table: still groundwater with no flow.
            return new WaterSample(FluidType.Water, table, table - position.y,
                Vector3.zero, 0f, TableBodyId, TablePriority);
        }

        public int SampleBatch(Vector3[] positions, WaterSample[] results)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length < positions.Length)
                throw new ArgumentException("Results must fit every position.", nameof(results));
            for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
            return positions.Length;
        }
    }
}
