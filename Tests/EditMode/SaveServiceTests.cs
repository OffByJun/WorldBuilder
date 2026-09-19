using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Editing;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class SaveServiceTests
    {
        private string tempDirectory;
        private Func<string> previousProvider;
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "wb_save_tests_" + Guid.NewGuid().ToString("N"));
            previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;
            RuntimePlacementService.Reset();
            TerrainDeformer.ResetJournal();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimePlacementService.Reset();
            TerrainDeformer.ResetJournal();
            foreach (UnityEngine.Object instance in objects)
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
            WorldSaveService.DirectoryProvider = previousProvider;
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        private const string PlacementsJson =
            "{\"placements\":[{\"prefabId\":\"Hut\",\"px\":1.0,\"py\":2.0,\"pz\":3.0," +
            "\"rx\":0.0,\"ry\":0.7071,\"rz\":0.0,\"rw\":0.7071,\"scale\":1.0}]}";

        [Test]
        public void Save_CreatesFileAndListReportsIt()
        {
            WorldSaveService.Save("slot_a", PlacementsJson);

            Assert.That(WorldSaveService.Exists("slot_a"), Is.True);
            List<WorldSaveService.SaveInfo> slots = WorldSaveService.List();
            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0].Slot, Is.EqualTo("slot_a"));
            Assert.That(slots[0].PlacementCount, Is.EqualTo(1));
        }

        [Test]
        public void Load_RestoresThroughRuntimePlacementService()
        {
            WorldSaveService.Save("slot_b", PlacementsJson);

            bool loaded = WorldSaveService.Load("slot_b", id => id == "Hut" ? new GameObject("Hut") : null);
            try
            {
                Assert.That(loaded, Is.True);
                Assert.That(RuntimePlacementService.Records.Count, Is.EqualTo(1));
            }
            finally
            {
                RuntimePlacementService.Reset();
            }
        }

        [Test]
        public void Load_MissingSlot_ReturnsFalseWithoutThrowing()
        {
            Assert.That(WorldSaveService.Load("does_not_exist", _ => null), Is.False);
        }

        [Test]
        public void Delete_RemovesSlot()
        {
            WorldSaveService.Save("slot_c", PlacementsJson);
            Assert.That(WorldSaveService.Delete("slot_c"), Is.True);
            Assert.That(WorldSaveService.Exists("slot_c"), Is.False);
        }

        private GameObject CreateObject(string name)
        {
            var instance = new GameObject(name);
            objects.Add(instance);
            return instance;
        }

        private VoxelStoreAsset CreateStore()
        {
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            objects.Add(store);
            return store;
        }

        private static void SetDensity(VoxelStoreAsset store, Vector3Int coord, float density)
        {
            var voxels = new VoxelData(2, 2, 2);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++) voxels.SetDensity(x, y, z, density);
            store.SetVoxelData(coord, voxels);
        }

        [TestCase("copy")]
        [TestCase("original")]
        public void Manager_RestoredTerrainSurvivesSaveAsAndResave(string target)
        {
            VoxelStoreAsset store = CreateStore();
            SetDensity(store, Vector3Int.zero, 0.25f);
            WorldSaveService.SaveSnapshot("original", store, new[] { Vector3Int.zero }, "{}");
            SetDensity(store, Vector3Int.zero, 1f);
            var manager = CreateObject("manager").AddComponent<WorldSaveManager>();
            manager.TerrainStore = store;
            int notifications = 0;
            manager.TerrainChunkRestored += coord =>
            {
                Assert.That(TerrainDeformer.EditedChunks, Does.Contain(coord));
                notifications++;
            };

            Assert.That(manager.LoadFromSlot("original"), Is.True);
            Assert.That(notifications, Is.EqualTo(1));
            manager.SaveToSlot(target);
            SetDensity(store, Vector3Int.right, 0.75f);
            WorldSaveService.SaveTerrain("new_edit", store, new[] { Vector3Int.right });
            WorldSaveService.LoadTerrain("new_edit", store);
            manager.SaveToSlot(target);

            VoxelStoreAsset restored = CreateStore();
            Assert.That(WorldSaveService.LoadTerrain(target, restored), Is.EqualTo(2));
            Assert.That(restored.TryGetVoxelData(Vector3Int.zero, out VoxelData original), Is.True);
            Assert.That(original.GetDensity(0, 0, 0), Is.EqualTo(0.25f));
            Assert.That(restored.TryGetVoxelData(Vector3Int.right, out VoxelData added), Is.True);
            Assert.That(added.GetDensity(0, 0, 0), Is.EqualTo(0.75f));
        }

        [TestCase("missing")]
        [TestCase("snapshot_terrain")]
        [TestCase("snapshot_extras")]
        [TestCase("invalid")]
        public void InvalidLoads_PreservePlacementsTerrainAndJournal(string slot)
        {
            VoxelStoreAsset store = CreateStore();
            SetDensity(store, Vector3Int.zero, 0.25f);
            WorldSaveService.SaveSnapshot("snapshot", store, new[] { Vector3Int.zero }, "{}", "{}");
            WorldSaveService.LoadTerrain("snapshot", store);
            SetDensity(store, Vector3Int.zero, 0.75f);
            File.WriteAllText(Path.Combine(tempDirectory, "invalid.json"), "{}");
            WorldSaveService.SaveTerrain(slot, store, new[] { Vector3Int.zero });
            var record = RuntimePlacementService.Place(CreateObject("Hut"), Vector3.one, Quaternion.identity);
            var manager = CreateObject("manager").AddComponent<WorldSaveManager>();
            manager.TerrainStore = store;
            int notifications = 0;
            manager.Loaded += _ => notifications++;
            manager.TerrainChunkRestored += _ => notifications++;

            Assert.That(WorldSaveService.Load(slot, _ => null), Is.False);
            Assert.That(WorldSaveService.LoadSnapshot(slot, store, _ => null, out string extras), Is.False);
            Assert.That(extras, Is.Null);
            Assert.That(manager.LoadFromSlot(slot), Is.False);
            Assert.That(notifications, Is.Zero);
            Assert.That(RuntimePlacementService.Records[record.PlacementId], Is.SameAs(record));
            Assert.That(record.Instance != null, Is.True);
            Assert.That(TerrainDeformer.EditedChunks, Is.EquivalentTo(new[] { Vector3Int.zero }));
            Assert.That(store.TryGetVoxelData(Vector3Int.zero, out VoxelData current), Is.True);
            Assert.That(current.GetDensity(0, 0, 0), Is.EqualTo(0.75f));
            Assert.That(WorldSaveService.List().ConvertAll(info => info.Slot), Is.EqualTo(new[] { "snapshot" }));
        }

        [TestCase("not json")]
        [TestCase("{\"unrelated\":1}")]
        [TestCase("{\"placements\":null}")]
        [TestCase("{\"placements\":[{}]}")]
        [TestCase("{\"nested\":{\"placements\":[]}}")]
        [TestCase("{\"placements\":[],\"placements\":null}")]
        [TestCase("{\"text\":\"\\\"placements\\\":[]\"}")]
        public void InvalidPlacementPayload_DoesNotClearExistingPlacements(string payload)
        {
            WorldSaveService.Save("invalid", payload);
            var record = RuntimePlacementService.Place(CreateObject("Hut"), Vector3.zero, Quaternion.identity);
            Assert.That(WorldSaveService.Load("invalid", _ => null), Is.False);
            Assert.That(WorldSaveService.List(), Is.Empty);
            Assert.That(RuntimePlacementService.Records[record.PlacementId], Is.SameAs(record));
            Assert.That(record.Instance != null, Is.True);
        }

        [Test]
        public void CorruptTerrain_LoadFailsBeforeChangingWorld()
        {
            VoxelStoreAsset store = CreateStore();
            SetDensity(store, Vector3Int.zero, 0.25f);
            SetDensity(store, Vector3Int.right, 0.5f);
            WorldSaveService.SaveSnapshot("corrupt", store, new[] { Vector3Int.zero, Vector3Int.right }, "{}");
            string path = Path.Combine(tempDirectory, "corrupt_terrain.json");
            string json = File.ReadAllText(path);
            int lastValue = json.LastIndexOf('"');
            int startValue = json.LastIndexOf('"', lastValue - 1);
            File.WriteAllText(path, json.Substring(0, startValue + 1) + "invalid base64" + json.Substring(lastValue));
            SetDensity(store, Vector3Int.zero, 1f);
            var record = RuntimePlacementService.Place(CreateObject("Hut"), Vector3.zero, Quaternion.identity);
            var manager = CreateObject("manager").AddComponent<WorldSaveManager>();
            manager.TerrainStore = store;
            Assert.That(manager.LoadFromSlot("corrupt"), Is.False);
            Assert.That(WorldSaveService.LoadTerrain("corrupt", store), Is.EqualTo(-1));
            Assert.That(RuntimePlacementService.Records[record.PlacementId], Is.SameAs(record));
            Assert.That(TerrainDeformer.EditedChunks, Is.Empty);
            store.TryGetVoxelData(Vector3Int.zero, out VoxelData current);
            Assert.That(current.GetDensity(0, 0, 0), Is.EqualTo(1f));
        }

        [Test]
        public void EmptyAndLegacyPlacementOnlySaves_RemainLoadable()
        {
            WorldSaveService.Save("empty", "{}");
            Assert.That(WorldSaveService.LoadSnapshot("empty", null, _ => null, out _), Is.True);
            File.WriteAllText(Path.Combine(tempDirectory, "legacy.json"), "{\"placementsJson\":\"{\\\"placements\\\":[]}\"}");
            Assert.That(WorldSaveService.LoadSnapshot("legacy", CreateStore(), _ => null, out _), Is.True);
            Assert.That(WorldSaveService.List().Count, Is.EqualTo(2));
        }

        private void SeedAutosaves(string pattern)
        {
            VoxelStoreAsset store = CreateStore();
            SetDensity(store, Vector3Int.zero, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                string slot = $"{pattern}_{i:00}";
                WorldSaveService.SaveSnapshot(slot, store, new[] { Vector3Int.zero }, "{}", i.ToString());
                string path = Path.Combine(tempDirectory, slot + ".json");
                string json = File.ReadAllText(path);
                int start = json.IndexOf("\"timestampUtc\":\"", StringComparison.Ordinal) + "\"timestampUtc\":\"".Length;
                int end = json.IndexOf('"', start);
                File.WriteAllText(path, json.Substring(0, start) + $"2020-01-0{i + 1}T00:00:00.0000000Z" + json.Substring(end));
            }
        }

        [TestCase("autosave")]
        [TestCase("checkpoint")]
        public void Autosave_RotatesOldestOnlyAfterSavingAndReusesHoles(string pattern)
        {
            SeedAutosaves(pattern);
            WorldSaveService.Save(pattern + "_manual", "{}");
            var autosave = CreateObject("autosave").AddComponent<AutoSaveService>();
            JsonUtility.FromJsonOverwrite("{\"slotPattern\":\"" + pattern + "\"}", autosave);
            autosave.Bind(() => null, () => null, () => "{}", _ => null);
            Assert.That(autosave.TickNow(), Is.EqualTo(pattern + "_03"));
            Assert.That(WorldSaveService.Exists(pattern + "_00"), Is.False);
            Assert.That(File.Exists(Path.Combine(tempDirectory, pattern + "_00_terrain.json")), Is.False);
            Assert.That(WorldSaveService.Exists(pattern + "_01"), Is.True);
            Assert.That(WorldSaveService.Exists(pattern + "_02"), Is.True);
            Assert.That(autosave.TickNow(), Is.EqualTo(pattern + "_00"));
            Assert.That(WorldSaveService.Exists(pattern + "_01"), Is.False);
            Assert.That(WorldSaveService.Exists(pattern + "_02"), Is.True);
            Assert.That(WorldSaveService.Exists(pattern + "_03"), Is.True);
            Assert.That(WorldSaveService.Exists(pattern + "_manual"), Is.True);
            Assert.That(WorldSaveService.List().Count, Is.EqualTo(4));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Autosave_FailurePreservesAllBackupsAndLastSavedSlot(bool failAfterMain)
        {
            var autosave = CreateObject("autosave").AddComponent<AutoSaveService>();
            autosave.Bind(() => null, () => null, () => "{}", _ => null);
            Assert.That(autosave.TickNow(), Is.EqualTo("autosave_00"));
            SeedAutosaves("autosave");
            var before = new Dictionary<string, string>();
            foreach (string path in Directory.GetFiles(tempDirectory)) before.Add(path, File.ReadAllText(path));
            int notifications = 0;
            autosave.AutoSaved += _ => notifications++;
            VoxelStoreAsset store = CreateStore();
            autosave.Bind(() => store, () => FailingChunks(),
                () => failAfterMain ? "{}" : throw new IOException("provider failed"), _ => null);
            Assert.Throws<IOException>(() => autosave.TickNow());
            Assert.That(autosave.LastSavedSlot, Is.EqualTo("autosave_00"));
            Assert.That(notifications, Is.Zero);
            Assert.That(Directory.GetFiles(tempDirectory), Is.EquivalentTo(before.Keys));
            foreach (var file in before) Assert.That(File.ReadAllText(file.Key), Is.EqualTo(file.Value));
        }

        private static IEnumerable<Vector3Int> FailingChunks()
        {
            yield return Vector3Int.zero;
            throw new IOException("terrain serialization failed");
        }

        [Test]
        [Platform("Win")]
        public void AtomicOverwrite_FailurePreservesOriginalAndCleansTemporaryFile()
        {
            WorldSaveService.Save("atomic", "{}");
            string path = Path.Combine(tempDirectory, "atomic.json");
            string before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => WorldSaveService.Save("atomic", PlacementsJson));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(tempDirectory, "*.tmp"), Is.Empty);
            WorldSaveService.Save("atomic", PlacementsJson);
            Assert.That(WorldSaveService.List()[0].PlacementCount, Is.EqualTo(1));
        }

        [Test]
        public void SlotNames_AreSanitized()
        {
            WorldSaveService.Save("../evil", "{}");

            // "../evil" and "___evil" normalize to the same slot; no traversal escape happens.
            Assert.That(WorldSaveService.Exists("___evil"), Is.True);
            Assert.That(Directory.GetFiles(tempDirectory).Length, Is.EqualTo(1));
            Assert.That(Path.GetFileNameWithoutExtension(Directory.GetFiles(tempDirectory)[0]),
                Is.EqualTo("___evil"));
        }
    }
}
