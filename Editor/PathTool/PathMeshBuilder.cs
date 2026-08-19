using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PathTool
{
    public static class PathMeshBuilder
    {
        public static Mesh Build(IReadOnlyList<Vector3> controlPoints, float width, int segments)
        {
            Mesh mesh = new Mesh();

            if (controlPoints == null || controlPoints.Count < 2 || segments < 1)
            {
                return mesh;
            }

            int sampleCount = segments + 1;
            List<Vector3> centers = new List<Vector3>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / segments;
                centers.Add(Sample(controlPoints, t));
            }

            Vector3[] vertices = new Vector3[sampleCount * 2];
            Vector2[] uv = new Vector2[sampleCount * 2];
            float half = width * 0.5f;

            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 tangent = Tangent(centers, i);
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                if (side.sqrMagnitude < 0.0001f)
                {
                    side = Vector3.right;
                }

                float v = (float)i / segments;
                vertices[i * 2] = centers[i] - side * half;
                vertices[i * 2 + 1] = centers[i] + side * half;
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);
            }

            int[] triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int baseIndex = i * 2;
                int t = i * 6;
                triangles[t] = baseIndex;
                triangles[t + 1] = baseIndex + 2;
                triangles[t + 2] = baseIndex + 1;
                triangles[t + 3] = baseIndex + 1;
                triangles[t + 4] = baseIndex + 2;
                triangles[t + 5] = baseIndex + 3;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Sample(IReadOnlyList<Vector3> points, float t)
        {
            int spans = points.Count - 1;
            float scaled = Mathf.Clamp01(t) * spans;
            int index = Mathf.Min((int)scaled, spans - 1);
            float local = scaled - index;

            Vector3 p0 = points[Mathf.Max(index - 1, 0)];
            Vector3 p1 = points[index];
            Vector3 p2 = points[index + 1];
            Vector3 p3 = points[Mathf.Min(index + 2, points.Count - 1)];

            return CatmullRom(p0, p1, p2, p3, local);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Vector3 Tangent(List<Vector3> centers, int i)
        {
            if (i == 0)
            {
                return (centers[1] - centers[0]).normalized;
            }

            if (i == centers.Count - 1)
            {
                return (centers[i] - centers[i - 1]).normalized;
            }

            return (centers[i + 1] - centers[i - 1]).normalized;
        }
    }
}
