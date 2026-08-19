using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Authoring.Water;
using WorldBuilder.Baking.Water;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    public sealed class WaterQueryTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldGridSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(128f, 4, 16f, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects) Object.DestroyImmediate(item);
            objects.Clear();
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void PriorityOverrides_OceanThenAirThenNestedWater()
        {
            OceanWaterBody ocean = Add<OceanWaterBody>("ocean", Vector3.zero);
            ocean.SeaLevel = 0f;
            AirOverrideVolume air = Add<AirOverrideVolume>("air", new Vector3(0f, -2f, 0f));
            air.Size = new Vector3(8f, 4f, 8f);
            air.Priority = 100;
            LocalWaterVolume nested = Add<LocalWaterVolume>("nested", new Vector3(0f, -2f, 0f));
            nested.Size = Vector3.one;
            nested.Priority = 200;

            WaterBakeResult bake = WaterBaker.Bake(new WaterBodyAuthoring[] { nested, ocean, air }, settings);
            Assert.That(bake.Report.HasErrors, Is.False);
            WaterQueryService query = new WaterQueryService(bake.Data);
            Assert.That(query.Sample(new Vector3(10f, -1f, 0f)).IsInWater, Is.True);
            Assert.That(query.Sample(new Vector3(2f, -1f, 0f)).FluidType, Is.EqualTo(FluidType.Air));
            Assert.That(query.Sample(new Vector3(0f, -2f, 0f)).IsInWater, Is.True);
            Object.DestroyImmediate(bake.Data);
        }

        [Test]
        public void RiverLakeAndBatch_UseCellIndexAndMatchSingleQueries()
        {
            RiverWaterBody river = Add<RiverWaterBody>("river", new Vector3(20f, 0f, 0f));
            LakeWaterBody lake = Add<LakeWaterBody>("lake", new Vector3(40f, 0f, 0f));
            WaterBakeResult bake = WaterBaker.Bake(new WaterBodyAuthoring[] { lake, river }, settings);
            Assert.That(bake.Report.HasErrors, Is.False);
            WaterQueryService query = new WaterQueryService(bake.Data);
            Vector3[] points = { new Vector3(20f, -1f, 0f), new Vector3(40f, -1f, 0f), new Vector3(100f, -1f, 0f) };
            WaterSample[] results = new WaterSample[points.Length];
            Assert.That(query.SampleBatch(points, results), Is.EqualTo(points.Length));
            Assert.That(results[0].IsInWater, Is.True);
            Assert.That(results[1].IsInWater, Is.True);
            Assert.That(results[2].IsInWater, Is.False);
            for (int i = 0; i < points.Length; i++)
                Assert.That(results[i].FluidType, Is.EqualTo(query.Sample(points[i]).FluidType));
            Object.DestroyImmediate(bake.Data);
        }

        [Test]
        public void BakeHash_IsIndependentOfAuthoringEnumerationOrder()
        {
            OceanWaterBody ocean = Add<OceanWaterBody>("ocean", Vector3.zero);
            RiverWaterBody river = Add<RiverWaterBody>("river", Vector3.zero);
            WaterBakeResult first = WaterBaker.Bake(new WaterBodyAuthoring[] { ocean, river }, settings);
            WaterBakeResult second = WaterBaker.Bake(new WaterBodyAuthoring[] { river, ocean }, settings);
            Assert.That(first.Data.DeterministicHash, Is.EqualTo(second.Data.DeterministicHash));
            Object.DestroyImmediate(first.Data);
            Object.DestroyImmediate(second.Data);
        }

        [Test]
        public void VolumeCrossingQueryCellEdge_IsQueryableFromBothCells()
        {
            LocalWaterVolume volume = Add<LocalWaterVolume>("edge", new Vector3(16f, 0f, 0f));
            volume.Size = new Vector3(2f, 2f, 2f);
            WaterBakeResult bake = WaterBaker.Bake(new WaterBodyAuthoring[] { volume }, settings);
            WaterQueryService query = new WaterQueryService(bake.Data);
            Assert.That(query.Sample(new Vector3(15.5f, -0.5f, 0f)).IsInWater, Is.True);
            Assert.That(query.Sample(new Vector3(16.5f, -0.5f, 0f)).IsInWater, Is.True);
            Object.DestroyImmediate(bake.Data);
        }

        private T Add<T>(string id, Vector3 position) where T : WaterBodyAuthoring
        {
            GameObject go = new GameObject(typeof(T).Name);
            objects.Add(go);
            go.transform.position = position;
            T component = go.AddComponent<T>();
            component.SetStableId(id);
            return component;
        }
    }
}
