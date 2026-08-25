using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Editing;

namespace WorldBuilder.Tests
{
    public sealed class RuntimeEditingTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimePlacementService.Reset();
            CleanupRoot();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimePlacementService.Reset();
            CleanupRoot();
        }

        private static void CleanupRoot()
        {
            GameObject root = GameObject.Find("__WorldBuilder_RuntimeEdits");
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void Place_CreatesRecordWithPrefabIdentity()
        {
            GameObject prefab = new GameObject("Rock");
            try
            {
                RuntimePlacementService.PlacementRecord record =
                    RuntimePlacementService.Place(prefab, new Vector3(1f, 2f, 3f), Quaternion.identity, 1f);

                Assert.That(record, Is.Not.Null);
                Assert.That(record.PrefabId, Is.EqualTo("Rock"));
                Assert.That(record.Instance, Is.Not.Null);
                Assert.That(record.Instance.transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(RuntimePlacementService.Records.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Place_AppliesUniformScale()
        {
            GameObject prefab = new GameObject("Tree");
            try
            {
                RuntimePlacementService.PlacementRecord record =
                    RuntimePlacementService.Place(prefab, Vector3.zero, Quaternion.identity, 2.5f);
                Assert.That(record.Instance.transform.localScale, Is.EqualTo(Vector3.one * 2.5f));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RemoveNearest_RemovesOnlyWithinRadius()
        {
            GameObject prefab = new GameObject("Crate");
            try
            {
                RuntimePlacementService.Place(prefab, new Vector3(0f, 0f, 0f), Quaternion.identity);
                RuntimePlacementService.PlacementRecord far =
                    RuntimePlacementService.Place(prefab, new Vector3(50f, 0f, 0f), Quaternion.identity);

                bool removed = RuntimePlacementService.RemoveNearest(new Vector3(1f, 0f, 0f), 2f,
                    out RuntimePlacementService.PlacementRecord removedRecord);

                Assert.That(removed, Is.True);
                Assert.That(removedRecord.PrefabId, Is.EqualTo("Crate"));
                Assert.That(removedRecord.PlacementId, Is.Not.EqualTo(far.PlacementId));
                Assert.That(RuntimePlacementService.Records.ContainsKey(far.PlacementId), Is.True);
                Assert.That(RuntimePlacementService.Records.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RemoveNearest_ReturnsFalseWhenNothingNearby()
        {
            GameObject prefab = new GameObject("Crate");
            try
            {
                RuntimePlacementService.Place(prefab, new Vector3(30f, 0f, 30f), Quaternion.identity);
                bool removed = RuntimePlacementService.RemoveNearest(Vector3.zero, 1f, out _);
                Assert.That(removed, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Reset_ClearsAllRecordsAndInstances()
        {
            GameObject prefab = new GameObject("Wall");
            try
            {
                RuntimePlacementService.PlacementRecord record =
                    RuntimePlacementService.Place(prefab, Vector3.zero, Quaternion.identity);
                RuntimePlacementService.Reset();

                Assert.That(RuntimePlacementService.Records.Count, Is.EqualTo(0));
                Assert.That(record.Instance == null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void TryGetInstanceRecord_FindsOwnerThroughHierarchy()
        {
            GameObject prefab = new GameObject("Turret");
            try
            {
                RuntimePlacementService.PlacementRecord record =
                    RuntimePlacementService.Place(prefab, Vector3.zero, Quaternion.identity);

                Transform child = record.Instance.transform;
                GameObject probe = new GameObject("Probe");
                probe.transform.SetParent(child, false);

                bool found = RuntimePlacementService.TryGetInstanceRecord(probe,
                    out RuntimePlacementService.PlacementRecord owner);

                Assert.That(found, Is.True);
                Assert.That(owner.PlacementId, Is.EqualTo(record.PlacementId));

                Object.DestroyImmediate(probe);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void JsonRoundTrip_RestoresPositionRotationScale()
        {
            GameObject prefab = new GameObject("Hut");
            try
            {
                Quaternion rotation = Quaternion.Euler(0f, 45f, 0f);
                RuntimePlacementService.Place(prefab, new Vector3(1f, 2f, 3f), rotation, 2f);
                string json = RuntimePlacementService.ToJson();

                RuntimePlacementService.Reset();
                int restored = RuntimePlacementService.RestoreFromJson(json, id => id == "Hut" ? prefab : null);

                Assert.That(restored, Is.EqualTo(1));
                foreach (System.Collections.Generic.KeyValuePair<int, RuntimePlacementService.PlacementRecord> pair
                    in RuntimePlacementService.Records)
                {
                    RuntimePlacementService.PlacementRecord record = pair.Value;
                    Assert.That(record.PrefabId, Is.EqualTo("Hut"));
                    Assert.That(record.Instance.transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                    Assert.That(record.Instance.transform.rotation, Is.EqualTo(rotation).Within(1e-5f));
                    Assert.That(record.Instance.transform.localScale.x, Is.EqualTo(2f).Within(1e-5f));
                }
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RestoreFromJson_SkipsUnknownPrefabIds()
        {
            string json =
                "{\"placements\":[{\"prefabId\":\"Known\",\"px\":1.0,\"py\":0.0,\"pz\":0.0," +
                "\"rx\":0.0,\"ry\":0.0,\"rz\":0.0,\"rw\":1.0,\"scale\":1.0}," +
                "{\"prefabId\":\"Unknown\",\"px\":9.0,\"py\":0.0,\"pz\":9.0," +
                "\"rx\":0.0,\"ry\":0.0,\"rz\":0.0,\"rw\":1.0,\"scale\":1.0}]}";

            GameObject prefab = new GameObject("Known");
            try
            {
                RuntimePlacementService.Reset();
                int restored = RuntimePlacementService.RestoreFromJson(json, id => id == "Known" ? prefab : null);
                Assert.That(restored, Is.EqualTo(1));
                Assert.That(RuntimePlacementService.Records.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void WorldDataRuntimeLoader_InstantiatesBoundPrefabsAndRaisesEvents()
        {
            WorldBuilder.Runtime.Data.WorldDataSnapshot snapshot =
                ScriptableObject.CreateInstance<WorldBuilder.Runtime.Data.WorldDataSnapshot>();
            snapshot.Configure(new[]
            {
                new WorldBuilder.Runtime.Data.WorldDataRecord("POI", "a", "Ruins", new Vector3(5f, 0f, 5f)),
                new WorldBuilder.Runtime.Data.WorldDataRecord("LootContainer", "b", "Chest", new Vector3(6f, 0f, 6f))
            });

            GameObject boundPrefab = new GameObject("POIMarker");
            GameObject loaderObject = new GameObject("Loader");
            try
            {
                WorldBuilder.Runtime.Data.WorldDataRuntimeLoader loader =
                    loaderObject.AddComponent<WorldBuilder.Runtime.Data.WorldDataRuntimeLoader>();
                loader.Snapshot = snapshot;
                loader.AddKindBinding("POI", boundPrefab);

                var counts = new System.Collections.Generic.List<string>();
                loader.RecordLoaded += record => counts.Add(record.kind);

                loader.Load();

                Assert.That(loader.HasLoaded, Is.True);
                Assert.That(loader.LoadedCount, Is.EqualTo(2));
                Assert.That(counts.Count, Is.EqualTo(2));

                // One bound prefab (kind POI) must have been instantiated under the loader.
                int markerCount = 0;
                foreach (Transform child in loaderObject.transform)
                {
                    if (child.name == "Ruins") markerCount++;
                }
                Assert.That(markerCount, Is.EqualTo(1));

                // Loading twice is a no-op.
                loader.Load();
                Assert.That(loader.LoadedCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(boundPrefab);
                Object.DestroyImmediate(snapshot);
                Object.DestroyImmediate(loaderObject);
            }
        }
    }
}
