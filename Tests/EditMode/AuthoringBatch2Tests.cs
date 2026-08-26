using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Creatures;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class AuthoringBatch2Tests
    {
        /// <summary>Solid [-1..1]³ chunk block with a hollow room at lattice [6..10]³.</summary>
        private static VoxelStoreAsset BuildSolidStoreWithRoom(out Vector3 roomCenter)
        {
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

            roomCenter = new Vector3(8f, 8f, 8f);
            return store;
        }

        // ---- VoxelVolumeScatter ----

        private ScatterRuleSet RuleSet(string name, float density)
        {
            var set = ScriptableObject.CreateInstance<ScatterRuleSet>();
            set.rules.Add(new ScatterRuleSet.Rule
            {
                name = name,
                prefabs = new List<GameObject> { new GameObject(name) },
                anyBiome = true,
                densityPerSquareMeter = density
            });
            return set;
        }

        [Test]
        public void VolumeScatter_PlacesOnlyOnCavityFloors()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var query = new VoxelVolumeQuery(new VoxelWorldSampler(store, 16f), 16f);
            ScatterRuleSet set = RuleSet("Ore", 0.5f);

            var volume = new Bounds(roomCenter, Vector3.one * 8f);
            List<PcgPlacement> placements = VoxelVolumeScatter.Generate(set, query, volume, seed: 11);

            Assert.That(placements.Count, Is.GreaterThan(0));
            foreach (PcgPlacement placement in placements)
            {
                Assert.That(placement.Position.y, Is.InRange(6f, 11f),
                    $"floor placement outside room at {placement.Position}");
                Assert.That(Vector3.Distance(placement.Position, roomCenter),
                    Is.LessThanOrEqualTo(6f), "placement escaped the requested volume");
            }
        }

        [Test]
        public void VolumeScatter_IsDeterministic()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var query = new VoxelVolumeQuery(new VoxelWorldSampler(store, 16f), 16f);
            ScatterRuleSet set = RuleSet("Moss", 0.4f);
            var volume = new Bounds(roomCenter, Vector3.one * 8f);

            List<PcgPlacement> first = VoxelVolumeScatter.Generate(set, query, volume, seed: 42);
            List<PcgPlacement> second = VoxelVolumeScatter.Generate(set, query, volume, seed: 42);

            Assert.That(first.Count, Is.EqualTo(second.Count).And.GreaterThan(0));
            for (int i = 0; i < first.Count; i++)
                Assert.That(first[i].Position, Is.EqualTo(second[i].Position));
        }

        [Test]
        public void ScatterFactory_ProducesGatedPresets()
        {
            foreach (ScatterRuleSetFactory.EcologyKind kind in
                     Enum.GetValues(typeof(ScatterRuleSetFactory.EcologyKind)))
            {
                ScatterRuleSet set =
                    ScatterRuleSetFactory.Create(kind, kind.ToString());
                Assert.That(set.rules.Count, Is.GreaterThan(0));

                bool interiorLike = kind == ScatterRuleSetFactory.EcologyKind.CaveInterior ||
                                    kind == ScatterRuleSetFactory.EcologyKind.FishSchools;
                if (interiorLike)
                    Assert.That(set.rules[0].biome, Is.EqualTo(BiomeType.Cave));
                else
                {
                    Assert.That(set.rules[0].useDepthGate, Is.True,
                        "underwater presets must gate by depth");
                    Assert.That(set.rules[0].maxFlowSpeed, Is.LessThan(999f),
                        "underwater presets must respect torrent zones");
                }
            }
        }

        // ---- MeshCarver ----

        private static Mesh BuildVerticalTubeMesh(float centerXZ, float radius, float height)
        {
            const int sides = 8;
            var vertices = new Vector3[sides * 2];
            for (int i = 0; i < sides; i++)
            {
                float angle = Mathf.PI * 2f * i / sides;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vertices[i] = new Vector3(centerXZ + offset.x, 0f, centerXZ + offset.y);
                vertices[sides + i] = new Vector3(centerXZ + offset.x, height, centerXZ + offset.y);
            }

            var faces = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                faces.Add(i); faces.Add(next); faces.Add(sides + next);
                faces.Add(i); faces.Add(sides + next); faces.Add(sides + i);
            }

            var mesh = new Mesh { vertices = vertices, triangles = faces.ToArray() };
            mesh.RecalculateNormals();
            return mesh;
        }

        [Test]
        public void MeshCarver_TunnelsThroughSolidSlab()
        {
            const int resolution = 16;
            const float chunkSize = 16f;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                    store.SetDensity(entry, x, y, z, y <= 7 ? 1f : 0f);
            }

            Mesh tube = BuildVerticalTubeMesh(8f, radius: 1.2f, height: 16f);
            int changed = MeshCarver.CarveAlongSurface(store, chunkSize, tube,
                Matrix4x4.identity, thickness: 2.4f, yRange: new Vector2(-32f, 32f));
            Assert.That(changed, Is.GreaterThan(0));
            UnityEngine.Object.DestroyImmediate(tube);

            var sampler = new VoxelWorldSampler(store, chunkSize);
            for (float y = 1f; y <= 7f; y += 0.75f)
            {
                float density = sampler.Sample(8f, y, 8f);
                Assert.That(density, Is.LessThan(SurfaceNetsMesher.IsoLevel),
                    $"tube blocked at y={y:F1} (density {density:F3})");
            }
            // Wall far from the tube stays solid.
            Assert.That(sampler.Sample(2f, 4f, 2f),
                Is.GreaterThanOrEqualTo(SurfaceNetsMesher.IsoLevel));
        }

        // ---- Mid-water (cavity) scatter ----

        [Test]
        public void VolumeScatter_PlacesFishInCavityCenters()
        {
            VoxelStoreAsset store = BuildSolidStoreWithRoom(out Vector3 roomCenter);
            var query = new VoxelVolumeQuery(new VoxelWorldSampler(store, 16f), 16f);

            ScatterRuleSet set = ScriptableObject.CreateInstance<ScatterRuleSet>();
            set.rules.Add(new ScatterRuleSet.Rule
            {
                name = "school",
                prefabs = new List<GameObject> { new GameObject("Fish") },
                densityPerSquareMeter = 0.5f
            });

            var volume = new Bounds(roomCenter, new Vector3(8f, 6f, 8f));
            List<PcgPlacement> placements = VoxelVolumeScatter.GenerateMidWater(set, query, volume, seed: 3);
            Assert.That(placements.Count, Is.GreaterThan(0));

            foreach (PcgPlacement placement in placements)
            {
                // Cavity spans lattice [6..11); centers must float inside the room.
                Assert.That(placement.Position.y, Is.InRange(6f, 11f),
                    $"mid-water placement at {placement.Position}");
                Assert.That(Vector3.Distance(placement.Position, roomCenter), Is.LessThanOrEqualTo(5f));
            }
        }

        [Test]
        public void ScatterFactory_FishSchoolsKindProducesRules()
        {
            ScatterRuleSet set = ScatterRuleSetFactory.Create(
                ScatterRuleSetFactory.EcologyKind.FishSchools, "test");
            Assert.That(set.rules.Count, Is.GreaterThan(0));
            Assert.That(set.rules[0].biome, Is.EqualTo(BiomeType.Cave));
        }

        // ---- SeasonPalette ----

        [Test]
        public void SeasonPalette_BlendsAdjacentSeasonsAndWraps()
        {
            SeasonPalette palette = ScriptableObject.CreateInstance<SeasonPalette>();
            palette.EnsureDefaults();
            Assert.That(palette.Count, Is.EqualTo(Enum.GetValues(typeof(BiomeType)).Length));

            Color summerStart = palette.Sample(BiomeType.Forest, 1f);
            Color summerEntry = palette.GetEntry(ForestIndex(palette)).summer;
            Assert.That(summerStart.r, Is.EqualTo(summerEntry.r).Within(1e-3f));

            // Midpoint between summer and autumn sits between both colors.
            float midR = Mathf.Lerp(palette.GetEntry(ForestIndex(palette)).summer.r,
                palette.GetEntry(ForestIndex(palette)).autumn.r, 0.5f);
            Color mid = palette.Sample(BiomeType.Forest, 1.5f);
            Assert.That(mid.r, Is.EqualTo(midR).Within(1e-3f));

            // Wrapping: season 4 lands back on spring.
            Color wrapped = palette.Sample(BiomeType.Forest, 4.25f);
            Color quarterPastSpring = palette.Sample(BiomeType.Forest, 0.25f);
            Assert.That(wrapped.r, Is.EqualTo(quarterPastSpring.r).Within(1e-4f));
        }

        private static int ForestIndex(SeasonPalette palette)
        {
            for (int i = 0; i < palette.Count; i++)
                if (palette.GetEntry(i).biome == BiomeType.Forest) return i;
            return -1;
        }

        // ---- CreatureWaypointPath ----

        [Test]
        public void WaypointPath_SquareLoopHasPlausibleLengthAndEvaluatesContinuously()
        {
            var go = new GameObject("path");
            var path = go.AddComponent<CreatureWaypointPath>();

            // Rebuild points as a 10 m square loop.
            var field = typeof(CreatureWaypointPath)
                .GetField("waypoints", System.Reflection.BindingFlags.NonPublic |
                                        System.Reflection.BindingFlags.Instance);
            var points = new List<CreatureWaypointPath.Waypoint>
            {
                new CreatureWaypointPath.Waypoint { localPosition = new Vector3(5f, 0f, 5f) },
                new CreatureWaypointPath.Waypoint { localPosition = new Vector3(-5f, 0f, 5f) },
                new CreatureWaypointPath.Waypoint { localPosition = new Vector3(-5f, 0f, -5f) },
                new CreatureWaypointPath.Waypoint { localPosition = new Vector3(5f, 0f, -5f) }
            };
            field.SetValue(path, points);

            float length = path.TotalLength();
            Assert.That(length, Is.InRange(38f, 52f), $"catmull square ≈ 40+ m, got {length}");

            Vector3 start = path.EvaluateAtDistance(0f);
            Assert.That((start - new Vector3(5f, 0f, 5f)).magnitude, Is.LessThan(0.01f));

            // Continuous evaluation: consecutive small steps never jump more than ~2 m.
            Vector3 previous = path.EvaluateAtDistance(0f);
            for (float d = 0.25f; d < length; d += 0.25f)
            {
                Vector3 current = path.EvaluateAtDistance(d);
                Assert.That((current - previous).magnitude, Is.LessThan(2f));
                previous = current;
            }

            // Closed loop wraps back to start.
            Vector3 wrapped = path.EvaluateAtDistance(length + 0.1f);
            Assert.That(Vector3.Distance(wrapped, start), Is.LessThan(1f));

            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
