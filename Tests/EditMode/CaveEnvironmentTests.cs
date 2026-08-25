using System;
using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    public sealed class CaveEnvironmentTests
    {
        private static TerrainShapeParams CreateTerrainParams()
        {
            TerrainShapeParams parameters = ScriptableObject.CreateInstance<TerrainShapeParams>();
            parameters.seed = 42;
            parameters.baseHeight = 20f;
            parameters.heightAmplitude = 30f;
            parameters.featureScale = 180f;
            parameters.octaves = 4;
            parameters.islandRadius = 0f;
            parameters.bottomClampY = -40f;
            return parameters;
        }

        private static CaveShapeParams CreateCaveParams()
        {
            CaveShapeParams caves = ScriptableObject.CreateInstance<CaveShapeParams>();
            caves.seedOffset = 911;
            caves.minY = -38f;
            caves.maxY = 24f;
            caves.surfaceProtectDepth = 6f;
            caves.tunnelScale = 30f;
            caves.tunnelWidth = 0.2f;
            caves.roomScale = 55f;
            caves.roomThreshold = 0.5f;
            return caves;
        }

        private static void WriteFlatStore(VoxelStoreAsset store, TerrainShapeParams p)
        {
            TerrainField.HeightMap heights = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 33, 4f);
            TerrainField.WriteDensity(store, heights, p, 128f, 16);
        }

        // ---- CaveField ----

        [Test]
        public void Carve_OpensCavernsBelowIntactSurface()
        {
            TerrainShapeParams shape = CreateTerrainParams();
            CaveShapeParams caves = CreateCaveParams();

            VoxelStoreAsset carvedStore = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            WriteFlatStore(carvedStore, shape);

            var sampler = new VoxelWorldSampler(carvedStore, 128f);
            float surfaceY = FindSurface(sampler, 64f, 64f);
            Assert.That(surfaceY, Is.GreaterThan(-1f), "terrain must exist before carving");

            int changed = CaveField.Carve(carvedStore,
                TerrainField.BuildHeightMap(shape, new Vector2(0f, 0f), 33, 4f),
                shape, caves, 128f);

            Assert.That(changed, Is.GreaterThan(0), "cave field should carve something");

            // Surface protection: the crust just below the surface stays solid.
            float justBelowSurface = sampler.Sample(64f, surfaceY - 3f, 64f);
            Assert.That(justBelowSurface, Is.GreaterThanOrEqualTo(SurfaceNetsMesher.IsoLevel),
                $"surface crust at y={surfaceY - 3f:F1} was carved (density {justBelowSurface:F3})");

            // Deep down, carving must have opened air somewhere in the store.
            bool anyAirOpened = false;
            for (float y = caves.minY; y < surfaceY - caves.surfaceProtectDepth && !anyAirOpened; y += 1f)
            {
                for (float xz = 16f; xz <= 112f && !anyAirOpened; xz += 8f)
                {
                    if (sampler.Sample(xz, y, xz) < 0.05f) anyAirOpened = true;
                }
            }
            Assert.That(anyAirOpened, Is.True, "no cave air found below the protection shell");
        }

        [Test]
        public void Carve_IsDeterministic()
        {
            TerrainShapeParams shape = CreateTerrainParams();
            CaveShapeParams caves = CreateCaveParams();

            VoxelStoreAsset first = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            VoxelStoreAsset second = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            WriteFlatStore(first, shape);
            WriteFlatStore(second, shape);

            var heights = TerrainField.BuildHeightMap(shape, new Vector2(0f, 0f), 33, 4f);
            int a = CaveField.Carve(first, heights, shape, caves, 128f);
            int b = CaveField.Carve(second, heights, shape, caves, 128f);

            Assert.That(a, Is.EqualTo(b));
            foreach (Vector3Int key in first.Coords)
            {
                Assert.That(second.TryGetEntry(key, out VoxelChunkEntry otherEntry), Is.True);
                first.TryGetEntry(key, out VoxelChunkEntry entry);
                CollectionAssert.AreEqual(entry.density, otherEntry.density, $"chunk {key}");
            }
        }

        [Test]
        public void CarveAmountAt_RespectsVerticalRangeAndProtection()
        {
            CaveShapeParams caves = CreateCaveParams();
            var noise = new FbmNoise(42 + caves.seedOffset);

            Assert.That(CaveField.CarveAmountAt(noise, caves,
                new float3(64f, caves.minY - 10f, 64f), 40f), Is.EqualTo(0f), "below minY");
            Assert.That(CaveField.CarveAmountAt(noise, caves,
                new float3(64f, caves.maxY + 10f, 64f), 60f), Is.EqualTo(0f), "above maxY");
            Assert.That(CaveField.CarveAmountAt(noise, caves,
                new float3(64f, 30f, 64f), 34f), Is.EqualTo(0f), "inside surface protection shell");
        }

        [Test]
        public void CavePresets_ProduceDistinctShapes()
        {
            CaveShapeParams caves = ScriptableObject.CreateInstance<CaveShapeParams>();
            CavePresets.Apply(caves, CavePreset.LimestoneCaves);
            int limestoneWidth = Mathf.RoundToInt(caves.tunnelWidth * 1000f);

            CavePresets.Apply(caves, CavePreset.LavaTubes);
            Assert.That(caves.tunnelVerticalSquash, Is.GreaterThan(1.9f), "lava tubes are flat tubes");
            Assert.That(Mathf.RoundToInt(caves.tunnelWidth * 1000f), Is.Not.EqualTo(limestoneWidth));

            CavePresets.Apply(caves, CavePreset.FloodedGrotto);
            Assert.That(caves.maxY, Is.LessThan(12f), "grottos stay below the waterline band");

            CavePresets.Apply(caves, CavePreset.AbyssalNetwork);
            Assert.That(caves.tunnelScale, Is.GreaterThan(60f), "abyssal tunnels are broad");
        }

        private static float FindSurface(VoxelWorldSampler sampler, float x, float z)
        {
            for (float y = 120f; y > -60f; y -= 0.25f)
            {
                if (sampler.Sample(x, y, z) >= SurfaceNetsMesher.IsoLevel) return y;
            }
            return float.MinValue;
        }

        // ---- TerrainDeformer.Drill ----

        [Test]
        public void Drill_CarvesContinuousTunnelAlongSegment()
        {
            const int resolution = 16;
            const float chunkSize = 16f;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();

            for (int cy = -1; cy <= 0; cy++)
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, cy, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }

            TerrainDeformer.ResetJournal();
            var sampler = new VoxelWorldSampler(store, chunkSize);

            int changed = TerrainDeformer.Drill(store, chunkSize,
                new Vector3(4f, 8f, 8f), new Vector3(28f, 8f, 8f),
                radius: 2.5f, delta: -2f);

            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(TerrainDeformer.EditedChunks.Count, Is.GreaterThan(0));

            // The whole swept path is open air.
            for (float x = 5f; x <= 27f; x += 1f)
            {
                float density = sampler.Sample(x, 8f, 8f);
                Assert.That(density, Is.LessThan(SurfaceNetsMesher.IsoLevel),
                    $"tunnel blocked at x={x:F0} (density {density:F3})");
            }

            // Rock far from the path stays solid.
            Assert.That(sampler.Sample(8f, 14f, 2f), Is.GreaterThanOrEqualTo(SurfaceNetsMesher.IsoLevel));

            TerrainDeformer.ResetJournal();
        }

        // ---- CaveAmbientTint ----

        [Test]
        public void AmbientTint_DarkensCoveredVerticesOnly()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var sampler = new VoxelWorldSampler(store, 16f);

            Color biome = new Color(0.9f, 0.6f, 0.3f, 1f);
            Color insideRoom = CaveAmbientTint.Shade(sampler, roomCenter, biome, maxRay: 24f, step: 1f);
            Color aboveRoof = CaveAmbientTint.Shade(sampler, new Vector3(8f, 40f, 8f), biome,
                maxRay: 24f, step: 1f);

            Assert.That(insideRoom, Is.Not.EqualTo(biome), "room interior must darken");
            Assert.That(aboveRoof, Is.EqualTo(biome), "open-sky vertex passes through untouched");
            Assert.That(insideRoom.r, Is.LessThan(biome.r));

            // Deep cover converges toward the shadow color; shallow cover blends lightly.
            Color deep = CaveAmbientTint.Apply(biome, CaveAmbientTint.FullShadeCover * 2f, true);
            Color shallow = CaveAmbientTint.Apply(biome, 1.5f, true);
            Assert.That(deep.r, Is.LessThan(shallow.r));
            Color openSky = CaveAmbientTint.Apply(biome, 99f, false);
            Assert.That(openSky, Is.EqualTo(biome));
        }

        [Test]
        public void ClassifierBatch_MatchesSingleSample()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var sampler = new VoxelWorldSampler(store, 16f);

            var flooded = new ConstantWater(new WaterSample(
                FluidType.Water, 12f, 12f - roomCenter.y, Vector3.zero, 0f, 1, 100));

            var positions = new[]
            {
                roomCenter,
                new Vector3(8f, 40f, 8f),
                Vector3.zero
            };
            var results = new EnvironmentDomain[positions.Length];

            int written = EnvironmentClassifier.ClassifyBatch(flooded, sampler, positions, results,
                maxCoverRay: 48f);

            Assert.That(written, Is.EqualTo(positions.Length));
            Assert.That(results[0], Is.EqualTo(EnvironmentClassifier.Classify(
                flooded, sampler, positions[0], maxCoverRay: 48f)));
            Assert.That(results[0], Is.EqualTo(EnvironmentDomain.FloodedCave));
            Assert.That(results[1], Is.EqualTo(EnvironmentDomain.Underwater));
        }

        // ---- UndergroundProbe / EnvironmentClassifier ----        /// <summary>Fills every chunk in [-1..1]³ solid, then hollows a box room at the centre.</summary>
        private static VoxelStoreAsset BuildSolidStoreWithRoom(out Vector3 roomCenter)
        {
            const int resolution = 16;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();

            for (int cy = -1; cy <= 1; cy++)
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, cy, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }

            // Hollow room: lattice [6..10]³ inside chunk (0,0,0) → world [6..11)³.
            store.TryGetEntry(new Vector3Int(0, 0, 0), out VoxelChunkEntry centerEntry);
            for (int cy = 6; cy <= 10; cy++)
            for (int cz = 6; cz <= 10; cz++)
            for (int cx = 6; cx <= 10; cx++)
            {
                store.SetDensity(centerEntry, cx, cy, cz, 0f);
            }

            roomCenter = new Vector3(8f, 8f, 8f);
            return store;
        }

        [Test]
        public void Probe_DetectsCaveCeilingAboveRoom()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var sampler = new VoxelWorldSampler(store, 16f);

            EnclosureSample enclosure = UndergroundProbe.Probe(sampler, roomCenter, maxRay: 48f);
            Assert.That(enclosure.IsEnclosed, Is.True, "point inside a hollow room must be enclosed");
            // Ceiling is the solid at y=11 → cover ≈ 3 m from the room centre.
            Assert.That(enclosure.CoverThickness, Is.EqualTo(3f).Within(1f));

            EnclosureSample buried = UndergroundProbe.Probe(
                sampler, new Vector3(2f, 8f, 8f), maxRay: 48f);
            Assert.That(buried.IsEnclosed, Is.True, "solid rock counts as covered");
            Assert.That(buried.CoverThickness, Is.EqualTo(0f).Within(0.01f), "buried points have no air gap");
        }

        [Test]
        public void Probe_OpenSkyIsNotEnclosed()
        {
            TerrainShapeParams shape = CreateTerrainParams();
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            WriteFlatStore(store, shape);

            var sampler = new VoxelWorldSampler(store, 128f);
            float surfaceY = FindSurface(sampler, 64f, 64f);
            Assert.That(surfaceY, Is.GreaterThan(-1f));

            EnclosureSample above = UndergroundProbe.Probe(sampler,
                new Vector3(64f, surfaceY + 20f, 64f), maxRay: 96f);
            Assert.That(above.IsEnclosed, Is.False, "air well above terrain is open sky");

            EnclosureSample onGround = UndergroundProbe.Probe(sampler,
                new Vector3(64f, surfaceY + 0.75f, 64f), maxRay: 96f);
            Assert.That(onGround.IsEnclosed, Is.False, "standing on the surface sees open sky");
        }

        private sealed class ConstantWater : IWaterQueryService
        {
            private readonly WaterSample sample;

            public ConstantWater(WaterSample sample) => this.sample = sample;

            public WaterSample Sample(Vector3 worldPosition) => sample;

            public int SampleBatch(Vector3[] positions, WaterSample[] results)
            {
                for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
                return positions.Length;
            }
        }

        [Test]
        public void Classifier_SeparatesUnderwaterUndergroundAndFloodedCave()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var sampler = new VoxelWorldSampler(store, 16f);

            // Water flooding the room from y=12 down.
            var flooded = new ConstantWater(new WaterSample(
                FluidType.Water, 12f, 12f - roomCenter.y, Vector3.zero, 0f, 1, 100));

            Assert.That(EnvironmentClassifier.Classify(flooded, sampler, roomCenter),
                Is.EqualTo(EnvironmentDomain.FloodedCave), "water inside an enclosed room");

            // Same water table, but a point high above the solid stack → open water column.
            var openOcean = new ConstantWater(new WaterSample(
                FluidType.Water, 41f, 1f, Vector3.zero, 0f, 1, 100));

            Assert.That(EnvironmentClassifier.Classify(openOcean, sampler,
                    new Vector3(8f, 40f, 8f)),
                Is.EqualTo(EnvironmentDomain.Underwater));

            Assert.That(EnvironmentClassifier.Classify(null, sampler, roomCenter),
                Is.EqualTo(EnvironmentDomain.Underground), "dry enclosed room");

            Assert.That(EnvironmentClassifier.Classify(openOcean, null, Vector3.zero),
                Is.EqualTo(EnvironmentDomain.Underwater), "null sampler degrades to water-only");

            Assert.That(EnvironmentClassifier.Classify(null, null, Vector3.zero),
                Is.EqualTo(EnvironmentDomain.OpenAir));
        }
    }
}
