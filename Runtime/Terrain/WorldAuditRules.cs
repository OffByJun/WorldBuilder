using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    public sealed class AuditIssue
    {
        public string Code { get; }
        public string Message { get; }
        public Vector3Int Chunk { get; }

        public AuditIssue(string code, string message, Vector3Int chunk)
        {
            Code = code;
            Message = message;
            Chunk = chunk;
        }

        public override string ToString() => $"[{Code}] {Message}";
    }

    /// <summary>
    /// Terrain-focused world audit rules run straight over the voxel store: isolated chunk
    /// islands, corrupt density values and cross-chunk border mismatches that would show up
    /// as seams after saves or hand edits.
    /// </summary>
    public static class WorldAuditRules
    {
        private static readonly Vector3Int[] AxisNeighbors =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        /// <summary>Chunks with solid content but no neighbours on any axis — likely lost fragments.</summary>
        public static List<AuditIssue> CheckIsolatedChunks(VoxelStoreAsset store,
            float minSolidRatio = 0.02f)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var issues = new List<AuditIssue>();

            foreach (Vector3Int coord in store.Coords)
            {
                if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) continue;

                bool hasNeighbor = false;
                foreach (Vector3Int offset in AxisNeighbors)
                {
                    if (store.TryGetEntry(coord + offset, out _))
                    {
                        hasNeighbor = true;
                        break;
                    }
                }
                if (hasNeighbor) continue;

                int resolution = store.Resolution;
                int solid = 0;
                for (int i = 0; i < entry.density.Length; i++)
                    if (entry.density[i] >= SurfaceNetsMesher.IsoLevel) solid++;

                float ratio = solid / (float)entry.density.Length;
                if (ratio >= minSolidRatio)
                    issues.Add(new AuditIssue("WB_ISOLATED_CHUNK",
                        $"Isolated chunk holds {ratio:P0} solid voxels with no neighbours.", coord));
            }
            return issues;
        }

        /// <summary>NaN or out-of-range density values corrupt meshing and saves.</summary>
        public static List<AuditIssue> CheckDensitySanity(VoxelStoreAsset store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var issues = new List<AuditIssue>();

            foreach (Vector3Int coord in store.Coords)
            {
                if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) continue;
                for (int i = 0; i < entry.density.Length; i++)
                {
                    float value = entry.density[i];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        issues.Add(new AuditIssue("WB_DENSITY_NAN",
                            $"Non-finite density at voxel index {i}.", coord));
                        break;
                    }
                    if (value < -0.001f || value > 1.001f)
                    {
                        issues.Add(new AuditIssue("WB_DENSITY_RANGE",
                            $"Density {value:0.###} outside [0..1] at voxel index {i}.", coord));
                        break;
                    }
                }
            }
            return issues;
        }

        /// <summary>
        /// Adjacent chunks whose touching border densities disagree — the mesher welds by
        /// sampling across borders, so mismatched data means stale edits or bad merges.
        /// Returns one issue per offending border pair (capped per chunk).
        /// </summary>
        public static List<AuditIssue> CheckBorderContinuity(VoxelStoreAsset store,
            float epsilon = 0.01f, int maxIssuesPerPair = 3)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var issues = new List<AuditIssue>();
            int resolution = store.Resolution;
            var visited = new HashSet<(Vector3Int, Vector3Int)>();

            foreach (Vector3Int coord in store.Coords)
            {
                foreach (Vector3Int axis in new[]
                         {
                             new Vector3Int(1, 0, 0),
                             new Vector3Int(0, 1, 0),
                             new Vector3Int(0, 0, 1)
                         })
                {
                    Vector3Int neighborCoord = coord + axis;
                    if (!store.TryGetEntry(neighborCoord, out VoxelChunkEntry neighbor)) continue;

                    bool coordFirst =
                        coord.x != neighborCoord.x ? coord.x < neighborCoord.x :
                        coord.y != neighborCoord.y ? coord.y < neighborCoord.y :
                        coord.z <= neighborCoord.z;
                    if (!visited.Add(coordFirst ? (coord, neighborCoord) : (neighborCoord, coord)))
                        continue;

                    int mismatches = 0;
                    for (int a = 0; a < resolution && mismatches <= maxIssuesPerPair; a++)
                    {
                        for (int b = 0; b < resolution; b++)
                        {
                            // Free axes share indices; the split axis uses last/first lattice.
                            float mine = ReadFace(store, coord, axis, positive: true, a, b);
                            float theirs = ReadFace(store, neighborCoord, axis, positive: false, a, b);
                            if (Mathf.Abs(mine - theirs) > epsilon)
                            {
                                mismatches++;
                                if (mismatches <= maxIssuesPerPair)
                                    issues.Add(new AuditIssue("WB_BORDER_MISMATCH",
                                        $"Border density {mine:0.###} vs {theirs:0.###} along {axis} at ({a},{b}).", coord));
                                break;
                            }
                        }
                    }
                }
            }
            return issues;
        }

        private static float ReadFace(VoxelStoreAsset store, Vector3Int coord, Vector3Int axis,
            bool positive, int a, int b)
        {
            if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) return 0f;
            int last = store.Resolution - 1;
            int split = positive ? last : 0;
            int x = axis.x != 0 ? split : a;
            int y = axis.y != 0 ? split : axis.x != 0 ? b : a; // keep free-axis order stable
            int z = axis.z != 0 ? split : axis.x != 0 ? a : b;
            if (axis.y != 0) { x = a; z = b; }
            return store.GetDensity(entry, x, y, z);
        }
    }
}
