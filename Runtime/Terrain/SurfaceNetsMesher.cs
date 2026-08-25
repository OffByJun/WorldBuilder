using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// World-space trilinear density sampler over a VoxelStoreAsset with cross-chunk
    /// lookups, so meshes weld seamlessly across chunk borders.
    /// Hot path: caches live chunk entries and reads their density arrays directly —
    /// zero allocation per sample.
    /// </summary>
    public sealed class VoxelWorldSampler
    {
        private readonly VoxelStoreAsset store;
        private readonly float chunkSize;
        private readonly int resolution;
        private readonly Dictionary<Vector3Int, VoxelChunkEntry> entryCache =
            new Dictionary<Vector3Int, VoxelChunkEntry>(64);

        public VoxelWorldSampler(VoxelStoreAsset store, float chunkSize)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.chunkSize = Mathf.Max(0.001f, chunkSize);
            resolution = store.Resolution;
        }

        /// <summary>Voxels per chunk axis — spacing between lattice samples.</summary>
        public int SamplePointResolution => resolution;

        public float Sample(float x, float y, float z)
        {
            float fx = x / chunkSize * resolution - 0.5f;
            float fy = y / chunkSize * resolution - 0.5f;
            float fz = z / chunkSize * resolution - 0.5f;

            int ix = Mathf.FloorToInt(fx);
            int iy = Mathf.FloorToInt(fy);
            int iz = Mathf.FloorToInt(fz);
            float tx = fx - ix;
            float ty = fy - iy;
            float tz = fz - iz;

            float c000 = SamplePoint(ix, iy, iz);
            float c100 = SamplePoint(ix + 1, iy, iz);
            float c010 = SamplePoint(ix, iy + 1, iz);
            float c110 = SamplePoint(ix + 1, iy + 1, iz);
            float c001 = SamplePoint(ix, iy, iz + 1);
            float c101 = SamplePoint(ix + 1, iy, iz + 1);
            float c011 = SamplePoint(ix, iy + 1, iz + 1);
            float c111 = SamplePoint(ix + 1, iy + 1, iz + 1);

            return Mathf.Lerp(
                Mathf.Lerp(Mathf.Lerp(c000, c100, tx), Mathf.Lerp(c010, c110, tx), ty),
                Mathf.Lerp(Mathf.Lerp(c001, c101, tx), Mathf.Lerp(c011, c111, tx), ty),
                tz);
        }

        public float SamplePoint(int ix, int iy, int iz)
        {
            int cx = FloorDiv(ix, resolution);
            int cy = FloorDiv(iy, resolution);
            int cz = FloorDiv(iz, resolution);

            var key = new Vector3Int(cx, cy, cz);
            if (!entryCache.TryGetValue(key, out VoxelChunkEntry entry))
            {
                store.TryGetEntry(key, out entry);
                entryCache[key] = entry; // cache misses too (as null) — missing chunks stay air
            }

            if (entry == null) return 0f;
            return entry.density[Mod(ix, resolution) +
                                 resolution * (Mod(iy, resolution) + resolution * Mod(iz, resolution))];
        }

        private static int FloorDiv(int value, int divisor) =>
            divisor <= 0 ? value : (value >= 0 ? value / divisor : (value - divisor + 1) / divisor);

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }
}

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Surface Nets mesher (dual contouring without QEF): density field → smooth mesh.
    /// One vertex per crossing cell, one quad per crossing edge. A one-cell skirt is
    /// processed so border quads match the neighbour chunk exactly (each side owns its
    /// copy). Geometry computation is pure and thread-safe; mesh construction must run
    /// on the main thread.
    /// </summary>
    public static class SurfaceNetsMesher
    {
        public const float IsoLevel = 0.5f;

        private static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };
        private static readonly int[] EdgeAxis = { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 };
        private static readonly int[] CornerX = { 0, 1, 0, 1, 0, 1, 0, 1 };
        private static readonly int[] CornerY = { 0, 0, 1, 1, 0, 0, 1, 1 };
        private static readonly int[] CornerZ = { 0, 0, 0, 0, 1, 1, 1, 1 };

        public sealed class MeshGeometry
        {
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public Vector3[] Normals = Array.Empty<Vector3>();
            public Vector2[] Uv = Array.Empty<Vector2>();
            public Color[] Colors;
            public int[] Triangles = Array.Empty<int>();
            public bool IsEmpty => Triangles.Length == 0;
        }

        public sealed class Result
        {
            public Mesh Mesh { get; set; }
            public int VertexCount;
            public int TriangleCount;
        }

        /// <summary>Convenience: compute + build on the calling thread.</summary>
        public static Result Mesh(VoxelWorldSampler sampler, Vector3Int chunkCoord, int resolution,
            float chunkSize, Func<Vector3, Color> vertexColorSampler = null)
        {
            MeshGeometry geometry = ComputeGeometry(sampler, chunkCoord, resolution, chunkSize, vertexColorSampler);
            return BuildMesh(geometry);
        }

        /// <summary>
        /// Pure geometry pass — safe to run on worker threads with a per-thread sampler.
        /// </summary>
        public static MeshGeometry ComputeGeometry(VoxelWorldSampler sampler, Vector3Int chunkCoord,
            int resolution, float chunkSize, Func<Vector3, Color> vertexColorSampler = null)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            const int skirtLow = -1;

            Vector3 origin = new Vector3(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);
            float spacing = chunkSize / resolution;

            var vertices = new List<Vector3>(4096);
            var normals = new List<Vector3>(4096);
            var triangles = new List<int>(8192);
            var cellVertexIndex = new Dictionary<long, int>(1 << 14);

            bool TryGetVertex(int cx, int cy, int cz, out int index) =>
                cellVertexIndex.TryGetValue(Key(cx, cy, cz), out index);

            void EnsureCellVertex(int cx, int cy, int cz)
            {
                long key = Key(cx, cy, cz);
                if (cellVertexIndex.ContainsKey(key)) return;

                float sumX = 0f, sumY = 0f, sumZ = 0f;
                int crossings = 0;

                for (int e = 0; e < 12; e++)
                {
                    float da = DensityAtCorner(cx + CornerX[EdgeA[e]], cy + CornerY[EdgeA[e]], cz + CornerZ[EdgeA[e]]);
                    float db = DensityAtCorner(cx + CornerX[EdgeB[e]], cy + CornerY[EdgeB[e]], cz + CornerZ[EdgeB[e]]);
                    if ((da < 0f) == (db < 0f)) continue;

                    float t = da / (da - db);
                    sumX += CornerX[EdgeA[e]] + (CornerX[EdgeB[e]] - CornerX[EdgeA[e]]) * t;
                    sumY += CornerY[EdgeA[e]] + (CornerY[EdgeB[e]] - CornerY[EdgeA[e]]) * t;
                    sumZ += CornerZ[EdgeA[e]] + (CornerZ[EdgeB[e]] - CornerZ[EdgeA[e]]) * t;
                    crossings++;
                }

                if (crossings == 0)
                {
                    cellVertexIndex[key] = -1;
                    return;
                }

                Vector3 world = origin + spacing *
                    new Vector3(cx + sumX / crossings, cy + sumY / crossings, cz + sumZ / crossings);

                int index = vertices.Count;
                vertices.Add(world);
                normals.Add(DensityGradient(sampler, world));
                cellVertexIndex[key] = index;
            }

            // Pass 1: vertices for every cell in [-1 .. resolution-1]³. Skirt cells handle
            // border quads; cells with 2+ negative axes belong to diagonal chunks and are
            // skipped (their geometry is that chunk's job).
            for (int z = skirtLow; z < resolution; z++)
                for (int y = skirtLow; y < resolution; y++)
                    for (int x = skirtLow; x < resolution; x++)
                    {
                        int negatives = (x == skirtLow ? 1 : 0) + (y == skirtLow ? 1 : 0) + (z == skirtLow ? 1 : 0);
                        if (negatives > 1) continue;
                        EnsureCellVertex(x, y, z);
                    }

            // Pass 2: one quad per crossing edge whose four surrounding cells exist here.
            // Border edges are emitted by BOTH neighbouring chunks with identical geometry,
            // which keeps seams closed without any ownership bookkeeping.
            for (int z = skirtLow; z < resolution; z++)
            {
                for (int y = skirtLow; y < resolution; y++)
                {
                    for (int x = skirtLow; x < resolution; x++)
                    {
                        int negatives = (x == skirtLow ? 1 : 0) + (y == skirtLow ? 1 : 0) + (z == skirtLow ? 1 : 0);
                        if (negatives > 1) continue;

                        for (int e = 0; e < 12; e++)
                        {
                            int axis = EdgeAxis[e];

                            float da = DensityAtCorner(x + CornerX[EdgeA[e]], y + CornerY[EdgeA[e]], z + CornerZ[EdgeA[e]]);
                            float db = DensityAtCorner(x + CornerX[EdgeB[e]], y + CornerY[EdgeB[e]], z + CornerZ[EdgeB[e]]);
                            if ((da < 0f) == (db < 0f)) continue;

                            GetPerpendicular(axis, out int pa, out int pb);
                            int o0x = x, o0y = y, o0z = z;
                            int o1x = x + (pa == 0 ? 1 : 0), o1y = y + (pa == 1 ? 1 : 0), o1z = z + (pa == 2 ? 1 : 0);
                            int o2x = x + (pb == 0 ? 1 : 0), o2y = y + (pb == 1 ? 1 : 0), o2z = z + (pb == 2 ? 1 : 0);
                            int o3x = o1x + (pb == 0 ? 1 : 0), o3y = o1y + (pb == 1 ? 1 : 0), o3z = o1z + (pb == 2 ? 1 : 0);

                            bool ok = TryGetVertex(o0x, o0y, o0z, out int i0) &
                                      TryGetVertex(o1x, o1y, o1z, out int i1) &
                                      TryGetVertex(o2x, o2y, o2z, out int i2) &
                                      TryGetVertex(o3x, o3y, o3z, out int i3);
                            if (!ok || i0 < 0 || i1 < 0 || i2 < 0 || i3 < 0) continue;

                            AddOrientedQuad(i0, i1, i2, i3);
                        }
                    }
                }
            }

            // ---- local functions ----

            float DensityAtCorner(int lx, int ly, int lz) =>
                sampler.SamplePoint(chunkCoord.x * resolution + lx,
                                    chunkCoord.y * resolution + ly,
                                    chunkCoord.z * resolution + lz) - IsoLevel;

            static void GetPerpendicular(int axis, out int pa, out int pb)
            {
                switch (axis)
                {
                    case 0: pa = 1; pb = 2; break;   // edge on X → cells vary on Y,Z
                    case 1: pa = 0; pb = 2; break;   // edge on Y → cells vary on X,Z
                    default: pa = 0; pb = 1; break;  // edge on Z → cells vary on X,Y
                }
            }

            void AddOrientedQuad(int i0, int i1, int i2, int i3)
            {
                Vector3 p0 = vertices[i0], p1 = vertices[i1], p2 = vertices[i2];
                Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 averageNormal = normals[i0] + normals[i1] + normals[i2] + normals[i3];
                bool flipped = Vector3.Dot(faceNormal, averageNormal) < 0f;

                if (!flipped)
                {
                    triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                    triangles.Add(i0); triangles.Add(i2); triangles.Add(i3);
                }
                else
                {
                    triangles.Add(i0); triangles.Add(i2); triangles.Add(i1);
                    triangles.Add(i0); triangles.Add(i3); triangles.Add(i2);
                }
            }

            var uvs = new Vector2[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                uvs[i] = new Vector2(vertices[i].x / chunkSize, vertices[i].z / chunkSize);

            Color[] colors = null;
            if (vertexColorSampler != null)
            {
                colors = new Color[vertices.Count];
                for (int i = 0; i < vertices.Count; i++) colors[i] = vertexColorSampler(vertices[i]);
            }

            return new MeshGeometry
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                Uv = uvs,
                Colors = colors,
                Triangles = triangles.ToArray()
            };
        }

        /// <summary>Main-thread mesh construction from precomputed geometry.</summary>
        public static Result BuildMesh(MeshGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (geometry.IsEmpty) return new Result { Mesh = null, VertexCount = 0, TriangleCount = 0 };

            Mesh mesh = new Mesh
            {
                indexFormat = geometry.Vertices.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = geometry.Vertices,
                normals = geometry.Normals,
                uv = geometry.Uv,
                triangles = geometry.Triangles
            };
            if (geometry.Colors != null && geometry.Colors.Length == geometry.Vertices.Length)
                mesh.colors = geometry.Colors;
            mesh.RecalculateBounds();

            return new Result
            {
                Mesh = mesh,
                VertexCount = geometry.Vertices.Length,
                TriangleCount = geometry.Triangles.Length / 3
            };
        }

        private static Vector3 DensityGradient(VoxelWorldSampler sampler, Vector3 position)
        {
            const float epsilon = 0.01f;
            float dx = sampler.Sample(position.x + epsilon, position.y, position.z) -
                       sampler.Sample(position.x - epsilon, position.y, position.z);
            float dy = sampler.Sample(position.x, position.y + epsilon, position.z) -
                       sampler.Sample(position.x, position.y - epsilon, position.z);
            float dz = sampler.Sample(position.x, position.y, position.z + epsilon) -
                       sampler.Sample(position.x, position.y, position.z - epsilon);
            Vector3 gradient = new Vector3(dx, dy, dz);
            return gradient.sqrMagnitude > 1e-10f ? (-gradient).normalized : Vector3.up;
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x + 4096) << 42) | ((long)(y + 4096) << 21) | (long)(z + 4096);
    }
}
