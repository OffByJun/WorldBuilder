using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Voxelizes an arbitrary mesh (e.g. a Blender cave network) into the density store by
    /// stamping overlapping negative spheres along its surface — tunnels become carved
    /// pass-throughs whose walls follow the source geometry. Deterministic; authoring-time
    /// cost scales with triangle count × thickness.
    /// </summary>
    public static class MeshCarver
    {
        /// <summary>
        /// Carves along every triangle of <paramref name="mesh"/> transformed by
        /// <paramref name="localToWorld"/>. Points beyond <paramref name="yRange"/> are
        /// skipped to avoid stamping empty space far from the world.
        /// </summary>
        /// <param name="thickness">Carve diameter in meters (cave wall thickness).</param>
        /// <returns>Number of voxels changed.</returns>
        public static int CarveAlongSurface(VoxelStoreAsset store, float chunkSize, Mesh mesh,
            Matrix4x4 localToWorld, float thickness, Vector2 yRange)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
                return 0;

            float radius = Mathf.Max(0.15f, thickness * 0.5f);
            float spacing = radius * 0.6f;
            int changed = 0;

            for (int tri = 0; tri + 2 < triangles.Length; tri += 3)
            {
                Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[tri]]);
                Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[tri + 1]]);
                Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[tri + 2]]);

                Bounds triangle = new Bounds(a, Vector3.zero);
                triangle.Encapsulate(b);
                triangle.Encapsulate(c);
                if (triangle.max.y < yRange.x || triangle.min.y > yRange.y) continue;

                float longest = Mathf.Max(Mathf.Max(Vector3.Distance(a, b), Vector3.Distance(b, c)),
                    Vector3.Distance(a, c));
                int steps = Mathf.CeilToInt(longest / spacing);
                for (int i = 0; i <= steps; i++)
                {
                    float t01 = steps > 0 ? (float)i / steps : 0f;
                    Vector3 ab = Vector3.Lerp(a, b, t01);
                    Vector3 bc = Vector3.Lerp(b, c, t01);
                    // Two interleaved edge sweeps give dense coverage without exact barycentric grids.
                    changed += TerrainDeformer.StampSphere(store, chunkSize, ab, radius, -1.5f);

                    int innerSteps = Mathf.CeilToInt(Vector3.Distance(ab, bc) / spacing);
                    for (int j = 1; j < innerSteps; j++)
                    {
                        changed += TerrainDeformer.StampSphere(store, chunkSize,
                            Vector3.Lerp(ab, bc, (float)j / innerSteps), radius, -1.5f);
                    }
                }
            }
            return changed;
        }
    }
}
