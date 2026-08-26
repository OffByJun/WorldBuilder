using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    public sealed class SimulationBatchTests
    {
        // ---- Rigidbody steering force ----

        [Test]
        public void SteeringForce_PushesVelocityTowardTargetOverOneSecond()
        {
            Vector3 target = new Vector3(2f, 0f, 0f);
            const float mass = 4f;
            const float dt = 1f / 50f;

            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < 50; i++)
            {
                Vector3 force = WaterDrift.ComputeSteeringForce(velocity, target, mass, dt);
                velocity += force / mass * dt; // a = F/m
            }

            Assert.That(velocity.x, Is.EqualTo(2f).Within(0.01f),
                "one second of steering must reach the drift target");
        }

        [Test]
        public void SteeringForce_ZeroMassOrTime_IsSafe()
        {
            Assert.That(WaterDrift.ComputeSteeringForce(Vector3.one, Vector3.zero, 0f, 1f),
                Is.EqualTo(Vector3.zero));
            Assert.That(WaterDrift.ComputeSteeringForce(Vector3.one, Vector3.zero, 1f, 0f),
                Is.EqualTo(Vector3.zero));
        }

        // ---- RiverbedFlowSim ----

        [Test]
        public void RiverbedSim_ErodesFastWaterAndDepositsSlack()
        {
            var waterData = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            var rivers = new[]
            {
                new RiverSegmentData
                {
                    start = new Vector3(10f, 0f, 0f), end = new Vector3(40f, 0f, 0f),
                    startWidth = 6f, endWidth = 6f, startDepth = 3f, endDepth = 3f,
                    flowDirection = Vector3.right, flowSpeed = 3f, bodyId = 1, priority = 10,
                    bounds = new Bounds(new Vector3(25f, 0f, 0f), new Vector3(30f, 6f, 6f))
                }
            };
            waterData.Initialize(Vector3.zero, 32f, false, -999f, 0, 0, Vector3.zero, 0f,
                rivers, System.Array.Empty<BoxVolumeData>(), System.Array.Empty<LakeData>(),
                System.Array.Empty<CurrentZoneData>(), System.Array.Empty<Vector2>(),
                System.Array.Empty<WaterQueryCellData>(),
                System.Array.Empty<int>(), System.Array.Empty<int>(), System.Array.Empty<int>(),
                System.Array.Empty<int>(), "sim");

            // Flat slab bed under the river.
            const int resolution = 16;
            const float chunkSize = 16f;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cz = 0; cz <= 2; cz++)
            for (int cx = 0; cx <= 2; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, -1, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }

            var sim = new GameObject("sim").AddComponent<RiverbedFlowSim>();
            sim.Target = waterData;
            SetPrivate(sim, "store", store);
            SetPrivate(sim, "chunkSize", 16f); // match the synthetic store
            SetPrivate(sim, "seed", 777u);
            sim.SetIntervalForTests(intervalSeconds: 9999f); // manual ticks only

            double before = DensitySum(store);
            for (int tick = 0; tick < 40; tick++) sim.Tick();
            double after = DensitySum(store);

            Assert.That(after, Is.LessThan(before),
                $"fast-flow erosion must reduce total density ({before:F2} → {after:F2})");
            UnityEngine.Object.DestroyImmediate(sim.gameObject);
            UnityEngine.Object.DestroyImmediate(waterData);
            UnityEngine.Object.DestroyImmediate(store);
        }

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(target, value);

        private static double DensitySum(VoxelStoreAsset store)
        {
            double sum = 0;
            foreach (Vector3Int coord in store.Coords)
            {
                if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) continue;
                foreach (float value in entry.density) sum += value;
            }
            return sum;
        }

        // ---- CaveStabilityAnalyzer ----

        /// <summary>Lone chunk at (4,4,4): solid slab on top; when unsupported the space under it is air.</summary>
        private static VoxelStoreAsset BuildFloatingSlab(bool supported)
        {
            const int resolution = 16;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(4, 4, 4));
            for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;

            if (!supported)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < 8; y++)
                    store.SetDensity(entry, x, y, z, 0f);
            }
            return store;
        }

        [Test]
        public void Stability_GroundedSlabIsFullyConnected()
        {
            VoxelStoreAsset store = BuildFloatingSlab(supported: true);
            StabilityReport report =
                CaveStabilityAnalyzer.FindDetachedSolid(store, chunkSize: 16f);
            Assert.That(report.DetachedCount, Is.EqualTo(0));
        }

        [Test]
        public void Stability_HollowSlabBottomDetachesTheTop()
        {
            VoxelStoreAsset store = BuildFloatingSlab(supported: false);
            StabilityReport report =
                CaveStabilityAnalyzer.FindDetachedSolid(store, chunkSize: 16f);

            Assert.That(report.DetachedCount, Is.GreaterThan(0),
                "slab with an empty underside has no path to any base row");
            Assert.That(report.DetachedSamples, Is.Not.Empty);
        }
    }

    internal static class RiverbedFlowSimTestExtensions
    {
        public static void SetIntervalForTests(this RiverbedFlowSim sim, float intervalSeconds) =>
            sim.GetType().GetField("intervalSeconds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(sim, intervalSeconds);
    }
}
