using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Streaming;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;
using WorldBuilder.Runtime.WorldSeed;

namespace WorldBuilder.Tests
{
    /// <summary>
    /// Stabilization pass: locks down behaviours that later refactors are most likely to
    /// break silently (seed round-trips, water priority layering, save sidecar hygiene,
    /// preset invariants).
    /// </summary>
    public sealed class StabilizationTests
    {
        // ---- World seed codec ----

        [Test]
        public void SeedCodec_ImportRoundTripPreservesEveryField()
        {
            TerrainShapeParams source = ScriptableObject.CreateInstance<TerrainShapeParams>();
            source.seed = 424242;
            source.baseHeight = 17.5f;
            source.heightAmplitude = 44f;
            source.featureScale = 233f;
            source.octaves = 7;
            source.persistence = 0.51f;
            source.lacunarity = 2.25f;
            source.ridgeWeight = 0.42f;
            source.warpStrength = 61f;
            source.warpFrequency = 0.0031f;
            source.terraceBlend = 0.3f;
            source.islandRadius = 750f;
            source.surfaceSharpness = 3.5f;
            source.bottomClampY = -52f;

            CaveShapeParams caveSource = ScriptableObject.CreateInstance<CaveShapeParams>();
            CavePresets.Apply(caveSource, CavePreset.FloodedGrotto);
            caveSource.waterTableY = -6f;

            string json = WorldSeedCodec.Export(source, caveSource);

            TerrainShapeParams shapeTarget = ScriptableObject.CreateInstance<TerrainShapeParams>();
            CaveShapeParams caveTarget = ScriptableObject.CreateInstance<CaveShapeParams>();
            Assert.That(WorldSeedCodec.TryImport(json, shapeTarget, caveTarget, out string error),
                Is.True, error ?? "");

            Assert.That(shapeTarget.seed, Is.EqualTo(source.seed));
            Assert.That(shapeTarget.baseHeight, Is.EqualTo(source.baseHeight).Within(1e-4f));
            Assert.That(shapeTarget.warpFrequency, Is.EqualTo(source.warpFrequency).Within(1e-6f));
            Assert.That(caveTarget.waterTableY, Is.EqualTo(-6f).Within(1e-4f));
            Assert.That(caveTarget.tunnelWidth, Is.EqualTo(caveSource.tunnelWidth).Within(1e-4f));

            // Fingerprint is stable for identical shapes and changes with the seed.
            Assert.That(WorldSeedCodec.Fingerprint(shapeTarget),
                Is.EqualTo(WorldSeedCodec.Fingerprint(source)));
            source.seed += 1;
            Assert.That(WorldSeedCodec.Fingerprint(shapeTarget),
                Is.Not.EqualTo(WorldSeedCodec.Fingerprint(source)));

            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(caveSource);
            UnityEngine.Object.DestroyImmediate(shapeTarget);
            UnityEngine.Object.DestroyImmediate(caveTarget);
        }

        [Test]
        public void SeedCodec_RejectsUnknownSchemaVersion()
        {
            TerrainShapeParams target = ScriptableObject.CreateInstance<TerrainShapeParams>();
            Assert.That(WorldSeedCodec.TryImport(
                "{\"schemaVersion\":99,\"terrain\":{\"seed\":1}}", target, null, out string error),
                Is.False);
            Assert.That(error, Does.Contain("schema"));
            UnityEngine.Object.DestroyImmediate(target);
        }

        // ---- Groundwater vs authored body priority ----

        private sealed class AirPocketService : IWaterQueryService
        {
            public WaterSample Sample(Vector3 position) =>
                new WaterSample(FluidType.Air, position.y + 2f, 0f, Vector3.zero, 0f,
                    555, 100); // sealed dry pocket
            public int SampleBatch(Vector3[] positions, WaterSample[] results)
            {
                for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
                return positions.Length;
            }
        }

        [Test]
        public void Groundwater_DoesNotFloodAuthoredAirPockets()
        {
            // Table far ABOVE the pocket — old behaviour flooded sealed dry rooms.
            var service = new GroundwaterService(new AirPocketService(), waterTableY: -20f);
            WaterSample sample = service.Sample(new Vector3(8f, -30f, 8f));

            Assert.That(sample.IsInWater, Is.False,
                "an authored air override below the table stays dry");
            Assert.That(sample.WaterBodyId, Is.EqualTo(555));
        }

        // ---- Stability scan budget ----

        [Test]
        public void Stability_BudgetExhaustionMarksReportIncomplete()
        {
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }

            StabilityReport report = CaveStabilityAnalyzer.FindDetachedSolid(
                store, chunkSize: 16f, nodeBudget: 50);

            Assert.That(report.Complete, Is.False, "tiny budget must truncate the flood");
            UnityEngine.Object.DestroyImmediate(store);
        }

        // ---- World map shading monotonicity ----

        [Test]
        public void DepthMap_DeeperWaterRendersDarker()
        {
            const int resolution = 16;
            const float chunkSize = 16f;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            // Two seabed terraces: shallow left half (floor -4), deep right half (-14).
            // Solid fills from the chunk bottom UP TO the floor level (a real seabed).
            foreach (int cx in new[] { 0, 1 })
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, -1, 0));
                float floorY = cx == 0 ? -4f : -14f;
                int floorTopRow = Mathf.CeilToInt((floorY + 16f) / 16f * resolution);
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                    store.SetDensity(entry, x, y, z, y <= floorTopRow ? 1f : 0f);
            }

            Color32[] map = MinimapDepthBaker.BakeDepth(store, chunkSize,
                new Vector2(0f, 0f), resolutionPx: 32, sizeMeters: 32f, seaLevel: 0f);

            // Pick texels strictly inside each chunk (away from the z=0 border where a
            // missing neighbour chunk would halve the interpolated density).
            Color32 shallow = map[12 * 32 + 12];
            Color32 deep = map[12 * 32 + 28];
            Assert.That(deep.b, Is.LessThan(shallow.b),
                $"deeper column must shade darker (shallow={shallow} deep={deep})");

            UnityEngine.Object.DestroyImmediate(store);
        }

        [Test]
        public void Groundwater_YieldsToAuthoredOceanAboveTheTable()
        {
            var data = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            data.Initialize(Vector3.zero, 32f, true, 0f, 1, 50,
                Vector3.zero, 0f,
                Array.Empty<RiverSegmentData>(), Array.Empty<BoxVolumeData>(),
                Array.Empty<LakeData>(), Array.Empty<CurrentZoneData>(),
                Array.Empty<Vector2>(), Array.Empty<WaterQueryCellData>(),
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                "gw");

            // Table at -20: the ocean surface (y=0) must still win everywhere above it.
            var service = new GroundwaterService(new WaterQueryService(data), waterTableY: -20f);

            WaterSample sample = service.Sample(new Vector3(10f, -5f, 10f));
            Assert.That(sample.IsInWater, Is.True);
            Assert.That(sample.SurfaceHeight, Is.EqualTo(0f).Within(1e-4f),
                "authored sea level outranks the groundwater table");
            Assert.That(sample.WaterBodyId, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(data);
        }

        // ---- Fishing spot validation ----

        [Test]
        public void FishingSpot_BeginCastReturnsNullOnDryLand()
        {
            var data = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            data.Initialize(Vector3.zero, 32f, true, 0f, 1, 0,
                Vector3.zero, 0f,
                Array.Empty<RiverSegmentData>(), Array.Empty<BoxVolumeData>(),
                Array.Empty<LakeData>(), Array.Empty<CurrentZoneData>(),
                Array.Empty<Vector2>(), Array.Empty<WaterQueryCellData>(),
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                "fish");

            var go = new GameObject("spot");
            FishingSpot spot = go.AddComponent<FishingSpot>();
            spot.Configure(data);

            Assert.That(spot.BeginCast(new Vector3(10f, 12f, 10f), rngSeed: 5), Is.Null,
                "casting on dry land must be rejected");
            Assert.That(spot.BeginCast(new Vector3(10f, -3f, 10f), rngSeed: 5), Is.Not.Null,
                "casting into the ocean starts a session");

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(data);
        }

        // ---- Save sidecars ----

        [Test]
        public void SaveDelete_RemovesTerrainAndExtrasSidecars()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "wb_stab_" + Guid.NewGuid().ToString("N"));
            Func<string> previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;

            try
            {
                WorldSaveService.SaveSnapshot("stab_slot", store: null, editedChunks: null,
                    placementsJson: "{}", extrasJson: "{\"k\":1}");

                Assert.That(File.Exists(Path.Combine(tempDirectory, "stab_slot.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(tempDirectory, "stab_slot_extras.json")), Is.True);

                Assert.That(WorldSaveService.Delete("stab_slot"), Is.True);
                Assert.That(Directory.GetFiles(tempDirectory, "stab_slot*"), Is.Empty,
                    "main + sidecars all removed together");
            }
            finally
            {
                WorldSaveService.DirectoryProvider = previousProvider;
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
            }
        }

        // ---- Streaming preset invariants ----

        [Test]
        public void StreamingBudgetPresets_StayWithinSaneRadii()
        {
            foreach (StreamingBudgetPreset.Profile profile in
                     Enum.GetValues(typeof(StreamingBudgetPreset.Profile)))
            {
                StreamingBudgetPreset preset = StreamingBudgetPreset.Defaults(profile);
                Assert.That(preset.regionRadius, Is.InRange(1, 8));
                Assert.That(preset.focusIntervalSeconds, Is.InRange(0.1f, 5f));
            }

            var handheld = StreamingBudgetPreset.Defaults(StreamingBudgetPreset.Profile.Handheld);
            var server = StreamingBudgetPreset.Defaults(StreamingBudgetPreset.Profile.Server);
            Assert.That(server.regionRadius, Is.GreaterThan(handledRegion(handheld)),
                "server streams wider than handheld");

            int handledRegion(StreamingBudgetPreset p) => p.regionRadius;
        }
    }
}
