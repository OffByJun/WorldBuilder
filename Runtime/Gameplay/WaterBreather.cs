using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// Air meter for non-gilled GameObject creatures: drains while submerged
    /// (Underwater / FloodedCave), refills at the surface, and raises drowning events.
    /// HP handling stays in game code — bind to <see cref="DrowningTick"/>.
    /// Optional swim assist temporarily enables float-on-surface on a linked drifter when
    /// air runs low, so panicking animals bob up instead of dying silently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterBreather : MonoBehaviour
    {
        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private VoxelStoreAsset voxelStore;
        [SerializeField] private float chunkSize = 128f;
        [SerializeField] private bool gilled;                 // fish never drown
        [Min(1f)] [SerializeField] private float airCapacitySeconds = 30f;
        [Min(1f)] [SerializeField] private float rechargeMultiplier = 3f;
        [Range(0f, 1f)] [SerializeField] private float panicThreshold = 0.25f;
        [SerializeField] private bool swimAssistOnPanic = true;

        private WaterQueryService waterService;
        private VoxelWorldSampler sampler;
        private float air = 1f; // normalized
        private bool drowning;
        private float tickAccumulator;

        public IWaterQueryService QueryServiceOverride { get; set; }
        public VoxelWorldSampler SamplerOverride { get; set; }

        public float AirRatio => air;
        public bool IsSubmerged { get; private set; }
        public bool IsDrowning { get; private set; }

        public event Action<float> AirChanged;
        public event Action StartedDrowning;
        public event Action StoppedDrowning;
        /// <summary>Raised every second while actively drowning — hook damage here.</summary>
        public event Action DrowningTick;

        private void OnEnable()
        {
            if (waterData != null) waterService = new WaterQueryService(waterData);
            if (voxelStore != null) sampler = new VoxelWorldSampler(voxelStore, Mathf.Max(1f, chunkSize));
            air = 1f;
        }

        private void Update() => Simulate(Time.deltaTime);

        /// <summary>Testable core — callers may drive fixed steps directly.</summary>
        public void Simulate(float deltaTime)
        {
            Vector3 position = transform.position;
            IWaterQueryService water = QueryServiceOverride ?? waterService;
            VoxelWorldSampler voxels = SamplerOverride ?? sampler;

            EnvironmentDomain domain = EnvironmentClassifier.Classify(water, voxels, position);
            IsSubmerged = domain == EnvironmentDomain.Underwater ||
                          domain == EnvironmentDomain.FloodedCave;

            float previousAir = air;
            if (IsSubmerged && !gilled)
                air = Mathf.Max(0f, air - deltaTime / Mathf.Max(1f, airCapacitySeconds));
            else
                air = Mathf.Min(1f, air + deltaTime * rechargeMultiplier /
                                           Mathf.Max(1f, airCapacitySeconds));

            if (!Mathf.Approximately(previousAir, air)) AirChanged?.Invoke(air);

            HandleSwimAssist();

            bool drowningNow = IsSubmerged && !gilled && air <= 0f;
            if (drowningNow && !drowning)
            {
                drowning = true;
                StartedDrowning?.Invoke();
            }
            else if (!drowningNow && drowning)
            {
                drowning = false;
                StoppedDrowning?.Invoke();
            }

            if (drowning)
            {
                tickAccumulator += deltaTime;
                if (tickAccumulator >= 1f)
                {
                    tickAccumulator -= 1f;
                    DrowningTick?.Invoke();
                }
            }
            else tickAccumulator = 0f;
        }

        private void HandleSwimAssist()
        {
            if (!swimAssistOnPanic || !(air < panicThreshold) || !IsSubmerged) return;
            var drifter = GetComponent<Water.WaterDrifter>();
            if (drifter == null) return;
            drifter.Parameters.FloatOnSurface = true;
        }
    }
}
