using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;
namespace WorldBuilder.Tests
{
    public sealed class FlowFeatureTests
    {
        // ---- GroundwaterService ----

        private sealed class DryService : IWaterQueryService
        {
            public WaterSample Sample(Vector3 position) => WaterSample.Air;
            public int SampleBatch(Vector3[] positions, WaterSample[] results)
            {
                for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
                return positions.Length;
            }
        }

        [Test]
        public void Groundwater_FloodsBelowTableAndYieldsToAuthoredBodies()
        {
            var groundwater = new GroundwaterService(new DryService(), waterTableY: 9f);

            // Below the table → still groundwater up to the table line.
            WaterSample below = groundwater.Sample(new Vector3(500f, 3f, 5f));
            Assert.That(below.IsInWater, Is.True);
            Assert.That(below.SurfaceHeight, Is.EqualTo(9f).Within(1e-4));
            Assert.That(below.Depth, Is.EqualTo(6f).Within(1e-4));

            WaterSample above = groundwater.Sample(new Vector3(500f, 12f, 5f));
            Assert.That(above.IsInWater, Is.False);

            // Enclosed point below the table classifies as flooded cave.
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cy = -1; cy <= 1; cy++)
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, cy, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }
            store.TryGetEntry(new Vector3Int(0, 0, 0), out VoxelChunkEntry centerEntry);
            for (int y = 6; y <= 10; y++)
            for (int z = 6; z <= 10; z++)
            for (int x = 6; x <= 10; x++)
                store.SetDensity(centerEntry, x, y, z, 0f);

            var sampler = new VoxelWorldSampler(store, 16f);
            var domain = EnvironmentClassifier.Classify(groundwater, sampler,
                new Vector3(8f, -8.25f, 8f), maxCoverRay: 48f);
            Assert.That(domain, Is.EqualTo(EnvironmentDomain.FloodedCave),
                "sealed room under the water table is a flooded cave");
        }

        // ---- Runtime sea level / flow multiplier ----

        [Test]
        public void WaterLevelDriver_MovesSeaLevelAndFlowSpeed()
        {
            var data = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            data.Initialize(Vector3.zero, 32f, true, 0f, 1, 0,
                Vector3.zero, 0f,
                Array.Empty<RiverSegmentData>(), Array.Empty<BoxVolumeData>(),
                Array.Empty<LakeData>(), Array.Empty<CurrentZoneData>(),
                Array.Empty<Vector2>(), Array.Empty<WaterQueryCellData>(),
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                "test");

            var query = new WaterQueryService(data);
            float bakedSurface = query.Sample(new Vector3(10f, -2f, 10f)).SurfaceHeight;

            var driver = new GameObject("driver").AddComponent<WaterLevelDriver>();
            driver.Target = data;
            driver.SetIntensity(1f); // full flood

            WaterSample flooded = query.Sample(new Vector3(10f, 0.5f, 10f));
            Assert.That(flooded.IsInWater, Is.True, "flood rise submerges previously dry ground");
            Assert.That(flooded.SurfaceHeight, Is.GreaterThan(bakedSurface));

            driver.SetIntensity(0f); // drought
            WaterSample drought = query.Sample(new Vector3(10f, -0.5f, 10f));
            Assert.That(drought.IsInWater, Is.False, "drought drop exposes shallow floor");

           UnityEngine.Object.DestroyImmediate(driver.gameObject);
           UnityEngine.Object.DestroyImmediate(data);
        }

        // ---- Scatter underwater gates ----

        private sealed class SeafloorQuery : ITerrainQuery, IWaterAwareTerrainQuery
        {
            public bool TryHeight(Vector2 worldXz, out float height) { height = -12f; return true; }
            public float Slope(Vector2 worldXz) => 0f;
            public BiomeType BiomeAt(Vector2 worldXz) => BiomeType.CoralReef;

            public bool TrySampleWater(Vector3 position,
                out WorldBuilder.Runtime.Water.WaterSample sample)
            {
                sample = new WaterSample(FluidType.Water, 0f, -position.y,
                    Vector3.zero, position.x > 50f ? 4f : 0.2f, 1, 100);
                return true;
            }
        }

        [Test]
        public void ScatterRules_DepthAndFlowGatesFilterPlacements()
        {
            var ruleSet = ScriptableObject.CreateInstance<ScatterRuleSet>();
            ruleSet.rules.Add(new ScatterRuleSet.Rule
            {
                name = "coral",
                prefabs = new List<GameObject> { new GameObject("Coral") },
                densityPerSquareMeter = 0.05f,
                useDepthGate = true,
                minDepth = 8f,
                maxDepth = 20f
            });

            var bounds = Rect.MinMaxRect(0f, 0f, 200f, 200f);
            List<PcgPlacement> gated =
                PcgScatterEngine.Generate(ruleSet, new SeafloorQuery(), bounds, seed: 7);
            Assert.That(gated.Count, Is.GreaterThan(0));

            // Flow gate: torrent zones (>4 m/s excluded by maxFlowSpeed) reject everything.
            ruleSet.rules[0].maxFlowSpeed = 2f;
            List<PcgPlacement> torrentsOnly =
                PcgScatterEngine.Generate(ruleSet, new SeafloorQuery(),
                    Rect.MinMaxRect(51f, 0f, 200f, 200f), seed: 7);
            Assert.That(torrentsOnly.Count, Is.EqualTo(0), "fast flow must reject placements");

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "Coral" && go.scene.IsValid()) UnityEngine.Object.DestroyImmediate(go);
        }

        // ---- Unified snapshot v2 ----

        [Test]
        public void Snapshot_RoundTripsPlacementsTerrainAndExtras()
        {
            string tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wb_snapshot_" + Guid.NewGuid().ToString("N"));
            Func<string> previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;

            try
            {
                VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
                VoxelChunkEntry entry = store.GetOrCreate(Vector3Int.zero);
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;

                TerrainDeformer.ResetJournal();
                TerrainDeformer.Modify(store, 16f, new Vector3(8f, 8f, 8f), 3f, -2f);

                WorldSaveService.SaveSnapshot("snap_slot", store, TerrainDeformer.EditedChunks,
                    placementsJson: "{}",
                    extrasJson: "{\"weather\":\"rain\",\"seaOffset\":1.25}");

                // Wipe local state, then restore.
                store.SetVoxelData(Vector3Int.zero, SolidVoxels());
                TerrainDeformer.ResetJournal();

                bool loaded = WorldSaveService.LoadSnapshot("snap_slot", store,
                    prefabResolver: _ => null, out string extrasJson);
                Assert.That(loaded, Is.True);
                Assert.That(extrasJson, Is.Not.Null);
                Assert.That(extrasJson, Does.Contain("seaOffset"));

                store.TryGetEntry(Vector3Int.zero, out VoxelChunkEntry restoredEntry);
                Assert.That(store.GetDensity(restoredEntry, 8, 8, 8),
                    Is.LessThan(0.35f), "dug voxel restored from snapshot");
            }
            finally
            {
                WorldSaveService.DirectoryProvider = previousProvider;
                if (System.IO.Directory.Exists(tempDirectory))
                    System.IO.Directory.Delete(tempDirectory, true);
                TerrainDeformer.ResetJournal();
            }
        }

        private static VoxelData SolidVoxels()
        {
            var voxels = new VoxelData(16, 16, 16);
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                voxels.SetDensity(x, y, z, 1f);
            return voxels;
        }
    }
}
