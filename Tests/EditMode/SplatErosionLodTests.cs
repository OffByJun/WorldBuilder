using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class SplatErosionLodTests
    {
        // ---- SplatBaker ----

        [Test]
        public void SplatBaker_AllRockyChunk_WeightsConcentrateOnRockLayer()
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

            Color32[] splat = SplatBaker.Bake(map, new Vector3Int(0, 0, 0), 32, 16f,
                SplatBaker.DefaultMapping());

            Assert.That(splat.Length, Is.EqualTo(32 * 32));

            // Interior texels: rock maps to layer 2 (blue channel).
            for (int ty = 2; ty < 30; ty++)
            {
                for (int tx = 2; tx < 30; tx++)
                {
                    Color32 pixel = splat[ty * 32 + tx];
                    Assert.That(pixel.b, Is.GreaterThan(220), $"interior pixel {pixel} at {tx},{ty}");
                }
            }

            // Channels sum to ~255 everywhere (border texels blend with unmapped fallback).
            foreach (Color32 pixel in splat)
            {
                int total = pixel.r + pixel.g + pixel.b + pixel.a;
                Assert.That(total, Is.EqualTo(255).Within(2));
            }
        }

        [Test]
        public void SplatBaker_BiomeSwitch_BlendsAcrossBorder()
        {
            HighResBiomeMap map = ScriptableObject.CreateInstance<HighResBiomeMap>();
            map.Configure(1,
                new System.Collections.Generic.List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) },
                new System.Collections.Generic.List<byte[]>
                {
                    new byte[] { (byte)BiomeType.Forest },
                    new byte[] { (byte)BiomeType.Beach }
                });

            Color32[] splat = SplatBaker.Bake(map, new Vector3Int(0, 0, 0), 16, 16f,
                SplatBaker.DefaultMapping());

            bool hasForestDominant = false;
            bool hasBeachWeight = false;   // beach bleeds into chunk 0's border texels
            foreach (Color32 p in splat)
            {
                if (p.g > 200) hasForestDominant = true;      // grass layer
                if (p.r > 8) hasBeachWeight = true;           // some sand weight present
            }
            Assert.That(hasForestDominant, Is.True);
            Assert.That(hasBeachWeight, Is.True);
        }

        // ---- Erosion map ----

        [Test]
        public void Erosion_ProducesNetZeroishDeltaMapWithRealMovement()
        {
            TerrainShapeParams p = ScriptableObject.CreateInstance<TerrainShapeParams>();
            p.seed = 11;
            p.baseHeight = 20f;
            p.heightAmplitude = 35f;
            p.featureScale = 150f;

            TerrainField.HeightMap a = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 65, 4f);
            TerrainField.HeightMap b = TerrainField.BuildHeightMap(p, new Vector2(0f, 0f), 65, 4f);

            ErosionSimulator.Apply(a, new ErosionSimulator.Params { DropletCount = 2500 }, 77, out float[] delta);

            float min = float.MaxValue;
            float max = float.MinValue;
            int moved = 0;
            for (int i = 0; i < delta.Length; i++)
            {
                min = Mathf.Min(min, delta[i]);
                max = Mathf.Max(max, delta[i]);
                if (Mathf.Abs(delta[i]) > 0.001f) moved++;
            }

            Assert.That(moved, Is.GreaterThan(delta.Length / 100), "erosion must move material somewhere");
            Assert.That(Mathf.Abs(min), Is.LessThan(40f));
            Assert.That(max, Is.LessThan(40f));

            // Determinism: same seed → identical map.
            ErosionSimulator.Apply(b, new ErosionSimulator.Params { DropletCount = 2500 }, 77, out float[] delta2);
            CollectionAssert.AreEqual(delta, delta2);
        }

        // ---- LOD simplifier with colors ----

        [Test]
        public void LodSimplifier_ReducesVerticesAndKeepsColors()
        {
            VoxelStoreAsset store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            const int resolution = 16;
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                    store.SetDensity(entry, x, y, z, y <= 7 ? 1f : 0f);
            }

            var sampler = new VoxelWorldSampler(store, 16f);
            SurfaceNetsMesher.Result full =
                SurfaceNetsMesher.Mesh(sampler, new Vector3Int(0, 0, 0), resolution, 16f,
                    v => Color.red);
            Assert.That(full.Mesh, Is.Not.Null);

            Mesh lod = WorldBuilder.Editor.LODGeneratorTool.LODMeshSimplifier.Simplify(
                full.Mesh, 0.25f, "lod_test");

            Assert.That(lod, Is.Not.Null);
            Assert.That(lod.vertexCount, Is.LessThan(full.Mesh.vertexCount));
            Assert.That(lod.colors.Length, Is.EqualTo(lod.vertexCount));
        }
    }
}
