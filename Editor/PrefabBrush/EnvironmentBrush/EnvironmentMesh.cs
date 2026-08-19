using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    internal sealed class MeshAccumulator
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
        public readonly List<Vector2> Uv = new List<Vector2>();
        public readonly List<Vector2> Uv2 = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();

        public int VertexCount => Vertices.Count;

        public int Add(Vector3 position, Vector2 uv, Color color)
        {
            return Add(position, uv, Vector2.zero, color);
        }

        public int Add(Vector3 position, Vector2 uv, Vector2 uv2, Color color)
        {
            int index = Vertices.Count;
            Vertices.Add(position);
            Uv.Add(uv);
            Uv2.Add(uv2);
            Colors.Add(color);
            return index;
        }

        public void Triangle(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name };
            if (Vertices.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(Vertices);
            mesh.SetTriangles(Triangles, 0);
            mesh.SetUVs(0, Uv);
            mesh.SetUVs(1, Uv2);
            mesh.SetColors(Colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }

    internal static class EnvironmentNoise
    {
        public static float Fbm(Vector3 position, float frequency, int octaves, bool layered)
        {
            float sum = 0f;
            float amplitude = 1f;
            float totalAmplitude = 0f;
            float f = Mathf.Max(0.0001f, frequency);

            int count = Mathf.Max(1, octaves);
            for (int i = 0; i < count; i++)
            {
                sum += Sample(position * f) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= layered ? 0.5f : 0.65f;
                f *= 2f;
            }

            return totalAmplitude > 0f ? sum / totalAmplitude : 0f;
        }

        public static float Sample(Vector3 p)
        {
            float xy = Mathf.PerlinNoise(p.x, p.y);
            float yz = Mathf.PerlinNoise(p.y, p.z);
            float zx = Mathf.PerlinNoise(p.z, p.x);
            float yx = Mathf.PerlinNoise(p.y + 31.7f, p.x - 11.3f);
            float zy = Mathf.PerlinNoise(p.z + 5.1f, p.y + 19.9f);
            float xz = Mathf.PerlinNoise(p.x - 7.7f, p.z + 3.3f);
            return (xy + yz + zx + yx + zy + xz) / 6f;
        }
    }

    internal static class EnvironmentTube
    {
        public static void Build(MeshAccumulator mesh, IReadOnlyList<Vector3> spine, IReadOnlyList<float> radii, IReadOnlyList<float> twists, int radialSegments, Color rootColor, Color tipColor, float vStart, float vEnd)
        {
            if (spine.Count < 2)
            {
                return;
            }

            int rings = spine.Count;
            int sides = Mathf.Max(3, radialSegments);
            int firstRing = mesh.VertexCount;

            Vector3 previousTangent = (spine[1] - spine[0]).normalized;
            Vector3 normalRef = Vector3.Cross(previousTangent, Vector3.up);
            if (normalRef.sqrMagnitude < 0.0001f)
            {
                normalRef = Vector3.Cross(previousTangent, Vector3.right);
            }
            normalRef.Normalize();

            for (int i = 0; i < rings; i++)
            {
                Vector3 tangent = Tangent(spine, i);
                normalRef = Vector3.ProjectOnPlane(normalRef, tangent).normalized;
                if (normalRef.sqrMagnitude < 0.0001f)
                {
                    normalRef = Vector3.Cross(tangent, Vector3.right).normalized;
                }
                Vector3 binormal = Vector3.Cross(tangent, normalRef).normalized;

                float t = (float)i / (rings - 1);
                float radius = radii[i];
                float twist = twists != null ? twists[i] : 0f;
                Color color = Color.Lerp(rootColor, tipColor, t);
                float v = Mathf.Lerp(vStart, vEnd, t);

                for (int s = 0; s < sides; s++)
                {
                    float angle = (float)s / sides * Mathf.PI * 2f + twist;
                    Vector3 offset = (Mathf.Cos(angle) * normalRef + Mathf.Sin(angle) * binormal) * radius;
                    Vector2 uv = new Vector2((float)s / sides, v);
                    mesh.Add(spine[i] + offset, uv, color);
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                int ringA = firstRing + i * sides;
                int ringB = firstRing + (i + 1) * sides;
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    mesh.Quad(ringA + s, ringA + next, ringB + next, ringB + s);
                }
            }
        }

        public static void Cap(MeshAccumulator mesh, Vector3 center, Color color, Vector2 uv, int ringStart, int sides, bool flip)
        {
            int c = mesh.Add(center, uv, color);
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                if (flip)
                {
                    mesh.Triangle(c, ringStart + next, ringStart + s);
                }
                else
                {
                    mesh.Triangle(c, ringStart + s, ringStart + next);
                }
            }
        }

        private static Vector3 Tangent(IReadOnlyList<Vector3> spine, int i)
        {
            if (i == 0)
            {
                return (spine[1] - spine[0]).normalized;
            }

            if (i == spine.Count - 1)
            {
                return (spine[i] - spine[i - 1]).normalized;
            }

            return (spine[i + 1] - spine[i - 1]).normalized;
        }
    }
}
