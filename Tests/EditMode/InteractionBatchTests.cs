using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    public sealed class InteractionBatchTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        private sealed class ConstantWater : IWaterQueryService
        {
            public float Depth = 8f;
            public WaterSample Sample(Vector3 position) =>
                new WaterSample(FluidType.Water, 0f, Depth, Vector3.zero, 0f, 1, 100);
            public int SampleBatch(Vector3[] positions, WaterSample[] results)
            {
                for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
                return positions.Length;
            }
        }

        // ---- FishingSession ----

        [Test]
        public void Fishing_BiteWindowReelCatchesPreRolledFish()
        {
            var spotGo = new GameObject("spot");
            objects.Add(spotGo);
            FishingSpot spot = spotGo.AddComponent<FishingSpot>();

            var tableField = typeof(FishingSpot).GetField("table",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tableField.SetValue(spot, new List<FishingSpot.FishEntry>
            {
                new FishingSpot.FishEntry { itemId = "bass", weight = 10, minDepth = 1f, maxDepth = 999f }
            });
            var delayField = typeof(FishingSpot).GetField("biteDelaySeconds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            delayField.SetValue(spot, new Vector2(0.5f, 0.6f));
            var windowField = typeof(FishingSpot).GetField("reelWindowSeconds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            windowField.SetValue(spot, 1.0f);

            // Session with a fixed seed; tick until the bite opens.
            var session = new FishingSession(spot, 8f, rngSeed: 42);
            for (int i = 0; i < 120 && session.Phase == FishingPhase.WaitingForBite; i++)
                session.Tick(0.05f);

            Assert.That(session.Phase, Is.EqualTo(FishingPhase.Biting),
                "shallow water + matching entry must eventually bite");

            float remainingBefore = session.BiteWindowRemaining;
            Assert.That(session.TryReel(out string itemId), Is.True);
            Assert.That(itemId, Is.EqualTo("bass"));
            Assert.That(session.Phase, Is.EqualTo(FishingPhase.Caught));

            // Reeling after the catch does nothing.
            Assert.That(session.TryReel(out _), Is.False);

            _ = remainingBefore;
        }

        [Test]
        public void Fishing_MissingBiteEscapes()
        {
            var spotGo = new GameObject("spot");
            objects.Add(spotGo);
            FishingSpot spot = spotGo.AddComponent<FishingSpot>();
            SetTable(spot, "bass");

            var session = new FishingSession(spot, 8f, rngSeed: 99);
            for (int i = 0; i < 200 && session.Phase == FishingPhase.WaitingForBite; i++)
                session.Tick(0.05f);

            Assert.That(session.Phase, Is.EqualTo(FishingPhase.Biting));

            // Let the whole reel window lapse without reeling.
            for (int i = 0; i < 40; i++) session.Tick(0.05f);
            Assert.That(session.Phase, Is.EqualTo(FishingPhase.Escaped));
        }

        [Test]
        public void Fishing_DepthGateExcludesDeepOnlySpecies()
        {
            var spotGo = new GameObject("spot");
            objects.Add(spotGo);
            FishingSpot spot = spotGo.AddComponent<FishingSpot>();
            SetTable(spot, "abyssal_eel", minDepth: 30f);

            string rolled = spot.RollCatch(depth: 5f, random: new System.Random(7));
            Assert.That(rolled, Is.Null.Or.Empty, "shallow roll must not pick the deep-only fish");
        }

        private static void SetTable(FishingSpot spot, string itemId, float minDepth = 1f)
        {
            var tableField = typeof(FishingSpot).GetField("table",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tableField.SetValue(spot, new List<FishingSpot.FishEntry>
            {
                new FishingSpot.FishEntry { itemId = itemId, weight = 10,
                    minDepth = minDepth, maxDepth = 999f }
            });
        }

        // ---- WaterBreather ----

        [Test]
        public void WaterBreather_DrainsUnderwaterAndRefillsInAir()
        {
            var go = new GameObject("breather");
            objects.Add(go);
            var breather = go.AddComponent<WaterBreather>();

            var water = new ConstantWater { Depth = 8f };
            breather.QueryServiceOverride = water;

            // Drive the testable core directly (Time.deltaTime is 0 in edit mode).
            breather.SamplerOverride = null; // classifier treats missing sampler as open
            // Force submerged classification path: constant water says submerged.
            for (int i = 0; i < 60; i++) breather.Simulate(0.1f);

            // With a null sampler the domain resolves from water only ??Underwater.
            Assert.That(breather.IsSubmerged, Is.True);
            float drainedAfterSubmersion = breather.AirRatio;
            Assert.That(drainedAfterSubmersion, Is.LessThan(1f), "non-gilled creature loses air");

            // Surface: refill.
            breather.QueryServiceOverride = new ConstantWater { Depth = 0.01f };
            for (int i = 0; i < 60; i++) breather.Simulate(0.1f);
            Assert.That(breather.AirRatio, Is.GreaterThan(drainedAfterSubmersion));
        }

        [Test]
        public void WaterBreather_GilledCreatureNeverDrowns()
        {
            var go = new GameObject("fish");
            objects.Add(go);
            var breather = go.AddComponent<WaterBreather>();
            SetPrivate(breather, "gilled", true);
            breather.QueryServiceOverride = new ConstantWater { Depth = 20f };

            for (int i = 0; i < 300; i++) breather.Simulate(0.1f);

            Assert.That(breather.IsDrowning, Is.False);
            Assert.That(breather.AirRatio, Is.EqualTo(1f).Within(0.01f));
        }

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(target, value);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                if (go != null) Object.DestroyImmediate(go);
            objects.Clear();
        }
    }
}
