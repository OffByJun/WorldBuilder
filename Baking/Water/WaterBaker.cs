using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using WorldBuilder.Authoring.Water;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Baking.Water
{
    public sealed class WaterBakeResult
    {
        public WaterWorldRuntimeData Data { get; }
        public WorldBakeReport Report { get; }
        public WaterBakeResult(WaterWorldRuntimeData data, WorldBakeReport report) { Data = data; Report = report; }
    }

    public static class WaterBaker
    {
        private sealed class CellBuilder
        {
            public readonly List<int> rivers = new List<int>();
            public readonly List<int> volumes = new List<int>();
            public readonly List<int> lakes = new List<int>();
        }

        public static WaterBakeResult Bake(IEnumerable<WaterBodyAuthoring> source, WorldGridSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            WorldBakeReport report = new WorldBakeReport();
            List<WaterBodyAuthoring> bodies = new List<WaterBodyAuthoring>();
            if (source != null)
                foreach (WaterBodyAuthoring body in source) if (body != null) bodies.Add(body);
            bodies.Sort(CompareBodies);
            ValidateIds(bodies, report);

            WorldGrid grid = settings.CreateGrid();
            List<RiverSegmentData> rivers = new List<RiverSegmentData>();
            List<BoxVolumeData> volumes = new List<BoxVolumeData>();
            List<LakeData> lakes = new List<LakeData>();
            List<Vector2> lakeVertices = new List<Vector2>();
            SortedDictionary<QueryCellCoord, CellBuilder> cells = new SortedDictionary<QueryCellCoord, CellBuilder>();
            bool hasOcean = false;
            float seaLevel = 0f;
            int oceanId = 0;
            int oceanPriority = 0;

            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                WaterBodyAuthoring body = bodies[bodyIndex];
                if (string.IsNullOrWhiteSpace(body.StableId)) continue;
                int id = DeterministicHash.StableInt32(body.StableId);
                if (body is OceanWaterBody ocean)
                {
                    if (hasOcean)
                    {
                        report.Add(BakeIssueSeverity.Error, "WB_WATER_MULTIPLE_OCEANS", body.StableId,
                            "Only one OceanWaterBody can be baked into a WaterWorldRuntimeData asset.");
                        continue;
                    }
                    hasOcean = true;
                    seaLevel = ocean.SeaLevel;
                    oceanId = id;
                    oceanPriority = ocean.Priority;
                }
                else if (body is RiverWaterBody river)
                {
                    BakeRiver(river, id, grid, rivers, cells, report);
                }
                else if (body is LakeWaterBody lake)
                {
                    BakeLake(lake, id, grid, lakes, lakeVertices, cells, report);
                }
                else if (body is BoxWaterBodyAuthoring box)
                {
                    FluidType fluid = body is AirOverrideVolume ? FluidType.Air : FluidType.Water;
                    BoxVolumeData item = new BoxVolumeData
                    {
                        worldToUnitBox = box.GetWorldToUnitBoxMatrix(),
                        bounds = box.GetWorldBounds(),
                        fluidType = fluid,
                        bodyId = id,
                        priority = box.Priority
                    };
                    int index = volumes.Count;
                    volumes.Add(item);
                    Register(item.bounds, grid, cells, builder => builder.volumes.Add(index));
                }
            }

            List<WaterQueryCellData> cellData = new List<WaterQueryCellData>(cells.Count);
            List<int> riverIndices = new List<int>();
            List<int> volumeIndices = new List<int>();
            List<int> lakeIndices = new List<int>();
            foreach (KeyValuePair<QueryCellCoord, CellBuilder> pair in cells)
            {
                SortUnique(pair.Value.rivers);
                SortUnique(pair.Value.volumes);
                SortUnique(pair.Value.lakes);
                WaterQueryCellData item = new WaterQueryCellData
                {
                    coordinate = pair.Key,
                    riverIndexStart = riverIndices.Count,
                    riverIndexCount = pair.Value.rivers.Count,
                    volumeIndexStart = volumeIndices.Count,
                    volumeIndexCount = pair.Value.volumes.Count,
                    lakeIndexStart = lakeIndices.Count,
                    lakeIndexCount = pair.Value.lakes.Count
                };
                riverIndices.AddRange(pair.Value.rivers);
                volumeIndices.AddRange(pair.Value.volumes);
                lakeIndices.AddRange(pair.Value.lakes);
                cellData.Add(item);
            }

            string hash = BuildHash(settings, hasOcean, seaLevel, oceanId, oceanPriority,
                rivers, volumes, lakes, lakeVertices, cellData, riverIndices, volumeIndices, lakeIndices);
            WaterWorldRuntimeData data = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            data.Initialize(settings.WorldOrigin, settings.QueryCellSize, hasOcean, seaLevel, oceanId, oceanPriority,
                rivers.ToArray(), volumes.ToArray(), lakes.ToArray(), lakeVertices.ToArray(), cellData.ToArray(),
                riverIndices.ToArray(), volumeIndices.ToArray(), lakeIndices.ToArray(), hash);
            report.Sort();
            return new WaterBakeResult(data, report);
        }

        private static void BakeRiver(RiverWaterBody river, int id, WorldGrid grid,
            List<RiverSegmentData> output, SortedDictionary<QueryCellCoord, CellBuilder> cells, WorldBakeReport report)
        {
            if (river.Knots.Count < 2)
            {
                report.Add(BakeIssueSeverity.Error, "WB_RIVER_KNOT_COUNT", river.StableId,
                    "A river requires at least two knots.");
                return;
            }
            float estimatedLength = 0f;
            Vector3 previous = river.EvaluateWorldPosition(0f);
            const int estimateSamples = 32;
            for (int i = 1; i <= estimateSamples; i++)
            {
                Vector3 current = river.EvaluateWorldPosition(i / (float)estimateSamples);
                estimatedLength += Vector3.Distance(previous, current);
                previous = current;
            }
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(estimatedLength / river.BakeSpacing));
            for (int i = 0; i < segmentCount; i++)
            {
                float t0 = i / (float)segmentCount;
                float t1 = (i + 1) / (float)segmentCount;
                Vector3 start = river.EvaluateWorldPosition(t0);
                Vector3 end = river.EvaluateWorldPosition(t1);
                RiverKnot a = river.EvaluateKnot(t0);
                RiverKnot b = river.EvaluateKnot(t1);
                float maxWidth = Mathf.Max(a.width, b.width);
                float maxDepth = Mathf.Max(a.depth, b.depth);
                Vector3 min = Vector3.Min(start, end) - new Vector3(maxWidth * 0.5f, maxDepth, maxWidth * 0.5f);
                Vector3 max = Vector3.Max(start, end) + new Vector3(maxWidth * 0.5f, 0f, maxWidth * 0.5f);
                Vector3 direction = end - start;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.000001f) direction.Normalize();
                RiverSegmentData segment = new RiverSegmentData
                {
                    start = start,
                    end = end,
                    startWidth = a.width,
                    endWidth = b.width,
                    startDepth = a.depth,
                    endDepth = b.depth,
                    flowDirection = direction,
                    flowSpeed = (a.flowSpeed + b.flowSpeed) * 0.5f,
                    bodyId = id,
                    priority = river.Priority,
                    bounds = new Bounds((min + max) * 0.5f, max - min)
                };
                int index = output.Count;
                output.Add(segment);
                Register(segment.bounds, grid, cells, builder => builder.rivers.Add(index));
            }
        }

        private static void BakeLake(LakeWaterBody lake, int id, WorldGrid grid, List<LakeData> output,
            List<Vector2> vertices, SortedDictionary<QueryCellCoord, CellBuilder> cells, WorldBakeReport report)
        {
            if (lake.Polygon.Count < 3)
            {
                report.Add(BakeIssueSeverity.Error, "WB_LAKE_VERTEX_COUNT", lake.StableId,
                    "A lake requires at least three polygon vertices.");
                return;
            }
            int start = vertices.Count;
            Vector3 first = lake.transform.TransformPoint(lake.Polygon[0]);
            Vector3 min = first - Vector3.up * lake.Depth;
            Vector3 max = first;
            for (int i = 0; i < lake.Polygon.Count; i++)
            {
                Vector3 world = lake.transform.TransformPoint(lake.Polygon[i]);
                vertices.Add(new Vector2(world.x, world.z));
                min = Vector3.Min(min, new Vector3(world.x, first.y - lake.Depth, world.z));
                max = Vector3.Max(max, new Vector3(world.x, first.y, world.z));
            }
            LakeData item = new LakeData
            {
                vertexStart = start,
                vertexCount = lake.Polygon.Count,
                surfaceHeight = first.y,
                depth = lake.Depth,
                bodyId = id,
                priority = lake.Priority,
                bounds = new Bounds((min + max) * 0.5f, max - min)
            };
            int index = output.Count;
            output.Add(item);
            Register(item.bounds, grid, cells, builder => builder.lakes.Add(index));
        }

        private static void Register(Bounds bounds, WorldGrid grid,
            SortedDictionary<QueryCellCoord, CellBuilder> cells, Action<CellBuilder> add)
        {
            QueryCellCoord min = grid.WorldToQueryCell(bounds.min);
            QueryCellCoord max = grid.WorldToQueryCell(bounds.max);
            for (int x = min.X; x <= max.X; x++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                QueryCellCoord coordinate = new QueryCellCoord(x, z);
                if (!cells.TryGetValue(coordinate, out CellBuilder builder))
                {
                    builder = new CellBuilder();
                    cells.Add(coordinate, builder);
                }
                add(builder);
            }
        }

        private static void ValidateIds(List<WaterBodyAuthoring> bodies, WorldBakeReport report)
        {
            string previous = null;
            for (int i = 0; i < bodies.Count; i++)
            {
                string id = bodies[i].StableId;
                if (string.IsNullOrWhiteSpace(id))
                    report.Add(BakeIssueSeverity.Error, "WB_WATER_ID_MISSING", bodies[i].name,
                        "Every authoring body requires a persistent stable ID.");
                else if (string.Equals(previous, id, StringComparison.Ordinal))
                    report.Add(BakeIssueSeverity.Error, "WB_WATER_ID_DUPLICATE", id,
                        "Water body stable IDs must be unique.");
                previous = id;
            }
        }

        private static int CompareBodies(WaterBodyAuthoring left, WaterBodyAuthoring right)
        {
            int id = string.CompareOrdinal(left?.StableId, right?.StableId);
            return id != 0 ? id : string.CompareOrdinal(left?.GetType().FullName, right?.GetType().FullName);
        }

        private static void SortUnique(List<int> values)
        {
            values.Sort();
            int write = 0;
            for (int read = 0; read < values.Count; read++)
                if (write == 0 || values[read] != values[write - 1]) values[write++] = values[read];
            if (write < values.Count) values.RemoveRange(write, values.Count - write);
        }

        private static string BuildHash(WorldGridSettings settings, bool ocean, float level, int oceanId,
            int oceanPriority, List<RiverSegmentData> rivers, List<BoxVolumeData> volumes, List<LakeData> lakes,
            List<Vector2> vertices, List<WaterQueryCellData> cells, List<int> riverIndices,
            List<int> volumeIndices, List<int> lakeIndices)
        {
            StringBuilder text = new StringBuilder();
            text.Append(F(settings.QueryCellSize)).Append('|').Append(F(settings.WorldOrigin.x)).Append('|')
                .Append(F(settings.WorldOrigin.y)).Append('|').Append(F(settings.WorldOrigin.z)).Append('\n');
            text.Append(ocean ? 1 : 0).Append('|').Append(F(level)).Append('|').Append(oceanId).Append('|').Append(oceanPriority).Append('\n');
            foreach (RiverSegmentData v in rivers) text.Append("R|").Append(V(v.start)).Append('|').Append(V(v.end)).Append('|')
                .Append(F(v.startWidth)).Append('|').Append(F(v.endWidth)).Append('|').Append(F(v.startDepth)).Append('|')
                .Append(F(v.endDepth)).Append('|').Append(v.bodyId).Append('|').Append(v.priority).Append('\n');
            foreach (BoxVolumeData v in volumes) text.Append("B|").Append(v.bodyId).Append('|').Append(v.priority).Append('|')
                .Append((int)v.fluidType).Append('|').Append(Matrix(v.worldToUnitBox)).Append('\n');
            foreach (LakeData v in lakes) text.Append("L|").Append(v.bodyId).Append('|').Append(v.priority).Append('|')
                .Append(v.vertexStart).Append('|').Append(v.vertexCount).Append('|').Append(F(v.surfaceHeight)).Append('|').Append(F(v.depth)).Append('\n');
            foreach (Vector2 v in vertices) text.Append("V|").Append(F(v.x)).Append('|').Append(F(v.y)).Append('\n');
            foreach (WaterQueryCellData v in cells) text.Append("C|").Append(v.coordinate.X).Append('|').Append(v.coordinate.Z).Append('|')
                .Append(v.riverIndexStart).Append('|').Append(v.riverIndexCount).Append('|').Append(v.volumeIndexStart).Append('|')
                .Append(v.volumeIndexCount).Append('|').Append(v.lakeIndexStart).Append('|').Append(v.lakeIndexCount).Append('\n');
            text.Append("RI|").Append(string.Join(",", riverIndices)).Append("\nVI|").Append(string.Join(",", volumeIndices))
                .Append("\nLI|").Append(string.Join(",", lakeIndices));
            return DeterministicHash.Sha256(text.ToString());
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string Matrix(Matrix4x4 value)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < 16; i++) { if (i > 0) text.Append(','); text.Append(F(value[i])); }
            return text.ToString();
        }
    }
}
