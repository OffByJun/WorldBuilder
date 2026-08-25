using System;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Water
{
    public readonly struct WaterSample
    {
        public FluidType FluidType { get; }
        public bool IsInWater => FluidType == WorldBuilder.Runtime.Water.FluidType.Water;
        public float SurfaceHeight { get; }
        public float Depth { get; }
        public Vector3 FlowDirection { get; }
        public float FlowSpeed { get; }
        public int WaterBodyId { get; }
        public int Priority { get; }

        public WaterSample(FluidType fluidType, float surfaceHeight, float depth, Vector3 flowDirection,
            float flowSpeed, int waterBodyId, int priority)
        {
            FluidType = fluidType;
            SurfaceHeight = surfaceHeight;
            Depth = depth;
            FlowDirection = flowDirection;
            FlowSpeed = flowSpeed;
            WaterBodyId = waterBodyId;
            Priority = priority;
        }

        public static WaterSample Air => new WaterSample(FluidType.Air, 0f, 0f, Vector3.zero, 0f, 0, int.MinValue);
    }

    public interface IWaterQueryService
    {
        WaterSample Sample(Vector3 worldPosition);
        int SampleBatch(Vector3[] positions, WaterSample[] results);
    }

    public sealed class WaterQueryService : IWaterQueryService
    {
        private readonly WaterWorldRuntimeData data;

        public WaterQueryService(WaterWorldRuntimeData data)
        {
            this.data = data != null ? data : throw new ArgumentNullException(nameof(data));
        }

        public WaterSample Sample(Vector3 position)
        {
            WaterSample selected = data.HasOcean && position.y < data.SeaLevel
                ? new WaterSample(FluidType.Water, data.SeaLevel, data.SeaLevel - position.y,
                    data.OceanFlowDirection, data.OceanFlowSpeed, data.OceanBodyId, data.OceanPriority)
                : WaterSample.Air;

            QueryCellCoord coordinate = new QueryCellCoord(
                Mathf.FloorToInt((position.x - data.WorldOrigin.x) / data.QueryCellSize),
                Mathf.FloorToInt((position.z - data.WorldOrigin.z) / data.QueryCellSize));
            int cellIndex = FindCell(coordinate);
            if (cellIndex < 0) return selected;
            WaterQueryCellData cell = data.Cells[cellIndex];

            for (int i = 0; i < cell.volumeIndexCount; i++)
            {
                BoxVolumeData volume = data.Volumes[data.VolumeIndices[cell.volumeIndexStart + i]];
                Vector3 local = volume.worldToUnitBox.MultiplyPoint3x4(position);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f || Mathf.Abs(local.z) > 0.5f) continue;
                float surface = volume.bounds.max.y;
                WaterSample candidate = new WaterSample(volume.fluidType, surface,
                    volume.fluidType == FluidType.Water ? Mathf.Max(0f, surface - position.y) : 0f,
                    Vector3.zero, 0f, volume.bodyId, volume.priority);
                Select(ref selected, candidate);
            }

            for (int i = 0; i < cell.riverIndexCount; i++)
            {
                RiverSegmentData river = data.RiverSegments[data.RiverIndices[cell.riverIndexStart + i]];
                if (!TrySampleRiver(river, position, out WaterSample candidate)) continue;
                Select(ref selected, candidate);
            }

            for (int i = 0; i < cell.lakeIndexCount; i++)
            {
                LakeData lake = data.Lakes[data.LakeIndices[cell.lakeIndexStart + i]];
                if (position.y > lake.surfaceHeight || position.y < lake.surfaceHeight - lake.depth) continue;
                if (!ContainsPolygon(position, lake)) continue;
                WaterSample candidate = new WaterSample(FluidType.Water, lake.surfaceHeight,
                    lake.surfaceHeight - position.y, Vector3.zero, 0f, lake.bodyId, lake.priority);
                Select(ref selected, candidate);
            }

            return ApplyCurrentOverride(selected, cell, position);
        }

        /// <summary>
        /// Current zones do not add water — they redirect whatever water already won the
        /// priority selection. The highest-priority containing zone replaces the flow.
        /// </summary>
        private WaterSample ApplyCurrentOverride(WaterSample selected, WaterQueryCellData cell,
            Vector3 position)
        {
            if (!selected.IsInWater || cell.currentIndexCount == 0) return selected;

            int bestZone = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < cell.currentIndexCount; i++)
            {
                CurrentZoneData zone = data.Currents[data.CurrentIndices[cell.currentIndexStart + i]];
                if (!zone.bounds.Contains(position)) continue;
                if (zone.priority <= bestPriority) continue;
                bestPriority = zone.priority;
                bestZone = data.CurrentIndices[cell.currentIndexStart + i];
            }
            if (bestZone < 0) return selected;

            CurrentZoneData winner = data.Currents[bestZone];
            return new WaterSample(selected.FluidType, selected.SurfaceHeight, selected.Depth,
                winner.direction, winner.speed, selected.WaterBodyId, selected.Priority);
        }

        public int SampleBatch(Vector3[] positions, WaterSample[] results)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length < positions.Length) throw new ArgumentException("Results must fit every position.", nameof(results));
            for (int i = 0; i < positions.Length; i++) results[i] = Sample(positions[i]);
            return positions.Length;
        }

        private int FindCell(QueryCellCoord coordinate)
        {
            int low = 0;
            int high = data.Cells.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = data.Cells[middle].coordinate.CompareTo(coordinate);
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
                Vector2 a = data.LakeVertices[i];
                Vector2 b = data.LakeVertices[previous];
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
