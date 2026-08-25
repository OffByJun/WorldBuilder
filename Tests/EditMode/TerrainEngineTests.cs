using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Editing;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class TerrainEngineTests
    {
        private static TerrainShapeParams CreateParams()
        {
            TerrainShapeParams parameters = ScriptableObject.CreateInstance<TerrainShapeParams>();
            parameters.seed = 42;
            parameters.baseHeight = 20f;
            parameters.heightAmplitude = 30f;
            parameters.featureScale = 180f;
            parameters.octaves = 4;
            parameters.islandRadius = 0f;   // no falloff for predictable tests
            parameters.bottomClampY = -40f;
            return parameters;
        }

        [TearDown]
        public void TearDown()
        {
            // Nothing persistent: ScriptableObjects are created per test and GC'd in edit mode.
        }

        // ---- FbmNoise ----

        [Test]
        public void FbmNoise_IsDeterministicPerSeed()
        {
            var a = new FbmNoise(7);
            var b = new FbmNoise(7);
            var c = new FbmNoise(8);

            float va = a.Value2D(new float2(12.3f, 45.6f), 0.01f, 4, 0.5f, 2f);
            float vb = b.Value2D(new float2(12.3f, 45.6f), 0.01f, 4, 0.5f, 2f);
            float vc = c.Value2D(new float2(12.3f, 45.6f), 0.01f, 4, 0.5f, 2f);

            Assert.That(va, Is.EqualTo(vb));
            Assert.That(va, Is.Not.EqualTo(vc).Within(1e-6f));
        }

        // ---- Procedural generation ----

        [Test]
        public void HeightAt_IsDeterministicAndBounded()
        {
            TerrainShapeParams p = CreateParams();
            var noiseA = new FbmNoise(p.seed);

            float h1 = TerrainField.HeightAt(noiseA, p, new float2(100f, 100f));
            float h2 = TerrainField.HeightAt(noiseA, p, new float2(100f, 100f));

            Assert.That(h1, Is.EqualTo(h2));
            Assert.That(h1, Is.LessThan(p.baseHeight + p.heightAmplitude + p.ridgeWeight * p.heightAmplitude * 0.5f + 1f));
            Assert.That(h1, Is.GreaterThan(p.baseHeight - p.heightAmplitude - 1f));
        }

        [Test]
        public void WriteDensity_ProducesSolidBelowSurfaceAndAirAbove()
        {
            TerrainShapeParams p = CreateParams();
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();

            Vector2 origin = new Vector2(0f, 0f);
            TerrainField.HeightMap heights = TerrainField.BuildHeightMap(p, origin, 33, 4f); // 128 m
            int written = TerrainField.WriteDensity(store, heights, p, 128f, 16);
            Assert.That(written, Is.GreaterThan(0));

            // Sample the column at world (64, z=64): find surface via density crossing.
            var sampler = new VoxelWorldSampler(store, 128f);
            float surfaceY = FindSurface(sampler, 64f, 64f);
            Assert.That(surfaceY, Is.GreaterThan(-1f), $"terrain surface should exist (found {surfaceY})");

            float below = sampler.Sample(64f, surfaceY - 3f, 64f);
            float above = sampler.Sample(64f, surfaceY + 2f, 64f);
            Assert.That(below, Is.GreaterThan(0.65f), $"below={below:F3} at y={surfaceY - 3f}");
            Assert.That(above, Is.LessThan(0.35f), $"above={above:F3} at y={surfaceY + 2f}");
        }

        private static float FindSurface(VoxelWorldSampler sampler, float x, float z)
        {
            for (float y = 120f; y > -60f; y -= 0.5f)
            {
                if (sampler.Sample(x, y, z) >= SurfaceNetsMesher.IsoLevel) return y;
            }
            return float.MinValue;
        }

        // ---- Erosion ----

        [Test]
        public void Erosion_IsDeterministicAndSmoothsPeaks()
        {
            TerrainShapeParams p = CreateParams();
            TerrainField.HeightMap a = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 65, 4f);
            TerrainField.HeightMap b = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 65, 4f);

            float peakBefore = MaxHeight(a);
            ErosionSimulator.Apply(a, new ErosionSimulator.Params { DropletCount = 1500 }, 99);
            float peakAfter = MaxHeight(a);

            Assert.That(MaxHeight(b), Is.EqualTo(peakBefore));      // untouched map unchanged
            ErosionSimulator.Apply(b, new ErosionSimulator.Params { DropletCount = 1500 }, 99);
            Assert.That(Flatten(a.Heights), Is.EqualTo(Flatten(b.Heights))); // deterministic

            Assert.That(peakAfter, Is.LessThanOrEqualTo(peakBefore), "erosion should not raise peaks");
        }

        private static float[] Flatten(float[] source) => (float[])source.Clone();

        private static float MaxHeight(TerrainField.HeightMap map) => map.Heights.Max();

        // ---- Surface Nets meshing ----

        [Test]
        public void Mesher_BuildsMeshForHalfSpace()
        {
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            // Hard step at lattice y=8 across a 3×3 chunk neighbourhood so the centre
            // chunk has no exposed side walls — only the horizontal boundary plane.
            const int resolution = 16;
            for (int cz = -1; cz <= 1; cz++)
            {
                for (int cx = -1; cx <= 1; cx++)
                {
                    // Step layer.
                    VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                    for (int x = 0; x < resolution; x++)
                    for (int y = 0; y < resolution; y++)
                    for (int z = 0; z < resolution; z++)
                        store.SetDensity(entry, x, y, z, y <= 7 ? 1f : 0f);

                    // Solid layer underneath so the slab has no exposed bottom wall.
                    VoxelChunkEntry under = store.GetOrCreate(new Vector3Int(cx, -1, cz));
                    for (int i = 0; i < under.density.Length; i++) under.density[i] = 1f;
                }
            }

            var sampler = new VoxelWorldSampler(store, 16f);
            SurfaceNetsMesher.Result result =
                SurfaceNetsMesher.Mesh(sampler, new Vector3Int(0, 0, 0), resolution, 16f);

            Assert.That(result.Mesh, Is.Not.Null);
            Assert.That(result.VertexCount, Is.GreaterThan(0));
            Assert.That(result.TriangleCount, Is.GreaterThan(0));

            // Interior vertices hug the boundary plane y ≈ 8 (spacing = 1 m); skirt cells
            // at the chunk's outer border may add wall geometry, which we exclude here.
            int interior = 0;
            foreach (Vector3 vertex in result.Mesh.vertices)
            {
                if (vertex.x < 0f || vertex.x > 16f || vertex.z < 0f || vertex.z > 16f ||
                    vertex.y < -0.5f || vertex.y > 16.5f) continue;
                Assert.That(vertex.y, Is.EqualTo(8f).Within(1.5f), $"interior vertex at {vertex}");
                interior++;
            }
            Assert.That(interior, Is.GreaterThanOrEqualTo(resolution * resolution / 4),
                "expected a full plane of interior vertices");
        }

        [Test]
        public void Mesher_IsDeterministic()
        {
            TerrainShapeParams p = CreateParams();
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            var heights = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 33, 4f);
            TerrainField.WriteDensity(store, heights, p, 128f, 16);

            var sampler = new VoxelWorldSampler(store, 128f);
            SurfaceNetsMesher.Result first =
                SurfaceNetsMesher.Mesh(sampler, new Vector3Int(0, 0, 0), 16, 128f);
            SurfaceNetsMesher.Result second =
                SurfaceNetsMesher.Mesh(sampler, new Vector3Int(0, 0, 0), 16, 128f);

            if (first.Mesh == null)
            {
                Assert.That(second.Mesh, Is.Null); // no terrain in this chunk at all
                return;
            }

            Assert.That(first.VertexCount, Is.EqualTo(second.VertexCount));
            CollectionAssert.AreEqual(first.Mesh.vertices, second.Mesh.vertices);
        }

        // ---- v0.7.0 performance & features ----

        [Test]
        public void ComputeGeometry_ParallelMatchesSequential()
        {
            TerrainShapeParams p = CreateParams();
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            var heights = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 65, 4f);
            TerrainField.WriteDensity(store, heights, p, 128f, 16);

            var coords = new[]
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
                new Vector3Int(0, 0, -1), new Vector3Int(-1, 0, 1)
            };

            foreach (Vector3Int coord in coords)
            {
                var sequential = new VoxelWorldSampler(store, 128f);
                var threaded = new VoxelWorldSampler(store, 128f); // per-thread sampler pattern

                SurfaceNetsMesher.MeshGeometry a =
                    SurfaceNetsMesher.ComputeGeometry(sequential, coord, 16, 128f);
                SurfaceNetsMesher.MeshGeometry b = null;
                System.Threading.Tasks.Parallel.Invoke(() =>
                {
                    b = SurfaceNetsMesher.ComputeGeometry(threaded, coord, 16, 128f);
                });

                if (a.IsEmpty)
                {
                    Assert.That(b.IsEmpty, Is.True);
                    continue;
                }

                CollectionAssert.AreEqual(a.Vertices, b.Vertices, $"vertices {coord}");
                CollectionAssert.AreEqual(a.Triangles, b.Triangles, $"triangles {coord}");
            }
        }

        [Test]
        public void VertexBiomeColors_AreAppliedWhenSamplerProvided()
        {
            HighResBiomeMap map = ScriptableObject.CreateInstance<HighResBiomeMap>();
            map.Configure(2,
                new System.Collections.Generic.List<Vector3Int> { new Vector3Int(0, 0, 0) },
                new System.Collections.Generic.List<byte[]>
                {
                    new byte[]
                    {
                        (byte)BiomeType.Rocky, (byte)BiomeType.Rocky,
                        (byte)BiomeType.Rocky, (byte)BiomeType.Rocky
                    }
                });

            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(0, 0, 0));
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                store.SetDensity(entry, x, y, z, y <= 7 ? 1f : 0f);
            // Fill neighbours so no side walls exist.
            foreach (var coord in new[] { new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
                                          new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1) })
            {
                var e = store.GetOrCreate(coord);
                for (int i = 0; i < e.density.Length; i++) e.density[i] = 1f;
            }

            var sampler = new VoxelWorldSampler(store, 16f);
            SurfaceNetsMesher.Result result = SurfaceNetsMesher.Mesh(
                sampler, new Vector3Int(0, 0, 0), 16, 16f,
                v => map.SampleColor(v.x, v.z, 16f));

            Assert.That(result.Mesh, Is.Not.Null);
            Color[] colors = result.Mesh.colors;
            Assert.That(colors.Length, Is.EqualTo(result.VertexCount));

            // Interior vertices must carry exactly what the biome sampler produces
            // (validates the plumbing). All mapped cells are Rocky, so the blended
            // result is Rocky within floating-point tolerance.
            Color rocky = BiomeClassifier.DebugColor(BiomeType.Rocky);
            int sampledCount = 0;
            for (int i = 0; i < result.Mesh.vertices.Length; i++)
            {
                Vector3 v = result.Mesh.vertices[i];
                if (v.x is < 1f or > 15f || v.z is < 1f or > 15f || v.y is < 1f or > 15f) continue;
                Color expected = map.SampleColor(v.x, v.z, 16f);
                Assert.That(colors[i], Is.EqualTo(expected), $"vertex {v}");
                Assert.That(expected.r, Is.EqualTo(rocky.r).Within(1e-3f), $"blend drift at {v}");
                Assert.That(expected.g, Is.EqualTo(rocky.g).Within(1e-3f), $"blend drift at {v}");
                Assert.That(expected.b, Is.EqualTo(rocky.b).Within(1e-3f), $"blend drift at {v}");
                sampledCount++;
            }
            Assert.That(sampledCount, Is.GreaterThan(0));
        }

        [Test]
        public void DeformerJournal_AndTerrainSave_RoundTrip()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "wb_terrain_save_" + Guid.NewGuid().ToString("N"));
            Func<string> previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;

            try
            {
                VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
                VoxelChunkEntry entry = store.GetOrCreate(Vector3Int.zero);
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;

                RuntimePlacementService.Reset();
                int changed = TerrainDeformer.Modify(store, 16f, new Vector3(8f, 8f, 8f), 4f, -2f);
                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(TerrainDeformer.EditedChunks.Count, Is.GreaterThan(0));

                WorldSaveService.Save("terrain_slot", "{}", "W");
                WorldSaveService.SaveTerrain("terrain_slot", store, TerrainDeformer.EditedChunks);

                float dugBefore = store.GetDensity(entry, 8, 8, 8);

                // Reset everything and restore.
                store.SetVoxelData(Vector3Int.zero, CreateSolidVoxels());
                TerrainDeformer.ResetJournal();

                int restored = WorldSaveService.LoadTerrain("terrain_slot", store);
                Assert.That(restored, Is.GreaterThan(0));

                store.TryGetEntry(Vector3Int.zero, out VoxelChunkEntry restoredEntry);
                Assert.That(store.GetDensity(restoredEntry, 8, 8, 8), Is.EqualTo(dugBefore).Within(1e-5f));
            }
            finally
            {
                WorldSaveService.DirectoryProvider = previousProvider;
                if (System.IO.Directory.Exists(tempDirectory))
                    System.IO.Directory.Delete(tempDirectory, true);
                TerrainDeformer.ResetJournal();
                RuntimePlacementService.Reset();
            }
        }

        private static VoxelData CreateSolidVoxels()
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
