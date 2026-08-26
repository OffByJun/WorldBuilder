using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    public sealed class StabilityReport
    {
        /// <summary>World-space samples taken from the detached solid cluster(s).</summary>
        public List<Vector3> DetachedSamples { get; } = new List<Vector3>();
        /// <summary>Total solid voxels not connected to the world base.</summary>
        public int DetachedCount { get; set; }
        /// <summary>False when the node budget stopped the scan early.</summary>
        public bool Complete { get; set; }
    }

    /// <summary>
    /// Connectivity-based collapse analysis: floods solid voxels from the lowest solid rows
    /// of every chunk column; anything unreachable is hanging in the air and will fall once
    /// structural gameplay (or a mod) asks it to. Pure, budget-capped, allocation-heavy —
    /// drive it throttled from <see cref="CollapseWatcher"/>.
    /// </summary>
    public static class CaveStabilityAnalyzer
    {
        private const int CoordinateBias = 4096;

        public static StabilityReport FindDetachedSolid(VoxelStoreAsset store, float chunkSize,
            float minY = -999f, float maxY = 999f, int nodeBudget = 250000,
            int maxSamples = 64)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            var report = new StabilityReport { Complete = true };
            var visited = new HashSet<long>();
            var stack = new Stack<(int ix, int iy, int iz)>();

            SeedBaseRows(store, chunkSize, minY, maxY, visited, stack);

            int resolution = store.Resolution;
            float spacing = chunkSize / resolution;
            var neighbors = stack.Count > 0 ? new List<(int, int, int)>(6) : null;

            while (stack.Count > 0 && visited.Count <= nodeBudget)
            {
                (int ix, int iy, int iz) = stack.Pop();
                neighbors.Clear();
                neighbors.Add((ix + 1, iy, iz));
                neighbors.Add((ix - 1, iy, iz));
                neighbors.Add((ix, iy + 1, iz));
                neighbors.Add((ix, iy - 1, iz));
                neighbors.Add((ix, iy, iz + 1));
                neighbors.Add((ix, iy, iz - 1));

                foreach ((int nx, int ny, int nz) in neighbors)
                {
                    long key = Key(nx, ny, nz);
                    if (!visited.Add(key)) continue;
                    if (visited.Count > nodeBudget)
                    {
                        report.Complete = false;
                        break;
                    }
                    float density = SampleLattice(store, chunkSize, nx, ny, nz);
                    if (density >= SurfaceNetsMesher.IsoLevel)
                        stack.Push((nx, ny, nz));
                    else
                        visited.Remove(key); // air: don't waste budget on it
                }
            }

            // Second pass: count + sample solids that the flood never reached.
            foreach (Vector3Int coord in store.Coords)
            {
                Vector3 origin = new Vector3(coord.x * chunkSize, coord.y * chunkSize,
                    coord.z * chunkSize);
                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                {
                    float worldY = origin.y + y * spacing;
                    if (worldY < minY || worldY > maxY) continue;
                    if (store.GetDensity(store.GetOrCreate(coord), x, y, z) <
                        SurfaceNetsMesher.IsoLevel) continue;

                    long key = Key(
                        coord.x * resolution + x,
                        coord.y * resolution + y,
                        coord.z * resolution + z);
                    if (visited.Contains(key)) continue;

                    report.DetachedCount++;
                    if (report.DetachedSamples.Count < maxSamples)
                        report.DetachedSamples.Add(origin +
                            new Vector3(x * spacing, y * spacing, z * spacing));
                }
            }
            return report;
        }

        private static void SeedBaseRows(VoxelStoreAsset store, float chunkSize,
            float minY, float maxY, HashSet<long> visited, Stack<(int, int, int)> stack)
        {
            // Lowest existing Y layer per (cx,cz) column seeds the flood — the world floor.
            var lowestLayer = new Dictionary<Vector2Int, Vector3Int>();
            foreach (Vector3Int coord in store.Coords)
            {
                var column = new Vector2Int(coord.x, coord.z);
                if (!lowestLayer.TryGetValue(column, out Vector3Int best) || coord.y < best.y)
                    lowestLayer[column] = coord;
            }

            int resolution = store.Resolution;
            foreach (KeyValuePair<Vector2Int, Vector3Int> pair in lowestLayer)
            {
                if (!store.TryGetEntry(pair.Value, out VoxelChunkEntry entry)) continue;
                for (int x = 0; x < resolution; x++)
                for (int z = 0; z < resolution; z++)
                {
                    float worldY = pair.Value.y * chunkSize;
                    if (worldY < minY || worldY > maxY) continue;
                    if (store.GetDensity(entry, x, 0, z) < SurfaceNetsMesher.IsoLevel) continue;

                    int gx = pair.Value.x * resolution + x;
                    int gy = pair.Value.y * resolution;
                    int gz = pair.Value.z * resolution + z;
                    if (visited.Add(Key(gx, gy, gz)))
                        stack.Push((gx, gy, gz));
                }
            }
        }

        private static float SampleLattice(VoxelStoreAsset store, float chunkSize,
            int ix, int iy, int iz)
        {
            // Direct entry lookup when inside a known chunk; zero otherwise (air).
            int resolution = store.Resolution;
            int cx = DivFloor(ix, resolution);
            int cy = DivFloor(iy, resolution);
            int cz = DivFloor(iz, resolution);
            if (!store.TryGetEntry(new Vector3Int(cx, cy, cz), out VoxelChunkEntry entry)) return 0f;
            return entry.density[
                Mod(ix, resolution) + resolution * (Mod(iy, resolution) + resolution * Mod(iz, resolution))];
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x + CoordinateBias) << 42) | ((long)(y + CoordinateBias) << 21) |
            (long)(z + CoordinateBias);

        private static int DivFloor(int value, int divisor) =>
            divisor <= 0 ? value : value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }

    /// <summary>
    /// Watches runtime terrain deformation (throttled) and reports detached solid clusters
    /// — hook survival gameplay (collapse warnings, falling debris) to <see cref="UnstableFound"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CollapseWatcher : MonoBehaviour
    {
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private float chunkSize = 128f;
        [Min(0.5f)] [SerializeField] private float checkIntervalSeconds = 3f;
        [Tooltip("Log a warning when detached terrain is detected.")]
        [SerializeField] private bool logWarnings = true;
        [SerializeField] private GameObject markerPrefab;
        [Min(10000)] [SerializeField] private int nodeBudget = 250000;

        private bool dirty;
        private float timer;

        public event Action<StabilityReport> UnstableFound;

        public int LastDetachedCount { get; private set; }

        private void OnEnable() => TerrainDeformer.ChunkDeformed += OnChunkDeformed;
        private void OnDisable() => TerrainDeformer.ChunkDeformed -= OnChunkDeformed;

        private void OnChunkDeformed(Vector3Int _) => dirty = true;

        private void Update()
        {
            timer += Time.deltaTime;
            if (!dirty || timer < checkIntervalSeconds) return;
            timer = 0f;
            dirty = false;

            StabilityReport report =
                CaveStabilityAnalyzer.FindDetachedSolid(store, chunkSize, nodeBudget: nodeBudget);
            LastDetachedCount = report.DetachedCount;
            if (report.DetachedCount == 0) return;

            if (logWarnings)
                Debug.LogWarning($"[WorldBuilder] {report.DetachedCount} detached solid voxel(s) " +
                                 $"detected{(report.Complete ? "" : " (scan truncated)")}).");
            int markers = Math.Min(8, report.DetachedSamples.Count);
            for (int i = 0; i < markers; i++)
                if (markerPrefab != null)
                    Instantiate(markerPrefab, report.DetachedSamples[i], Quaternion.identity);
            UnstableFound?.Invoke(report);
        }
    }
}
