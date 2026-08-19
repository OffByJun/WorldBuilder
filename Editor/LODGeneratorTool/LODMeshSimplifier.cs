using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.LODGeneratorTool
{
    public static class LODMeshSimplifier
    {
        public static Mesh Simplify(Mesh source, float ratio, string name)
        {
            if (source == null)
            {
                return null;
            }

            ratio = Mathf.Clamp01(ratio);
            if (ratio >= 1f)
            {
                return Object.Instantiate(source);
            }

            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uv = source.uv;
            int[] triangles = source.triangles;

            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUv = uv != null && uv.Length == vertices.Length;

            int targetVerts = Mathf.Max(4, Mathf.RoundToInt(vertices.Length * ratio));
            int gridRes = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(targetVerts, 1f / 3f)));

            Bounds bounds = source.bounds;
            Vector3 size = bounds.size;
            Vector3 cell = new Vector3(
                size.x <= 0f ? 1f : size.x / gridRes,
                size.y <= 0f ? 1f : size.y / gridRes,
                size.z <= 0f ? 1f : size.z / gridRes);

            Dictionary<int, int> cellToIndex = new Dictionary<int, int>();
            int[] vertexToNew = new int[vertices.Length];
            List<Vector3> accumPos = new List<Vector3>();
            List<Vector3> accumNormal = new List<Vector3>();
            List<Vector2> accumUv = new List<Vector2>();
            List<int> counts = new List<int>();

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 local = vertices[i] - bounds.min;
                int cx = Mathf.FloorToInt(local.x / cell.x);
                int cy = Mathf.FloorToInt(local.y / cell.y);
                int cz = Mathf.FloorToInt(local.z / cell.z);
                int cellKey = cx + (gridRes + 1) * (cy + (gridRes + 1) * cz);

                if (!cellToIndex.TryGetValue(cellKey, out int newIndex))
                {
                    newIndex = accumPos.Count;
                    cellToIndex.Add(cellKey, newIndex);
                    accumPos.Add(Vector3.zero);
                    accumNormal.Add(Vector3.zero);
                    accumUv.Add(Vector2.zero);
                    counts.Add(0);
                }

                accumPos[newIndex] += vertices[i];
                if (hasNormals)
                {
                    accumNormal[newIndex] += normals[i];
                }

                if (hasUv)
                {
                    accumUv[newIndex] += uv[i];
                }

                counts[newIndex]++;
                vertexToNew[i] = newIndex;
            }

            Vector3[] newVertices = new Vector3[accumPos.Count];
            Vector2[] newUv = new Vector2[accumPos.Count];
            for (int i = 0; i < accumPos.Count; i++)
            {
                float inv = counts[i] > 0 ? 1f / counts[i] : 1f;
                newVertices[i] = accumPos[i] * inv;
                newUv[i] = accumUv[i] * inv;
            }

            List<int> newTriangles = new List<int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = vertexToNew[triangles[i]];
                int b = vertexToNew[triangles[i + 1]];
                int c = vertexToNew[triangles[i + 2]];

                if (a == b || b == c || a == c)
                {
                    continue;
                }

                newTriangles.Add(a);
                newTriangles.Add(b);
                newTriangles.Add(c);
            }

            Mesh result = new Mesh
            {
                name = name,
                indexFormat = newVertices.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            result.SetVertices(newVertices);
            if (hasUv)
            {
                result.SetUVs(0, newUv);
            }

            result.SetTriangles(newTriangles, 0);
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }
    }
}
