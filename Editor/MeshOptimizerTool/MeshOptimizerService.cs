using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.MeshOptimizerTool
{
    public struct MeshOptimizeOptions
    {
        public bool removeDuplicates;
        public bool removeUnused;
        public bool cleanUv;
    }

    public struct MeshOptimizeResult
    {
        public int beforeVertices;
        public int afterVertices;
    }

    public static class MeshOptimizerService
    {
        public static MeshOptimizeResult Optimize(Mesh mesh, MeshOptimizeOptions options)
        {
            MeshOptimizeResult result = new MeshOptimizeResult();
            if (mesh == null)
            {
                return result;
            }

            result.beforeVertices = mesh.vertexCount;

            Undo.RecordObject(mesh, "Optimize Mesh");

            if (options.removeDuplicates)
            {
                Rebuild(mesh, BuildWeldRemap(mesh));
            }

            if (options.removeUnused)
            {
                Rebuild(mesh, BuildUnusedRemap(mesh));
            }

            if (options.cleanUv)
            {
                mesh.uv2 = null;
                mesh.uv3 = null;
                mesh.uv4 = null;
            }

            MeshUtility.Optimize(mesh);
            mesh.RecalculateBounds();

            result.afterVertices = mesh.vertexCount;

            EditorUtility.SetDirty(mesh);
            return result;
        }

        private static int[] BuildWeldRemap(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;

            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUv = uv != null && uv.Length == vertices.Length;

            Dictionary<string, int> map = new Dictionary<string, int>();
            int[] remap = new int[vertices.Length];
            int next = 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                string key = Key(vertices[i], hasNormals ? normals[i] : Vector3.zero, hasUv ? uv[i] : Vector2.zero);
                if (!map.TryGetValue(key, out int index))
                {
                    index = next;
                    next++;
                    map.Add(key, index);
                }

                remap[i] = index;
            }

            return remap;
        }

        private static int[] BuildUnusedRemap(Mesh mesh)
        {
            int vertexCount = mesh.vertexCount;
            bool[] used = new bool[vertexCount];

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int i = 0; i < triangles.Length; i++)
                {
                    used[triangles[i]] = true;
                }
            }

            int[] remap = new int[vertexCount];
            int next = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                if (used[i])
                {
                    remap[i] = next;
                    next++;
                }
                else
                {
                    remap[i] = -1;
                }
            }

            return remap;
        }

        private static void Rebuild(Mesh mesh, int[] remap)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;

            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUv = uv != null && uv.Length == vertices.Length;

            int newCount = 0;
            for (int i = 0; i < remap.Length; i++)
            {
                if (remap[i] >= newCount)
                {
                    newCount = remap[i] + 1;
                }
            }

            Vector3[] newVertices = new Vector3[newCount];
            Vector3[] newNormals = hasNormals ? new Vector3[newCount] : null;
            Vector2[] newUv = hasUv ? new Vector2[newCount] : null;

            for (int i = 0; i < remap.Length; i++)
            {
                int index = remap[i];
                if (index < 0)
                {
                    continue;
                }

                newVertices[index] = vertices[i];
                if (hasNormals)
                {
                    newNormals[index] = normals[i];
                }

                if (hasUv)
                {
                    newUv[index] = uv[i];
                }
            }

            int subMeshCount = mesh.subMeshCount;
            List<int[]> submeshes = new List<int[]>(subMeshCount);
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = remap[triangles[i]];
                }

                submeshes.Add(triangles);
            }

            mesh.Clear();
            mesh.vertices = newVertices;
            if (hasNormals)
            {
                mesh.normals = newNormals;
            }

            if (hasUv)
            {
                mesh.uv = newUv;
            }

            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                mesh.SetTriangles(submeshes[s], s);
            }
        }

        private static string Key(Vector3 position, Vector3 normal, Vector2 uv)
        {
            return position.x.ToString("F4") + "_" + position.y.ToString("F4") + "_" + position.z.ToString("F4") + "_" +
                   normal.x.ToString("F3") + "_" + normal.y.ToString("F3") + "_" + normal.z.ToString("F3") + "_" +
                   uv.x.ToString("F4") + "_" + uv.y.ToString("F4");
        }
    }
}
