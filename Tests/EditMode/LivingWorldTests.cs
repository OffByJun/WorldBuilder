using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Gameplay;

namespace WorldBuilder.Tests
{
    public sealed class LivingWorldTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        private GameObject BuildGrowable(int stageCount, bool startMature = false)
        {
            var go = new GameObject("growable");
            objects.Add(go);
            var growable = go.AddComponent<GrowableResource>();

            var stagesField = typeof(GrowableResource).GetField("stages",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var visuals = new List<GameObject>();
            for (int i = 0; i < stageCount; i++)
            {
                var child = new GameObject($"stage_{i}");
                child.transform.SetParent(go.transform, false);
                objects.Add(child);
                visuals.Add(child);
            }
            stagesField.SetValue(growable, visuals);

            var secondsField = typeof(GrowableResource).GetField("secondsPerStage",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            secondsField.SetValue(growable, 10f);

            var matureField = typeof(GrowableResource).GetField("startMature",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            matureField.SetValue(growable, startMature);

            return go;
        }

        [Test]
        public void Growable_AdvancesThroughStagesOverTime()
        {
            BuildGrowable(stageCount: 3);
            var growable = objects[0].GetComponent<GrowableResource>();

            Assert.That(growable.CurrentStage, Is.EqualTo(0), "starts as sprout");

            int advanced = 0;
            for (int i = 0; i < 25; i++)
                if (growable.Advance(5f)) advanced++; // 10 s per stage → 2 advances in 125 s

            Assert.That(advanced, Is.EqualTo(2));
            Assert.That(growable.CurrentStage, Is.EqualTo(2));
            Assert.That(growable.Growth01, Is.EqualTo(1f).Within(0.001f), "fully grown caps");
        }

        [Test]
        public void Harvestable_OnlyYieldsWhenMatureAndRespawnsThroughStageZero()
        {
            BuildGrowable(stageCount: 3);
            GameObject go = objects[0];
            var growable = go.GetComponent<GrowableResource>();
            var node = go.AddComponent<HarvestableNode>();

            // Immature: refused.
            Assert.That(node.TryHarvest(out _), Is.False);

            // Mature it fully.
            while (growable.CurrentStage < growable.StageCount - 1) growable.Advance(11f);
            Assert.That(node.ReadyForHarvest, Is.True);

            Assert.That(node.TryHarvest(out List<GrowableResource.ItemYield> rolled), Is.True);
            Assert.That(rolled, Is.Not.Empty);
            foreach (var entry in rolled)
                Assert.That(entry.minAmount >= 0 && entry.maxAmount <= 3,
                    $"rolled amounts stay inside authored bounds ({entry.itemId})");

            // Respawn loop: node reset to sprout, refuses again until regrown.
            Assert.That(growable.CurrentStage, Is.EqualTo(0));
            Assert.That(node.TryHarvest(out _), Is.False);
        }

        [Test]
        public void Harvestable_DestroyModeRemovesNode()
        {
            BuildGrowable(stageCount: 1); // single-stage nodes are always "mature"
            GameObject go = objects[0];
            var node = go.AddComponent<HarvestableNode>();
            var destroyField = typeof(HarvestableNode).GetField("destroyOnHarvest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            destroyField.SetValue(node, true);

            Assert.That(node.TryHarvest(out _), Is.True);
            Assert.That(go == null, Is.True, "destroy-on-harvest removes the node");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                if (go != null) Object.DestroyImmediate(go);
            objects.Clear();
        }
    }
}
