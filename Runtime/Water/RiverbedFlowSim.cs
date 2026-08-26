using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Slow, budgeted river evolution at play time: fast water erodes its bed (tiny negative
    /// stamps along baked river segments), slack water deposits. Uses the effective flow
    /// speed so <see cref="WaterLevelDriver"/> weather directly changes erosion rates.
    /// Authoring-safe: stamps are tiny and interval-gated; wrap in undo at call sites that need it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RiverbedFlowSim : MonoBehaviour
    {
        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private float chunkSize = 128f;

        [Header("Budget")]
        [Min(1f)] [SerializeField] private float intervalSeconds = 20f;
        [Range(1, 256)] [SerializeField] private int stampsPerTick = 24;

        [Header("Rates")]
        [Tooltip("Density delta per stamp in fast water (negative = carve).")]
        [SerializeField] private float erosionDelta = -0.04f;
        [Tooltip("Density delta per stamp in slack water (positive = deposit).")]
        [SerializeField] private float depositionDelta = 0.025f;
        [Tooltip("Flow speeds below this count as slack water.")]
        [SerializeField] private float slackSpeed = 0.4f;
        [SerializeField] private uint seed = 20260826;

        private float timer;
        private Unity.Mathematics.Random rng;
        private bool rngInitialized;

        public WaterWorldRuntimeData Target
        {
            get => waterData;
            set => waterData = value;
        }

        public VoxelStoreAsset Store
        {
            get => store;
            set => store = value;
        }

        public event Action<int> TickCompleted;

        private static uint FallbackSeed() =>
            (uint)(System.Environment.TickCount & 0x7FFFFFFF) | 1u;

        private void Update()
        {
            if (waterData == null || store == null) return;
            timer += Time.deltaTime;
            if (timer < intervalSeconds) return;
            timer = 0f;
            TickCompleted?.Invoke(Tick());
        }

        /// <summary>Runs one simulation tick. Returns voxels changed.</summary>
        public int Tick()
        {
            // Lazy init (edit-mode tests never run OnEnable): prefer the serialized seed
            // so a given seed always erodes identically.
            if (!rngInitialized)
            {
                uint state = seed != 0u ? seed : FallbackSeed();
                rng = new Unity.Mathematics.Random(state);
                rngInitialized = true;
            }

            RiverSegmentData[] segments = waterData.RiverSegments;
            if (segments == null || segments.Length == 0) return 0;

            float speedScale = waterData.FlowSpeedMultiplier;
            int changed = 0;

            for (int i = 0; i < stampsPerTick; i++)
            {
                RiverSegmentData segment = segments[(int)(rng.NextUInt() % (uint)segments.Length)];
                float t = rng.NextFloat();
                Vector3 center = Vector3.Lerp(segment.start, segment.end, t);
                float halfSpan = Mathf.Lerp(segment.startWidth, segment.endWidth, t) * 0.5f;
                float radius = Mathf.Max(0.75f, halfSpan * 0.9f);

                float speed = segment.flowSpeed * speedScale;
                float delta = speed >= slackSpeed
                    ? erosionDelta * Mathf.Clamp01(speed / 3f)
                    : depositionDelta;

                changed += TerrainDeformer.StampSphere(store, chunkSize, center, radius, delta);
            }
            return changed;
        }
    }
}
