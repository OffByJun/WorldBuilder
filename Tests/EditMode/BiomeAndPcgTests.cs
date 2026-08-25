using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class BiomeAndPcgTests
    {
        // ---- BiomeClassifier ----

        [Test]
        public void Classifier_MapsElevationBands()
        {
            Assert.That(BiomeClassifier.FromClimate(-70f, 0.5f, 0.5f, 0f), Is.EqualTo(BiomeType.AbyssalTrench));
            Assert.That(BiomeClassifier.FromClimate(-30f, 0.5f, 0.5f, 0f), Is.EqualTo(BiomeType.DeepSea));
            Assert.That(BiomeClassifier.FromClimate(-12f, 0.5f, 0.5f, 0f), Is.EqualTo(BiomeType.CoralReef));
            Assert.That(BiomeClassifier.FromClimate(-5f, 0.5f, 0.5f, 0f), Is.EqualTo(BiomeType.KelpForest));
            Assert.That(BiomeClassifier.FromClimate(-1f, 0.5f, 0.5f, 0f), Is.EqualTo(BiomeType.Ocean));
            Assert.That(BiomeClassifier.FromClimate(1f, 0.9f, 0.2f, 0f), Is.EqualTo(BiomeType.Beach));
        }

        [Test]
        public void Classifier_EnclosedPointsAreCaves()
        {
            Assert.That(BiomeClassifier.FromEnvironment(10f, 0.5f, 0.9f, 0f, true),
                Is.EqualTo(BiomeType.Cave));
            Assert.That(BiomeClassifier.FromEnvironment(10f, 0.5f, 0.9f, 0f, false),
                Is.EqualTo(BiomeType.Forest));
            Assert.That(BiomeClassifier.FromEnvironment(-30f, 0.5f, 0.5f, 0f, true),
                Is.EqualTo(BiomeType.Cave), "a flooded grotto is still a cave for biome purposes");
        }

        [Test]
        public void HighResBiomeMap_SampleBiome_RoundTrips()
        {
            HighResBiomeMap map = ScriptableObject.CreateInstance<HighResBiomeMap>();
            var keys = new List<Vector3Int> { new Vector3Int(0, 0, 0) };
            var ids = new List<byte[]>
            {
                new byte[]
                {
                    (byte)BiomeType.Forest, (byte)BiomeType.Rocky,
                    (byte)BiomeType.CoralReef, (byte)BiomeType.AbyssalTrench
                }
            };
            map.Configure(2, keys, ids);

            // Cell (0,0) of chunk (0,0): world [0..64]² at cellsPerChunk=2, chunkSize=128.
            float cellWorld = 128f / 2 * 0.25f;
            Assert.That(map.SampleBiome(cellWorld, cellWorld, 128f), Is.EqualTo(BiomeType.Forest));

            float farCellX = 128f / 2 * 1.75f;
            Assert.That(map.SampleBiome(farCellX, cellWorld, 128f), Is.EqualTo(BiomeType.Rocky));

            // New underwater biomes survive the byte round-trip without clamping.
            float secondRowZ = 128f / 2 * 1.25f;
            Assert.That(map.SampleBiome(cellWorld, secondRowZ, 128f), Is.EqualTo(BiomeType.CoralReef));
            Assert.That(map.SampleBiome(farCellX, secondRowZ, 128f), Is.EqualTo(BiomeType.AbyssalTrench));
        }

        [Test]
        public void DebugColor_DistinguishesAllBiomes()
        {
            var colors = new List<Color>();
            foreach (BiomeType biome in System.Enum.GetValues(typeof(BiomeType)))
            {
                Color color = BiomeClassifier.DebugColor(biome);
                Assert.That(color, Is.Not.EqualTo(default(Color)), $"no debug color for {biome}");
                Assert.That(colors, Has.No.Member(color), $"duplicate debug color for {biome}");
                colors.Add(color);
            }
            Assert.That(colors.Count, Is.EqualTo(BiomeClassifier.LastBiomeId + 1));
        }

        // ---- PCG scatter engine ----

        private sealed class FlatQuery : ITerrainQuery
        {
            public bool TryHeight(Vector2 worldXz, out float height)
            {
                height = 10f;
                return true;
            }

            public float Slope(Vector2 worldXz) => 0f;

            public BiomeType BiomeAt(Vector2 worldXz) => BiomeType.Forest;
        }

        private sealed class SteepQuery : ITerrainQuery
        {
            public bool TryHeight(Vector2 worldXz, out float height)
            {
                height = 10f;
                return true;
            }
            public float Slope(Vector2 worldXz) => 80f;
            public BiomeType BiomeAt(Vector2 worldXz) => BiomeType.Forest;
        }

        [Test]
        public void PcgEngine_RespectsSlopeGate()
        {
            ScatterRuleSet ruleSet = ScriptableObject.CreateInstance<ScatterRuleSet>();
            ruleSet.rules.Add(new ScatterRuleSet.Rule
            {
                name = "trees",
                prefabs = new List<GameObject> { new GameObject("Tree") },
                densityPerSquareMeter = 0.01f,
                maxSlopeDegrees = 35f
            });

            var flat = PcgScatterEngine.Generate(ruleSet, new FlatQuery(), Rect.MinMaxRect(0f, 0f, 200f, 200f), 5);
            var steep = PcgScatterEngine.Generate(ruleSet, new SteepQuery(), Rect.MinMaxRect(0f, 0f, 200f, 200f), 5);

            Assert.That(flat.Count, Is.GreaterThan(0));
            Assert.That(steep.Count, Is.EqualTo(0));
        }

        [Test]
        public void PcgEngine_IsDeterministic()
        {
            ScatterRuleSet ruleSet = ScriptableObject.CreateInstance<ScatterRuleSet>();
            ruleSet.rules.Add(new ScatterRuleSet.Rule
            {
                prefabs = new List<GameObject> { new GameObject("Rock") },
                densityPerSquareMeter = 0.02f
            });

            var bounds = Rect.MinMaxRect(-100f, -100f, 100f, 100f);
            var a = PcgScatterEngine.Generate(ruleSet, new FlatQuery(), bounds, 123);
            var b = PcgScatterEngine.Generate(ruleSet, new FlatQuery(), bounds, 123);

            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].Position, Is.EqualTo(b[i].Position));
                Assert.That(a[i].Rotation, Is.EqualTo(b[i].Rotation));
            }
        }
    }
}
