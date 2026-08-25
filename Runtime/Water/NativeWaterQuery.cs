using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Native mirror of <see cref="WaterWorldRuntimeData"/> plus a parallel sampling job.
    /// Results are bit-compatible with the serial <see cref="WaterQueryService.Sample"/>;
    /// a parity test guards both implementations.
    /// </summary>
    public sealed class NativeWaterQuery : IDisposable
    {
        private NativeArray<float> scalars;             // [0]=queryCellSize [1]=seaLevel
        private NativeArray<Vector3> worldOrigin;       // [0]
        private NativeArray<bool> hasOcean;
        private readonly NativeArray<int> oceanMeta;

        private NativeArray<RiverSegmentData> rivers;
        private NativeArray<BoxVolumeData> volumes;
        private NativeArray<LakeData> lakes;
        private NativeArray<Vector2> lakeVertices;
        private NativeArray<WaterQueryCellData> cells;
        private NativeArray<int> riverIndices;
        private NativeArray<int> volumeIndices;
        private NativeArray<int> lakeIndices;

        public bool IsCreated { get; private set; }

        public static implicit operator bool(NativeWaterQuery query) => query != null && query.IsCreated;

        public NativeWaterQuery(WaterWorldRuntimeData data, Allocator allocator)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            worldOrigin = new NativeArray<Vector3>(1, allocator);
            worldOrigin[0] = data.WorldOrigin;
            scalars = new NativeArray<float>(2, allocator);
            scalars[0] = data.QueryCellSize;
            scalars[1] = data.SeaLevel;
            hasOcean = new NativeArray<bool>(1, allocator);
            hasOcean[0] = data.HasOcean;

            rivers = Copy(data.RiverSegments, allocator);
            volumes = Copy(data.Volumes, allocator);
            lakes = Copy(data.Lakes, allocator);
            lakeVertices = Copy(data.LakeVertices, allocator);
            cells = Copy(data.Cells, allocator);
            riverIndices = Copy(data.RiverIndices, allocator);
            volumeIndices = Copy(data.VolumeIndices, allocator);
            lakeIndices = Copy(data.LakeIndices, allocator);

            oceanMeta = new NativeArray<int>(2, allocator);
            oceanMeta[0] = data.OceanBodyId;
            oceanMeta[1] = data.OceanPriority;

            IsCreated = true;
        }

        private static NativeArray<T> Copy<T>(T[] source, Allocator allocator) where T : unmanaged
        {
            source = source ?? Array.Empty<T>();
            NativeArray<T> array = new NativeArray<T>(source.Length, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < source.Length; i++) array[i] = source[i];
            return array;
        }

        public JobHandle SampleBatch(NativeArray<Vector3> positions, NativeArray<WaterSample> results, JobHandle dependsOn = default)
        {
            WaterSampleParallelJob job = new WaterSampleParallelJob
            {
                positions = positions,
                results = results,
                worldOrigin = worldOrigin,
                scalars = scalars,
                hasOcean = hasOcean,
                oceanMeta = oceanMeta,
                rivers = rivers,
                volumes = volumes,
                lakes = lakes,
                lakeVertices = lakeVertices,
                cells = cells,
                riverIndices = riverIndices,
                volumeIndices = volumeIndices,
                lakeIndices = lakeIndices
            };
            return job.Schedule(positions.Length, 64, dependsOn);
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            worldOrigin.Dispose();
            scalars.Dispose();
            hasOcean.Dispose();
            oceanMeta.Dispose();
            rivers.Dispose();
            volumes.Dispose();
            lakes.Dispose();
            lakeVertices.Dispose();
            cells.Dispose();
            riverIndices.Dispose();
            volumeIndices.Dispose();
            lakeIndices.Dispose();
            IsCreated = false;
        }
    }

        [BurstCompile]
        internal struct WaterSampleParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> positions;
        [WriteOnly] public NativeArray<WaterSample> results;

        [ReadOnly] public NativeArray<Vector3> worldOrigin;
        [ReadOnly] public NativeArray<float> scalars;
        [ReadOnly] public NativeArray<bool> hasOcean;
        [ReadOnly] public NativeArray<int> oceanMeta;
        [ReadOnly] public NativeArray<RiverSegmentData> rivers;
        [ReadOnly] public NativeArray<BoxVolumeData> volumes;
        [ReadOnly] public NativeArray<LakeData> lakes;
        [ReadOnly] public NativeArray<Vector2> lakeVertices;
        [ReadOnly] public NativeArray<WaterQueryCellData> cells;
        [ReadOnly] public NativeArray<int> riverIndices;
        [ReadOnly] public NativeArray<int> volumeIndices;
        [ReadOnly] public NativeArray<int> lakeIndices;

        public void Execute(int index)
        {
            Vector3 position = positions[index];
            WaterSample selected = WaterSample.Air;

            if (hasOcean[0] && position.y < scalars[1])
            {
                selected = new WaterSample(FluidType.Water, scalars[1], scalars[1] - position.y,
                    Vector3.zero, 0f, oceanMeta[0], oceanMeta[1]);
            }

            QueryCellCoord coordinate = new QueryCellCoord(
                (int)Math.Floor((position.x - worldOrigin[0].x) / scalars[0]),
                (int)Math.Floor((position.z - worldOrigin[0].z) / scalars[0]));
            int cellIndex = FindCell(coordinate);
            if (cellIndex >= 0)
            {
                WaterQueryCellData cell = cells[cellIndex];

                for (int i = 0; i < cell.volumeIndexCount; i++)
                {
                    BoxVolumeData volume = volumes[volumeIndices[cell.volumeIndexStart + i]];
                    Vector3 local = volume.worldToUnitBox.MultiplyPoint3x4(position);
                    if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f || Mathf.Abs(local.z) > 0.5f) continue;
                    float surface = volume.bounds.max.y;
                    var candidate = new WaterSample(volume.fluidType, surface,
                        volume.fluidType == FluidType.Water ? Mathf.Max(0f, surface - position.y) : 0f,
                        Vector3.zero, 0f, volume.bodyId, volume.priority);
                    Select(ref selected, candidate);
                }

                for (int i = 0; i < cell.riverIndexCount; i++)
                {
                    if (TrySampleRiver(rivers[riverIndices[cell.riverIndexStart + i]], position, out WaterSample candidate))
                        Select(ref selected, candidate);
                }

                for (int i = 0; i < cell.lakeIndexCount; i++)
                {
                    LakeData lake = lakes[lakeIndices[cell.lakeIndexStart + i]];
                    if (position.y > lake.surfaceHeight || position.y < lake.surfaceHeight - lake.depth) continue;
                    if (!ContainsPolygon(position, lake)) continue;
                    var candidate = new WaterSample(FluidType.Water, lake.surfaceHeight,
                        lake.surfaceHeight - position.y, Vector3.zero, 0f, lake.bodyId, lake.priority);
                    Select(ref selected, candidate);
                }
            }

            results[index] = selected;
        }

        private int FindCell(QueryCellCoord coordinate)
        {
            int low = 0;
            int high = cells.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = cells[middle].coordinate.CompareTo(coordinate);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }

        private bool ContainsPolygon(Vector3 position, LakeData lake)
        {
            bool inside = false;
            int previous = lake.vertexStart + lake.vertexCount - 1;
            for (int i = lake.vertexStart; i < lake.vertexStart + lake.vertexCount; i++)
            {
                Vector2 a = lakeVertices[i];
                Vector2 b = lakeVertices[previous];
                bool crosses = (a.y > position.z) != (b.y > position.z) &&
                    position.x < (b.x - a.x) * (position.z - a.y) / (b.y - a.y) + a.x;
                if (crosses) inside = !inside;
                previous = i;
            }
            return inside;
        }

        private static bool TrySampleRiver(RiverSegmentData river, Vector3 position, out WaterSample sample)
        {
            Vector2 start = new Vector2(river.start.x, river.start.z);
            Vector2 end = new Vector2(river.end.x, river.end.z);
            Vector2 point = new Vector2(position.x, position.z);
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            float t = lengthSquared > 0.000001f ? Mathf.Clamp01(Vector2.Dot(point - start, delta) / lengthSquared) : 0f;
            float halfWidth = Mathf.Lerp(river.startWidth, river.endWidth, t) * 0.5f;
            if ((point - Vector2.Lerp(start, end, t)).sqrMagnitude > halfWidth * halfWidth)
            {
                sample = default;
                return false;
            }
            float surface = Mathf.Lerp(river.start.y, river.end.y, t);
            float depth = Mathf.Lerp(river.startDepth, river.endDepth, t);
            if (position.y > surface || position.y < surface - depth)
            {
                sample = default;
                return false;
            }
            sample = new WaterSample(FluidType.Water, surface, surface - position.y,
                river.flowDirection, river.flowSpeed, river.bodyId, river.priority);
            return true;
        }

        private static void Select(ref WaterSample selected, WaterSample candidate)
        {
            if (candidate.Priority > selected.Priority ||
                (candidate.Priority == selected.Priority && candidate.WaterBodyId < selected.WaterBodyId))
                selected = candidate;
        }
    }
}
