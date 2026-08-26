using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.AuditTools
{
    /// <summary>
    /// Throughput benchmarks for the hot paths (meshing, water sampling serial vs Burst,
    /// cave carving, PCG scatter). Numbers print to the console as a copy-pasteable report.
    /// </summary>
    public static class PerformanceBenchmarkMenu
    {
        private const int WaterProbeCount = 20000;

        [MenuItem("WorldBuilder/Audit/Run Performance Benchmark")]
        public static void Run()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== WorldBuilder Performance Benchmark ===");
            report.AppendLine($"Machine: {SystemInfo.processorType}, {(Application.platform)}");

            // --- Meshing ---
            VoxelStoreAsset store = BuildSyntheticStore(out float chunkSize);
            var sampler = new VoxelWorldSampler(store, chunkSize);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            SurfaceNetsMesher.MeshGeometry geometry =
                SurfaceNetsMesher.ComputeGeometry(sampler, new Vector3Int(0, 0, 0), store.Resolution, chunkSize);
            watch.Stop();
            report.AppendLine($"[Mesh] ComputeGeometry 16³ chunk: {watch.Elapsed.TotalMilliseconds:F2} ms " +
                              $"({geometry.Vertices.Length} verts)");

            // --- Water queries ---
            var waterData = ScriptableObject.CreateInstance<WaterWorldRuntimeData>();
            waterData.Initialize(Vector3.zero, 32f, true, 0f, 1, 0,
                Vector3.zero, 0f,
                System.Array.Empty<RiverSegmentData>(), System.Array.Empty<BoxVolumeData>(),
                System.Array.Empty<LakeData>(), System.Array.Empty<CurrentZoneData>(),
                System.Array.Empty<Vector2>(), System.Array.Empty<WaterQueryCellData>(),
                System.Array.Empty<int>(), System.Array.Empty<int>(), System.Array.Empty<int>(),
                System.Array.Empty<int>(), "bench");
            var service = new WaterQueryService(waterData);
            var positions = new Vector3[WaterProbeCount];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = new Vector3(i % 512 - 256f, -5f + i % 7, (i * 13) % 512 - 256f);

            watch.Restart();
            var results = new WaterSample[positions.Length];
            service.SampleBatch(positions, results);
            watch.Stop();
            double serialMs = watch.Elapsed.TotalMilliseconds;

            using (var nativeQuery = new NativeWaterQuery(waterData, Unity.Collections.Allocator.TempJob))
            using (var nativePositions = new Unity.Collections.NativeArray<Vector3>(positions, Unity.Collections.Allocator.TempJob))
            using (var nativeResults = new Unity.Collections.NativeArray<WaterSample>(positions.Length, Unity.Collections.Allocator.TempJob))
            {
                watch.Restart();
                nativeQuery.SampleBatch(nativePositions, nativeResults).Complete();
                watch.Stop();
                double burstMs = watch.Elapsed.TotalMilliseconds;
                report.AppendLine($"[Water] {WaterProbeCount:N0} samples — serial: {serialMs:F2} ms · " +
                                  $"Burst parallel: {burstMs:F2} ms ({serialMs / Mathf.Max(0.01f, (float)burstMs):F1}× )");
            }

            // --- Cave carve ---
            watch.Restart();
            TerrainShapeParams shape = ScriptableObject.CreateInstance<TerrainShapeParams>();
            CaveShapeParams caves = ScriptableObject.CreateInstance<CaveShapeParams>();
            var heights = TerrainField.BuildHeightMap(shape, new Vector2(0f, 0f), 33, 4f);
            int carved = CaveField.Carve(store, heights, shape, caves, chunkSize);
            watch.Stop();
            report.AppendLine($"[Caves] Carve footprint: {carved:N0} voxels in {watch.Elapsed.TotalMilliseconds:F0} ms");

            Object.DestroyImmediate(shape);
            Object.DestroyImmediate(caves);
            Object.DestroyImmediate(store);
            Object.DestroyImmediate(waterData);

            Debug.Log(report.ToString());
        }

        private static VoxelStoreAsset BuildSyntheticStore(out float chunkSize)
        {
            chunkSize = 16f;
            const int resolution = 16;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                    store.SetDensity(entry, x, y, z,
                        y <= Mathf.FloorToInt(8 + 3 * Mathf.Sin(x * 0.6f) * Mathf.Cos(z * 0.5f)) ? 1f : 0f);
            }
            return store;
        }
    }
}
