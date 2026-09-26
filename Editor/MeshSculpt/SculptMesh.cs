using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldBuilder.Editor.MeshSculpt
{
    internal enum SculptBrush
    {
        Raise,
        Dig,
        Smooth,
        Flatten,
        Inflate,
        Hole
    }

    internal struct SculptDab
    {
        public Vector3 Center;      // world space
        public Vector3 Direction;   // world space, raise direction
        public float Radius;        // metres
        public float Strength;      // 0..1
        public float Hardness;      // 0..1, share of the radius at full strength
        public float Detail;        // metres; longer edges under the brush are split. 0 = off
        public bool Invert;
        public Vector3 PlanePoint;  // flatten target, world space
        public Vector3 PlaneNormal;
    }

    /// <summary>
    /// Editable working copy of one mesh. Vertices that share a position (UV seams, hard edges) are
    /// welded logically — they always move together and splits happen on both sides — so sculpting and
    /// subdividing never open cracks, while each side keeps its own UVs.
    /// </summary>
    internal sealed class SculptMesh
    {
        private const int MaxVertices = 400000;

        public readonly MeshFilter Filter;
        public readonly Mesh Mesh;

        private readonly Transform transform;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uv0 = new List<Vector2>();
        private readonly List<Vector2> uv1 = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();      // 3 per triangle; -1 = removed
        private readonly List<int> triangleSubmesh = new List<int>();
        private readonly List<int> weld = new List<int>();           // vertex -> weld id
        private readonly List<List<int>> weldVertices = new List<List<int>>();
        private readonly Dictionary<long, List<int>> edgeTriangles = new Dictionary<long, List<int>>();
        private readonly int submeshCount;
        private readonly bool hasTangents;
        private int uploadedVertexCount;

        public int VertexCount => vertices.Count;

        public SculptMesh(MeshFilter filter)
        {
            Filter = filter;
            Mesh = filter.sharedMesh;
            transform = filter.transform;

            Mesh.GetVertices(vertices);
            Mesh.GetNormals(normals);
            Mesh.GetUVs(0, uv0);
            Mesh.GetUVs(1, uv1);
            Mesh.GetColors(colors);
            hasTangents = Mesh.HasVertexAttribute(VertexAttribute.Tangent);
            submeshCount = Mesh.subMeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                int[] indices = Mesh.GetTriangles(s);
                triangles.AddRange(indices);
                for (int t = 0; t < indices.Length / 3; t++) triangleSubmesh.Add(s);
            }

            BuildWeld();
            for (int t = 0; t < triangleSubmesh.Count; t++) LinkTriangle(t);
            uploadedVertexCount = vertices.Count;
        }

        // ------------------------------------------------------------------ topology

        private void BuildWeld()
        {
            float epsilon = Mathf.Max(Mesh.bounds.size.magnitude * 1e-6f, 1e-9f);
            var lookup = new Dictionary<Vector3Int, int>();
            for (int v = 0; v < vertices.Count; v++)
            {
                Vector3 p = vertices[v] / epsilon;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!lookup.TryGetValue(key, out int id))
                {
                    id = weldVertices.Count;
                    lookup[key] = id;
                    weldVertices.Add(new List<int>());
                }

                weld.Add(id);
                weldVertices[id].Add(v);
            }
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private void LinkTriangle(int t)
        {
            int i = t * 3;
            if (triangles[i] < 0) return;
            for (int c = 0; c < 3; c++)
            {
                long key = EdgeKey(weld[triangles[i + c]], weld[triangles[i + (c + 1) % 3]]);
                if (!edgeTriangles.TryGetValue(key, out List<int> list)) edgeTriangles[key] = list = new List<int>(2);
                list.Add(t);
            }
        }

        private void UnlinkTriangle(int t)
        {
            int i = t * 3;
            if (triangles[i] < 0) return;
            for (int c = 0; c < 3; c++)
            {
                long key = EdgeKey(weld[triangles[i + c]], weld[triangles[i + (c + 1) % 3]]);
                if (edgeTriangles.TryGetValue(key, out List<int> list))
                {
                    list.Remove(t);
                    if (list.Count == 0) edgeTriangles.Remove(key);
                }
            }
        }

        private Vector3 WeldPosition(int id) => vertices[weldVertices[id][0]];

        private int AddVertex(int a, int b, int weldId)
        {
            int v = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            normals.Add(((normals.Count > a ? normals[a] : Vector3.up) + (normals.Count > b ? normals[b] : Vector3.up)).normalized);
            if (uv0.Count > 0) uv0.Add((uv0[a] + uv0[b]) * 0.5f);
            if (uv1.Count > 0) uv1.Add((uv1[a] + uv1[b]) * 0.5f);
            if (colors.Count > 0) colors.Add(Color32.Lerp(colors[a], colors[b], 0.5f));
            weld.Add(weldId);
            weldVertices[weldId].Add(v);
            return v;
        }

        /// <summary>Splits one welded edge in every triangle that uses it, so neighbours stay conforming.</summary>
        private void SplitEdge(long key)
        {
            if (!edgeTriangles.TryGetValue(key, out List<int> users)) return;
            int weldA = (int)(key >> 32), weldB = (int)(key & 0xffffffff);
            int middle = weldVertices.Count;
            weldVertices.Add(new List<int>());
            var midpoints = new Dictionary<long, int>();

            foreach (int t in users.ToArray())
            {
                int i = t * 3;
                int ca = -1, cb = -1;
                for (int c = 0; c < 3; c++)
                {
                    int w = weld[triangles[i + c]];
                    if (w == weldA) ca = c;
                    else if (w == weldB) cb = c;
                }

                if (ca < 0 || cb < 0) continue;
                int va = triangles[i + ca], vb = triangles[i + cb];
                long pair = EdgeKey(va, vb);
                if (!midpoints.TryGetValue(pair, out int vm)) midpoints[pair] = vm = AddVertex(va, vb, middle);

                // Replacing one corner at a time keeps the winding of both halves.
                UnlinkTriangle(t);
                int added = triangleSubmesh.Count;
                triangles.Add(triangles[i]);
                triangles.Add(triangles[i + 1]);
                triangles.Add(triangles[i + 2]);
                triangleSubmesh.Add(triangleSubmesh[t]);
                triangles[i + cb] = vm;
                triangles[added * 3 + ca] = vm;
                LinkTriangle(t);
                LinkTriangle(added);
            }
        }

        /// <summary>Longest-edge bisection under the brush until no edge there is longer than maxEdge.</summary>
        private void Subdivide(Vector3 center, float radius, float maxEdge)
        {
            float maxEdgeSqr = maxEdge * maxEdge;
            var edges = new List<(long key, float length)>();
            var seen = new HashSet<long>();
            for (int pass = 0; pass < 12 && vertices.Count < MaxVertices; pass++)
            {
                edges.Clear();
                seen.Clear();
                int count = triangleSubmesh.Count;
                for (int t = 0; t < count; t++)
                {
                    int i = t * 3;
                    if (triangles[i] < 0) continue;
                    int w0 = weld[triangles[i]], w1 = weld[triangles[i + 1]], w2 = weld[triangles[i + 2]];
                    Vector3 p0 = WeldPosition(w0), p1 = WeldPosition(w1), p2 = WeldPosition(w2);
                    float e01 = (p1 - p0).sqrMagnitude, e12 = (p2 - p1).sqrMagnitude, e20 = (p0 - p2).sqrMagnitude;
                    float longest = Mathf.Max(e01, Mathf.Max(e12, e20));
                    if (longest <= maxEdgeSqr) continue;
                    Vector3 centroid = (p0 + p1 + p2) / 3f;
                    float reach = radius + Mathf.Sqrt(longest);
                    if ((centroid - center).sqrMagnitude > reach * reach) continue;

                    long key = longest == e01 ? EdgeKey(w0, w1) : longest == e12 ? EdgeKey(w1, w2) : EdgeKey(w2, w0);
                    if (seen.Add(key)) edges.Add((key, longest));
                }

                if (edges.Count == 0) return;
                edges.Sort((a, b) => b.length.CompareTo(a.length));
                foreach ((long key, float _) in edges)
                {
                    SplitEdge(key);
                    if (vertices.Count >= MaxVertices) return;
                }
            }
        }

        // ------------------------------------------------------------------ brushes

        /// <summary>Applies one dab. Returns true when the mesh changed.</summary>
        public bool Apply(SculptBrush brush, SculptDab dab)
        {
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            float scale = Mathf.Max(1e-6f, transform.lossyScale.x);
            Vector3 center = toLocal.MultiplyPoint3x4(dab.Center);
            float radius = dab.Radius / scale;

            if (brush == SculptBrush.Hole) return Punch(center, radius);
            if (dab.Detail > 0f) Subdivide(center, radius, dab.Detail / scale);

            // Welded groups inside the brush and how strongly each is affected.
            var affected = new List<(int id, float weight)>();
            float inner = radius * Mathf.Clamp01(dab.Hardness) * 0.95f;
            for (int id = 0; id < weldVertices.Count; id++)
            {
                if (weldVertices[id].Count == 0) continue;
                float distance = (WeldPosition(id) - center).magnitude;
                if (distance >= radius) continue;
                float t = distance <= inner ? 1f : 1f - (distance - inner) / (radius - inner);
                float weight = t * t * (3f - 2f * t);
                if (brush == SculptBrush.Dig) weight = Mathf.Pow(weight, 2.2f);
                affected.Add((id, weight));
            }

            if (affected.Count == 0) return false;

            float amount = dab.Strength * dab.Radius * 0.08f;
            Vector3 direction = toLocal.MultiplyVector(dab.Direction).normalized;
            float sign = dab.Invert ? -1f : 1f;

            switch (brush)
            {
                case SculptBrush.Raise:
                    foreach ((int id, float w) in affected) MoveWeld(id, direction * (sign * amount * w / scale));
                    break;
                case SculptBrush.Dig:
                    foreach ((int id, float w) in affected) MoveWeld(id, direction * (-sign * amount * 2.2f * w / scale));
                    break;
                case SculptBrush.Inflate:
                    RecalculateNormals();
                    foreach ((int id, float w) in affected)
                        MoveWeld(id, normals[weldVertices[id][0]] * (sign * amount * w / scale));
                    break;
                case SculptBrush.Flatten:
                {
                    Vector3 planePoint = toLocal.MultiplyPoint3x4(dab.PlanePoint);
                    Vector3 planeNormal = toLocal.MultiplyVector(dab.PlaneNormal).normalized;
                    float pull = Mathf.Clamp01(dab.Strength * 0.6f);
                    foreach ((int id, float w) in affected)
                    {
                        float height = Vector3.Dot(WeldPosition(id) - planePoint, planeNormal);
                        MoveWeld(id, -planeNormal * (height * pull * w));
                    }

                    break;
                }
                default:
                    Smooth(affected, Mathf.Clamp01(dab.Strength * 0.7f));
                    break;
            }

            RecalculateNormals();
            return true;
        }

        private void MoveWeld(int id, Vector3 offset)
        {
            foreach (int v in weldVertices[id]) vertices[v] += offset;
        }

        private void Smooth(List<(int id, float weight)> affected, float strength)
        {
            var inBrush = new Dictionary<int, float>();
            foreach ((int id, float w) in affected) inBrush[id] = w;
            var sums = new Dictionary<int, Vector3>();
            var counts = new Dictionary<int, int>();
            for (int t = 0; t < triangleSubmesh.Count; t++)
            {
                int i = t * 3;
                if (triangles[i] < 0) continue;
                for (int c = 0; c < 3; c++)
                {
                    int a = weld[triangles[i + c]];
                    if (!inBrush.ContainsKey(a)) continue;
                    for (int o = 1; o < 3; o++)
                    {
                        int b = weld[triangles[i + (c + o) % 3]];
                        sums[a] = (sums.TryGetValue(a, out Vector3 s) ? s : Vector3.zero) + WeldPosition(b);
                        counts[a] = (counts.TryGetValue(a, out int n) ? n : 0) + 1;
                    }
                }
            }

            var targets = new List<(int id, Vector3 offset)>();
            foreach ((int id, float w) in affected)
            {
                if (!counts.TryGetValue(id, out int n) || n == 0) continue;
                Vector3 average = sums[id] / n;
                targets.Add((id, (average - WeldPosition(id)) * (strength * w)));
            }

            foreach ((int id, Vector3 offset) in targets) MoveWeld(id, offset);
        }

        private bool Punch(Vector3 center, float radius)
        {
            float radiusSqr = radius * radius;
            bool changed = false;
            for (int t = 0; t < triangleSubmesh.Count; t++)
            {
                int i = t * 3;
                if (triangles[i] < 0) continue;
                Vector3 centroid = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                if ((centroid - center).sqrMagnitude > radiusSqr) continue;
                UnlinkTriangle(t);
                triangles[i] = triangles[i + 1] = triangles[i + 2] = -1;
                changed = true;
            }

            return changed;
        }

        /// <summary>Smooth normals shared across welded vertices so seams do not show as shading lines.</summary>
        private void RecalculateNormals()
        {
            var sums = new Vector3[weldVertices.Count];
            for (int t = 0; t < triangleSubmesh.Count; t++)
            {
                int i = t * 3;
                if (triangles[i] < 0) continue;
                Vector3 p0 = vertices[triangles[i]], p1 = vertices[triangles[i + 1]], p2 = vertices[triangles[i + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                sums[weld[triangles[i]]] += n;
                sums[weld[triangles[i + 1]]] += n;
                sums[weld[triangles[i + 2]]] += n;
            }

            while (normals.Count < vertices.Count) normals.Add(Vector3.up);
            for (int v = 0; v < vertices.Count; v++)
            {
                Vector3 n = sums[weld[v]];
                normals[v] = n.sqrMagnitude > 1e-20f ? n.normalized : normals[v];
            }
        }

        // ------------------------------------------------------------------ upload

        public void Upload(bool final)
        {
            if (vertices.Count > 65535) Mesh.indexFormat = IndexFormat.UInt32;
            var perSubmesh = new List<int>[submeshCount];
            for (int s = 0; s < submeshCount; s++) perSubmesh[s] = new List<int>();
            for (int t = 0; t < triangleSubmesh.Count; t++)
            {
                int i = t * 3;
                if (triangles[i] < 0) continue;
                List<int> list = perSubmesh[triangleSubmesh[t]];
                list.Add(triangles[i]);
                list.Add(triangles[i + 1]);
                list.Add(triangles[i + 2]);
            }

            Mesh.SetVertices(vertices);
            Mesh.SetNormals(normals);
            if (uv0.Count == vertices.Count) Mesh.SetUVs(0, uv0);
            if (uv1.Count == vertices.Count) Mesh.SetUVs(1, uv1);
            if (colors.Count == vertices.Count) Mesh.SetColors(colors);
            Mesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++) Mesh.SetTriangles(perSubmesh[s], s, false);
            Mesh.RecalculateBounds();
            // A changed vertex count drops the old tangents, so they are rebuilt then and at stroke end.
            bool resized = vertices.Count != uploadedVertexCount;
            if (hasTangents && uv0.Count == vertices.Count && (final || resized)) Mesh.RecalculateTangents();
            uploadedVertexCount = vertices.Count;
        }
    }
}
