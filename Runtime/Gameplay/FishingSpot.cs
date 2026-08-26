using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// A place where fishing works: validates water via the baked query, rolls bites from a
    /// weighted fish table gated by depth, and exposes a reel-timing window. The actual
    /// input/progress UI stays in game code — drive <see cref="FishingSession"/> from it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FishingSpot : MonoBehaviour
    {
        [Serializable]
        public struct FishEntry
        {
            public string itemId;
            [Min(1)] public int weight;
            [Min(0f)] public float minDepth;
            public float maxDepth;
        }

        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private List<FishEntry> table = new List<FishEntry>
        {
            new FishEntry { itemId = "fish_common", weight = 60, minDepth = 0.5f, maxDepth = 999f },
            new FishEntry { itemId = "fish_rare", weight = 8, minDepth = 6f, maxDepth = 999f }
        };
        [SerializeField] private Vector2 biteDelaySeconds = new Vector2(3f, 9f);
        [Min(0.2f)] [SerializeField] private float reelWindowSeconds = 1.1f;

        private WaterQueryService service;

        public IReadOnlyList<FishEntry> Table => table;
        public Vector2 BiteDelay => biteDelaySeconds;
        public float ReelWindow => reelWindowSeconds;

        private void OnEnable()
        {
            if (waterData != null) service = new WaterQueryService(waterData);
        }

        public void Configure(WaterWorldRuntimeData data) => Configure(data, biteDelaySeconds);

        public void Configure(WaterWorldRuntimeData data, Vector2 biteDelay)
        {
            waterData = data;
            biteDelaySeconds = biteDelay;
            if (waterData != null) service = new WaterQueryService(waterData);
        }

        /// <summary>Begins a cast at the bobber position; null when there is no water there.</summary>
        public FishingSession BeginCast(Vector3 bobberPosition, int rngSeed)
        {
            WaterSample sample = ResolveSample(bobberPosition);
            if (!sample.IsInWater || sample.Depth <= 0.05f) return null;

            return new FishingSession(this, sample.Depth, rngSeed);
        }

        internal WaterSample ResolveSample(Vector3 position)
        {
            if (service == null && waterData != null) service = new WaterQueryService(waterData);
            return service != null ? service.Sample(position) : WaterSample.Air;
        }

        /// <summary>Weighted pick among entries whose depth band contains <paramref name="depth"/>.</summary>
        public string RollCatch(float depth, System.Random random)
        {
            int total = 0;
            foreach (FishEntry entry in table)
            {
                if (depth < entry.minDepth || depth > entry.maxDepth) continue;
                total += Math.Max(1, entry.weight);
            }
            if (total == 0) return null;

            int roll = random.Next(total);
            foreach (FishEntry entry in table)
            {
                if (depth < entry.minDepth || depth > entry.maxDepth) continue;
                roll -= Math.Max(1, entry.weight);
                if (roll < 0) return entry.itemId;
            }
            return null;
        }
    }

    public enum FishingPhase { Idle, WaitingForBite, Biting, Caught, Escaped }

    /// <summary>
    /// Deterministic fishing state machine: cast → wait → bite window → reel (catch by
    /// weighted roll) or escape. Feed <see cref="Tick"/> from gameplay; call
    /// <see cref="TryReel"/> on player input.
    /// </summary>
    public sealed class FishingSession
    {
        private readonly FishingSpot spot;
        private readonly float depth;
        private readonly System.Random random;
        private float phaseTimer;
        private string pendingCatch;

        public FishingPhase Phase { get; private set; } = FishingPhase.WaitingForBite;

        /// <summary>Public for tooling/tests; gameplay normally goes through FishingSpot.BeginCast.</summary>
        public FishingSession(FishingSpot owner, float waterDepth, int rngSeed)
        {
            spot = owner;
            depth = waterDepth;
            random = new System.Random(rngSeed == 0 ? Guid.NewGuid().GetHashCode() : rngSeed);
            double range = Math.Max(0f, owner.BiteDelay.y - owner.BiteDelay.x);
            phaseTimer = (float)(owner.BiteDelay.x + random.NextDouble() * range);
        }

        /// <summary>Seconds left in the current bite window (0 outside Biting).</summary>
        public float BiteWindowRemaining =>
            Phase == FishingPhase.Biting ? Mathf.Max(0f, spot.ReelWindow - phaseTimer) : 0f;

        public void Tick(float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);
            switch (Phase)
            {
                case FishingPhase.WaitingForBite:
                    phaseTimer -= deltaTime;
                    if (phaseTimer <= 0f)
                    {
                        // Pre-roll the catch so the window means something.
                        pendingCatch = spot.RollCatch(depth, random);
                        Phase = pendingCatch != null ? FishingPhase.Biting : FishingPhase.Escaped;
                        phaseTimer = 0f;
                    }
                    break;

                case FishingPhase.Biting:
                    phaseTimer += deltaTime;
                    if (phaseTimer >= spot.ReelWindow)
                    {
                        Phase = FishingPhase.Escaped;
                        pendingCatch = null;
                    }
                    break;
            }
        }

        /// <summary>Pulls the line. During the bite window this lands the pre-rolled catch.</summary>
        public bool TryReel(out string itemId)
        {
            itemId = null;
            if (Phase != FishingPhase.Biting) return false;
            itemId = pendingCatch;
            pendingCatch = null;
            Phase = FishingPhase.Caught;
            return true;
        }
    }
}
