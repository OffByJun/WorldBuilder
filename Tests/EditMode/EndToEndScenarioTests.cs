using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    /// <summary>
    /// Full authoring loop in one test: generate → carve caves → punch entrances →
    /// scatter cave ecology → dig with the deformer → snapshot save → wipe → restore →
    /// verify terrain and journal state.
    /// </summary>
    public sealed class EndToEndScenarioTests
    {
        [Test]
        public void FullPipeline_GenerateCarveScatterDigSaveRestore()
        {
            const float chunkSize = 16f;
            TerrainShapeParams shape = ScriptableObject.CreateInstance<TerrainShapeParams>();
            shape.seed = 777;
            shape.baseHeight = 20f;
            shape.heightAmplitude = 24f;
            shape.featureScale = 160f;
            shape.octaves = 3;
            shape.islandRadius = 0f;
            shape.bottomClampY = -40f;

            CaveShapeParams caves = ScriptableObject.CreateInstance<CaveShapeParams>();
            caves.seedOffset = 5;
            caves.minY = -36f;
            caves.maxY = 18f;
            caves.surfaceProtectDepth = 4f;
            caves.tunnelScale = 26f;
            caves.tunnelWidth = 0.22f;
            caves.roomScale = 45f;
            caves.roomThreshold = 0.48f;

            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();

            // 1) Generate.
            var heights = TerrainField.BuildHeightMap(shape, new Vector2(0f, 0f), 33, 4f);
            int written = TerrainField.WriteDensity(store, heights, shape, chunkSize, 16);
            Assert.That(written, Is.GreaterThan(0), "heightfield must produce chunks");

            // 2) Carve caves + entrances.
            int carved = CaveField.Carve(store, heights, shape, caves, chunkSize);
            Assert.That(carved, Is.GreaterThan(0));
            int entrances = CaveField.CarveEntrances(store, heights, shape, caves, chunkSize,
                maxEntrances: 3, shaftRadius: 1.6f);
            Assert.That(entrances, Is.GreaterThan(0), "at least one shaft should reach a cavity");

            // 3) Scatter cave interior placements.
            var sampler = new VoxelWorldSampler(store, chunkSize);
            var volumeQuery = new VoxelVolumeQuery(sampler, chunkSize);
            ScatterRuleSet ruleSet = ScriptableObject.CreateInstance<ScatterRuleSet>();
            ruleSet.rules.Add(new ScatterRuleSet.Rule
            {
                name = "ore",
                prefabs = new List<GameObject> { new GameObject("OreNode") },
                densityPerSquareMeter = 0.4f
            });
            var volumeBounds = new Bounds(new Vector3(64f, -8f, 64f), new Vector3(96f, 40f, 96f));
            List<PcgPlacement> placements =
                VoxelVolumeScatter.Generate(ruleSet, volumeQuery, volumeBounds, seed: 9);
            Assert.That(placements.Count, Is.GreaterThan(0),
                "cave interiors should host ore placements");

            // 4) Player digs into the floor of one entrance column.
            TerrainDeformer.ResetJournal();
            Vector3 digPoint = new Vector3(entranceProbeXZ.x, heights.SampleWorld(entranceProbeXZ) - 1.5f,
                entranceProbeXZ.y);
            int dug = TerrainDeformer.Modify(store, chunkSize, digPoint, radius: 2f, delta: -2f);
            Assert.That(dug, Is.GreaterThan(0));
            Assert.That(TerrainDeformer.EditedChunks.Count, Is.GreaterThan(0));

            // 5) Snapshot save → wipe → restore.
            string tempDirectory = Path.Combine(Path.GetTempPath(), "wb_e2e_" + Guid.NewGuid().ToString("N"));
            Func<string> previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;

            try
            {
                WorldSaveService.SaveSnapshot("e2e_slot", store, TerrainDeformer.EditedChunks,
                    "{}", extrasJson: "{\"scenario\":\"full-pipeline\"}");

                // Wipe everything and prove restore brings it back bit-for-bit.
                var wiped = ScriptableObject.CreateInstance<VoxelStoreAsset>();
                bool restored = WorldSaveService.LoadSnapshot("e2e_slot", wiped,
                    prefabResolver: _ => null, out string extras);

                Assert.That(restored, Is.True);
                Assert.That(extras, Does.Contain("full-pipeline"));

                foreach (Vector3Int coord in TerrainDeformer.EditedChunks)
                {
                    Assert.That(wiped.TryGetEntry(coord, out VoxelChunkEntry restoredEntry),
                        Is.True, $"chunk {coord} missing after restore");
                    store.TryGetEntry(coord, out VoxelChunkEntry original);
                    CollectionAssert.AreEqual(original.density, restoredEntry.density,
                        $"chunk {coord} densities diverged");
                }

                // Regrowth would relax these chunks back toward the baseline — verify the
                // analyzer sees no detached geometry after restoration.
                StabilityReport stability =
                    CaveStabilityAnalyzer.FindDetachedSolid(wiped, chunkSize, nodeBudget: 200000);
                Assert.That(stability.Complete, Is.True);
            }
            finally
            {
                WorldSaveService.DirectoryProvider = previousProvider;
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
                TerrainDeformer.ResetJournal();
                UnityEngine.Object.DestroyImmediate(store);
                UnityEngine.Object.DestroyImmediate(shape);
                UnityEngine.Object.DestroyImmediate(caves);
                UnityEngine.Object.DestroyImmediate(ruleSet);
            }
        }

        private static readonly Vector2 entranceProbeXZ = new Vector2(64f, 64f);
    }
}
