using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    /// <summary>
    /// Guards parity between the serial WaterQueryService and the parallel
    /// NativeWaterQuery job on a synthetic dataset.
    /// </summary>
    public sealed class NativeWaterQueryTests
    {
        private WaterWorldRuntimeData data;

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();

            RiverSegmentData[] rivers =
            {
                new RiverSegmentData
                {
                    start = new Vector3(10f, 1f, 0f),
                    end = new Vector3(30f, 1f, 0f),
                    startWidth = 6f,
                    endWidth = 4f,
                    startDepth = 2f,
                    endDepth = 3f,
                    flowDirection = Vector3.right,
                    flowSpeed = 1.5f,
                    bodyId = 2,
                    priority = 5
                }
            };

            BoxVolumeData[] volumes =
            {
                new BoxVolumeData
                {
                    worldToUnitBox = Matrix4x4.TRS(new Vector3(50f, -2f, 50f), Quaternion.identity, new Vector3(20f, 8f, 20f)).inverse,
                    bounds = new Bounds(new Vector3(50f, -2f, 50f), new Vector3(20f, 8f, 20f)),
                    fluidType = FluidType.Water,
                    bodyId = 3,
                    priority = 7
                }
            };

            LakeData[] lakes =
            {
                new LakeData
                {
                    vertexStart = 0,
                    vertexCount = 4,
                    surfaceHeight = 0.5f,
                    depth = 4f,
                    bodyId = 4,
                    priority = 9,
                    bounds = new Bounds(new Vector3(-40f, 0f, -40f), new Vector3(24f, 8f, 24f))
                }
            };

            // Diamond-shaped lake polygon around (-40, -40).
            Vector2[] vertices =
            {
                new Vector2(-52f, -40f),
                new Vector2(-40f, -28f),
                new Vector2(-28f, -40f),
                new Vector2(-40f, -52f)
            };

            List<WaterQueryCellData> cells = new List<WaterQueryCellData>();
            List<int> riverIndices = new List<int>();
            List<int> volumeIndices = new List<int>();
            List<int> lakeIndices = new List<int>();

            cells.Add(new WaterQueryCellData
            {
                coordinate = new QueryCellCoord(0, 0),
                riverIndexStart = 0, riverIndexCount = 1,
                volumeIndexStart = 0, volumeIndexCount = 0,
                lakeIndexStart = 0, lakeIndexCount = 0
            });
            riverIndices.Add(0);

            cells.Add(new WaterQueryCellData
            {
                coordinate = new QueryCellCoord(1, 1),
                riverIndexStart = 0, riverIndexCount = 0,
                volumeIndexStart = 0, volumeIndexCount = 1,
                lakeIndexStart = 0, lakeIndexCount = 0
            });
            volumeIndices.Add(0);

            cells.Add(new WaterQueryCellData
            {
                coordinate = new QueryCellCoord(-2, -2),
                riverIndexStart = 0, riverIndexCount = 0,
                volumeIndexStart = 0, volumeIndexCount = 0,
                lakeIndexStart = 0, lakeIndexCount = 1
            });
            lakeIndices.Add(0);

            cells.Sort((left, right) => left.coordinate.CompareTo(right.coordinate));

            data.Initialize(
                Vector3.zero, 32f, false, -999f, 0, 0,
                Vector3.zero, 0f,
                rivers, volumes, lakes, System.Array.Empty<CurrentZoneData>(), vertices, cells.ToArray(),
                riverIndices.ToArray(), volumeIndices.ToArray(), lakeIndices.ToArray(),
                System.Array.Empty<int>(), "test-hash");
        }

        [TearDown]
        public void TearDown()
        {
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
        }

        private static readonly Vector3[] ProbePositions =
        {
            new Vector3(20f, 0.5f, 0f),     // inside river
            new Vector3(20f, 5f, 0f),       // above river surface
            new Vector3(50f, -3f, 50f),     // inside box volume
            new Vector3(-40f, 0f, -40f),    // inside lake polygon
            new Vector3(-60f, 0f, -60f),    // outside lake (same cell)
            new Vector3(200f, 1f, 200f)     // no cell at all -> air
        };

        [Test]
        public void ParallelJob_MatchesSerialSampling()
        {
            WaterQueryService serial = new WaterQueryService(data);

            NativeArray<Vector3> positions = new NativeArray<Vector3>(ProbePositions.Length, Allocator.TempJob);
            NativeArray<WaterSample> results = new NativeArray<WaterSample>(ProbePositions.Length, Allocator.TempJob);
            using (NativeWaterQuery native = new NativeWaterQuery(data, Allocator.TempJob))
            {
                for (int i = 0; i < ProbePositions.Length; i++) positions[i] = ProbePositions[i];
                JobHandle handle = native.SampleBatch(positions, results);
                handle.Complete();

                for (int i = 0; i < ProbePositions.Length; i++)
                {
                    WaterSample expected = serial.Sample(ProbePositions[i]);
                    WaterSample actual = results[i];

                    Assert.That(actual.IsInWater, Is.EqualTo(expected.IsInWater), $"index {i} in-water");
                    if (expected.IsInWater)
                    {
                        Assert.That(actual.Depth, Is.EqualTo(expected.Depth).Within(1e-4f), $"index {i} depth");
                        Assert.That(actual.SurfaceHeight, Is.EqualTo(expected.SurfaceHeight).Within(1e-4f), $"index {i} surface");
                        Assert.That(actual.WaterBodyId, Is.EqualTo(expected.WaterBodyId), $"index {i} bodyId");
                        Assert.That(actual.FlowSpeed, Is.EqualTo(expected.FlowSpeed).Within(1e-4f), $"index {i} flow");
                    }
                }
            }

            positions.Dispose();
            results.Dispose();
        }
    }
}
