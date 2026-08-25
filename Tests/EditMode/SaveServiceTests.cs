using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Editing;
using WorldBuilder.Runtime.Saves;

namespace WorldBuilder.Tests
{
    public sealed class SaveServiceTests
    {
        private string tempDirectory;
        private Func<string> previousProvider;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "wb_save_tests_" + Guid.NewGuid().ToString("N"));
            previousProvider = WorldSaveService.DirectoryProvider;
            WorldSaveService.DirectoryProvider = () => tempDirectory;
        }

        [TearDown]
        public void TearDown()
        {
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
