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
        Hole,
        Grab,       // 누른 자리를 잡고 끄는 대로 옮긴다(손을 뗄 때까지 같은 정점)
        SnakeHook,  // 끄는 대로 계속 늘려 뽑는다(브러시 아래를 계속 잘게 나눈다)
        Pinch,      // 브러시 가운데로 모아 능선·골을 날카롭게
        Crease,     // 모으면서 올려 능선을 긋는다(반대로는 골)
        Noise,      // 울퉁불퉁한 바위·모래 결
        Terrace     // 일정한 높이마다 계단
    }

    /// <summary>브러시 가운데에서 가장자리로 갈 때 세기가 줄어드는 모양.</summary>
    internal enum SculptFalloff
    {
        Smooth,
        Sphere,
        Sharp,
        Linear,
        Constant
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
        public SculptFalloff Falloff;
        public Vector3 Pull;        // snake hook: how far the cursor moved since the last dab, world space
        public float NoiseScale;    // metres per noise bump
        public float TerraceStep;   // metres per step
    }

    /// <summary>
    /// Editable working copy of one mesh. Vertices that share a position (UV seams, hard edges) are
    /// welded logically — they always move together and splits happen on both sides — so sculpting and
    /// subdividing never open cracks, while each side keeps its own UVs.
    /// <para>
    /// 한 번 찍을 때 메시 전체를 훑지 않는다. 삼각형을 칸 격자(로컬 공간)에 넣어 두고 브러시 근처 칸만 보며,
    /// 이웃(법선·부드럽게·모서리 나누기)은 용접 정점 → 삼각형 목록으로 찾는다. 움직인 정점의 삼각형만 칸을 고쳐 넣고
    /// 그 주변 법선만 다시 계산한다. 업로드는 모양이 바뀌지 않았으면 위치·법선만 보낸다.
    /// </para>
    /// </summary>
    internal sealed class SculptMesh
    {
        private const int MaxVertices = 400000;
        private const MeshUpdateFlags QuickUpload =
            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds;

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
        private readonly List<List<int>> weldTriangles = new List<List<int>>();
        private readonly int submeshCount;
        private readonly bool hasTangents;
        private readonly List<List<(int id, float weight, Vector3 origin)>> grabs = new List<List<(int, float, Vector3)>>();
        private int uploadedVertexCount;
        private bool topologyDirty;
        private Bounds bounds;   // 로컬. 움직인 정점으로 넓혀 두었다가 빠른 업로드 때 그대로 넣는다.

        // 삼각형 칸 격자: 칸 → 그 칸에 걸친 삼각형. 삼각형마다 걸친 칸 범위를 기억해 옮길 때 뺀다.
        private readonly float cellSize;
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private readonly List<Vector3Int> triangleCellMin = new List<Vector3Int>();
        private readonly List<Vector3Int> triangleCellMax = new List<Vector3Int>();

        // 한 번 찍기 안에서 겹치지 않게 세는 표시. 숫자가 바뀌면 지난 표시는 모두 무효다.
        private int mark;
        private readonly List<int> weldMark = new List<int>();
        private readonly List<int> triangleMark = new List<int>();

        // 이번 찍기에서 움직였거나 새로 생긴 용접 정점. 끝날 때 그 삼각형의 칸과 주변 법선을 고친다.
        private readonly HashSet<int> moved = new HashSet<int>();

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
            while (normals.Count < vertices.Count) normals.Add(Vector3.up);
            hasTangents = Mesh.HasVertexAttribute(VertexAttribute.Tangent);
            submeshCount = Mesh.subMeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                int[] indices = Mesh.GetTriangles(s);
                triangles.AddRange(indices);
                for (int t = 0; t < indices.Length / 3; t++) triangleSubmesh.Add(s);
            }

            BuildWeld();
            cellSize = ChooseCellSize();
            for (int t = 0; t < triangleSubmesh.Count; t++)
            {
                triangleCellMin.Add(default);
                triangleCellMax.Add(default);
                triangleMark.Add(0);
                LinkTriangle(t);
            }

            uploadedVertexCount = vertices.Count;
            bounds = Mesh.bounds;
        }

        // ------------------------------------------------------------------ topology

        private void BuildWeld()
        {
            float epsilon = Mathf.Max(Mesh.bounds.size.magnitude * 1e-6f, 1e-9f);
            var lookup = new Dictionary<Vector3Int, int>(vertices.Count);
            weld.Capacity = vertices.Count;
            for (int v = 0; v < vertices.Count; v++)
            {
                Vector3 p = vertices[v] / epsilon;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!lookup.TryGetValue(key, out int id))
                {
                    id = NewWeld();
                    lookup[key] = id;
                }

                weld.Add(id);
                weldVertices[id].Add(v);
            }
        }

        private int NewWeld()
        {
            weldVertices.Add(new List<int>(2));
            weldTriangles.Add(new List<int>(6));
            weldMark.Add(0);
            return weldVertices.Count - 1;
        }

        /// <summary>칸 크기: 보통 모서리의 몇 배. 너무 작으면 큰 삼각형이 칸을 많이 차지하고, 너무 크면 칸마다 삼각형이 많다.</summary>
        private float ChooseCellSize()
        {
            var lengths = new List<float>();
            int step = Mathf.Max(1, triangleSubmesh.Count / 4000);
            for (int t = 0; t < triangleSubmesh.Count; t += step)
            {
                int i = t * 3;
                if (triangles[i] < 0) continue;
                lengths.Add((vertices[triangles[i]] - vertices[triangles[i + 1]]).magnitude);
            }

            float diagonal = Mathf.Max(Mesh.bounds.size.magnitude, 1e-4f);
            if (lengths.Count == 0) return diagonal / 64f;
            lengths.Sort();
            return Mathf.Clamp(lengths[lengths.Count / 2] * 4f, diagonal / 4096f, diagonal / 8f);
        }

        private Vector3Int Cell(Vector3 p) =>
            new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));

        private static long CellKey(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

        private void LinkTriangle(int t)
        {
            int i = t * 3;
            if (triangles[i] < 0) return;
            for (int c = 0; c < 3; c++)
            {
                List<int> list = weldTriangles[weld[triangles[i + c]]];
                if (!list.Contains(t)) list.Add(t);
            }

            Bin(t);
        }

        private void UnlinkTriangle(int t)
        {
            int i = t * 3;
            if (triangles[i] < 0) return;
            for (int c = 0; c < 3; c++) weldTriangles[weld[triangles[i + c]]].Remove(t);
            Unbin(t);
        }

        private void TriangleCells(int t, out Vector3Int min, out Vector3Int max)
        {
            int i = t * 3;
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            min = Cell(Vector3.Min(a, Vector3.Min(b, c)));
            max = Cell(Vector3.Max(a, Vector3.Max(b, c)));
        }

        private void Bin(int t)
        {
            TriangleCells(t, out Vector3Int min, out Vector3Int max);
            triangleCellMin[t] = min;
            triangleCellMax[t] = max;
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                long key = CellKey(x, y, z);
                if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>(8);
                list.Add(t);
            }
        }

        private void Unbin(int t)
        {
            Vector3Int min = triangleCellMin[t], max = triangleCellMax[t];
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
                if (cells.TryGetValue(CellKey(x, y, z), out List<int> list)) list.Remove(t);
        }

        /// <summary>움직인 삼각형이 다른 칸으로 넘어갔으면 칸을 고친다.</summary>
        private void Rebin(int t)
        {
            if (triangles[t * 3] < 0) return;
            TriangleCells(t, out Vector3Int min, out Vector3Int max);
            if (min == triangleCellMin[t] && max == triangleCellMax[t]) return;
            Unbin(t);
            Bin(t);
        }

        /// <summary>상자 [<paramref name="center"/> ± <paramref name="reach"/>]의 칸에 걸친 살아 있는 삼각형.</summary>
        private List<int> TrianglesNear(Vector3 center, float reach)
        {
            var result = new List<int>();
            int current = ++mark;
            Vector3Int min = Cell(center - Vector3.one * reach), max = Cell(center + Vector3.one * reach);
            long span = (long)(max.x - min.x + 1) * (max.y - min.y + 1) * (max.z - min.z + 1);
            if (span > cells.Count)
            {
                // 브러시가 메시보다 크면 칸을 하나씩 보느니 살아 있는 삼각형을 다 본다.
                for (int t = 0; t < triangleSubmesh.Count; t++)
                    if (triangles[t * 3] >= 0) result.Add(t);
                return result;
            }

            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                if (!cells.TryGetValue(CellKey(x, y, z), out List<int> list)) continue;
                foreach (int t in list)
                {
                    if (triangleMark[t] == current) continue;
                    triangleMark[t] = current;
                    result.Add(t);
                }
            }

            return result;
        }

        private Vector3 WeldPosition(int id) => vertices[weldVertices[id][0]];

        private int AddVertex(int a, int b, int weldId)
        {
            int v = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            normals.Add((normals[a] + normals[b]).normalized);
            if (uv0.Count > 0) uv0.Add((uv0[a] + uv0[b]) * 0.5f);
            if (uv1.Count > 0) uv1.Add((uv1[a] + uv1[b]) * 0.5f);
            if (colors.Count > 0) colors.Add(Color32.Lerp(colors[a], colors[b], 0.5f));
            weld.Add(weldId);
            weldVertices[weldId].Add(v);
            return v;
        }

        /// <summary>Splits one welded edge in every triangle that uses it, so neighbours stay conforming.</summary>
        private void SplitEdge(int weldA, int weldB)
        {
            var users = new List<int>();
            foreach (int t in weldTriangles[weldA])
            {
                int i = t * 3;
                if (weld[triangles[i]] == weldB || weld[triangles[i + 1]] == weldB || weld[triangles[i + 2]] == weldB) users.Add(t);
            }

            if (users.Count == 0) return;
            int middle = NewWeld();
            var midpoints = new Dictionary<long, int>();

            foreach (int t in users)
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
                long pair = va < vb ? ((long)va << 32) | (uint)vb : ((long)vb << 32) | (uint)va;
                if (!midpoints.TryGetValue(pair, out int vm)) midpoints[pair] = vm = AddVertex(va, vb, middle);

                // Replacing one corner at a time keeps the winding of both halves.
                UnlinkTriangle(t);
                int added = triangleSubmesh.Count;
                triangles.Add(triangles[i]);
                triangles.Add(triangles[i + 1]);
                triangles.Add(triangles[i + 2]);
                triangleSubmesh.Add(triangleSubmesh[t]);
                triangleCellMin.Add(default);
                triangleCellMax.Add(default);
                triangleMark.Add(0);
                triangles[i + cb] = vm;
                triangles[added * 3 + ca] = vm;
                LinkTriangle(t);
                LinkTriangle(added);
            }

            moved.Add(middle);
            moved.Add(weldA);
            moved.Add(weldB);
            topologyDirty = true;
        }

        /// <summary>Longest-edge bisection under the brush until no edge there is longer than maxEdge.</summary>
        private void Subdivide(Vector3 center, float radius, float maxEdge)
        {
            float maxEdgeSqr = maxEdge * maxEdge;
            var edges = new List<(int a, int b, float length)>();
            var seen = new HashSet<long>();
            for (int pass = 0; pass < 12 && vertices.Count < MaxVertices; pass++)
            {
                edges.Clear();
                seen.Clear();
                foreach (int t in TrianglesNear(center, radius))
                {
                    int i = t * 3;
                    int w0 = weld[triangles[i]], w1 = weld[triangles[i + 1]], w2 = weld[triangles[i + 2]];
                    Vector3 p0 = WeldPosition(w0), p1 = WeldPosition(w1), p2 = WeldPosition(w2);
                    float e01 = (p1 - p0).sqrMagnitude, e12 = (p2 - p1).sqrMagnitude, e20 = (p0 - p2).sqrMagnitude;
                    float longest = Mathf.Max(e01, Mathf.Max(e12, e20));
                    if (longest <= maxEdgeSqr) continue;
                    Vector3 centroid = (p0 + p1 + p2) / 3f;
                    float reach = radius + Mathf.Sqrt(longest);
                    if ((centroid - center).sqrMagnitude > reach * reach) continue;

                    (int a, int b) = longest == e01 ? (w0, w1) : longest == e12 ? (w1, w2) : (w2, w0);
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (seen.Add(key)) edges.Add((a, b, longest));
                }

                if (edges.Count == 0) return;
                edges.Sort((x, y) => y.length.CompareTo(x.length));
                foreach ((int a, int b, float _) in edges)
                {
                    SplitEdge(a, b);
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

            List<(int id, float weight)> affected = Collect(center, radius, dab.Hardness, dab.Falloff);
            if (brush == SculptBrush.Dig)
                for (int i = 0; i < affected.Count; i++) affected[i] = (affected[i].id, Mathf.Pow(affected[i].weight, 2.2f));

            if (affected.Count == 0)
            {
                Settle();
                return topologyDirty;
            }

            float amount = dab.Strength * dab.Radius * 0.08f;
            Vector3 direction = toLocal.MultiplyVector(dab.Direction).normalized;
            float sign = dab.Invert ? -1f : 1f;

            switch (brush)
            {
                case SculptBrush.SnakeHook:
                {
                    Vector3 pull = toLocal.MultiplyVector(dab.Pull);
                    foreach ((int id, float w) in affected) MoveWeld(id, pull * w);
                    break;
                }
                case SculptBrush.Pinch:
                    Pinch(affected, center, direction, Mathf.Clamp01(dab.Strength * 0.35f) * sign);
                    break;
                case SculptBrush.Crease:
                    Pinch(affected, center, direction, Mathf.Clamp01(dab.Strength * 0.25f));
                    foreach ((int id, float w) in affected) MoveWeld(id, direction * (sign * amount * 0.7f * w * w / scale));
                    break;
                case SculptBrush.Noise:
                {
                    float size = Mathf.Max(0.1f, dab.NoiseScale);
                    foreach ((int id, float w) in affected)
                    {
                        Vector3 world = toWorld.MultiplyPoint3x4(WeldPosition(id));
                        float n = Fbm(world.x / size, world.z / size) * 2f - 1f;
                        MoveWeld(id, direction * (sign * n * amount * 0.6f * w / scale));
                    }

                    break;
                }
                case SculptBrush.Terrace:
                {
                    float step = Mathf.Max(0.1f, dab.TerraceStep);
                    Vector3 up = dab.Direction.normalized;
                    float pull = Mathf.Clamp01(dab.Strength * 0.5f);
                    foreach ((int id, float w) in affected)
                    {
                        float height = Vector3.Dot(toWorld.MultiplyPoint3x4(WeldPosition(id)), up);
                        float target = TerraceHeight(height, step);
                        MoveWeld(id, toLocal.MultiplyVector(up * ((target - height) * pull * w)));
                    }

                    break;
                }
                case SculptBrush.Raise:
                    foreach ((int id, float w) in affected) MoveWeld(id, direction * (sign * amount * w / scale));
                    break;
                case SculptBrush.Dig:
                    foreach ((int id, float w) in affected) MoveWeld(id, direction * (-sign * amount * 2.2f * w / scale));
                    break;
                case SculptBrush.Inflate:
                {
                    var outward = new List<Vector3>(affected.Count);
                    foreach ((int id, float _) in affected) outward.Add(WeldNormal(id));
                    for (int i = 0; i < affected.Count; i++)
                        MoveWeld(affected[i].id, outward[i] * (sign * amount * affected[i].weight / scale));
                    break;
                }
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

            Settle();
            return true;
        }

        /// <summary>
        /// 잡아 끌기를 시작한다: 브러시 아래를 잘게 나누고, 그 안의 정점과 처음 자리를 기억한다.
        /// 돌려준 번호로 <see cref="Grab"/>을 부르면 그 정점들이 처음 자리에서 끈 만큼 옮겨진다.
        /// 대칭이면 거울 자리마다 한 번씩 부른다.
        /// </summary>
        public int BeginGrab(SculptDab dab)
        {
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            float scale = Mathf.Max(1e-6f, transform.lossyScale.x);
            Vector3 center = toLocal.MultiplyPoint3x4(dab.Center);
            float radius = dab.Radius / scale;
            if (dab.Detail > 0f) Subdivide(center, radius, dab.Detail / scale);
            var grabbed = new List<(int, float, Vector3)>();
            foreach ((int id, float w) in Collect(center, radius, dab.Hardness, dab.Falloff))
                grabbed.Add((id, w, WeldPosition(id)));
            grabs.Add(grabbed);
            Settle();
            return grabs.Count - 1;
        }

        /// <summary>잡은 정점을 처음 자리에서 <paramref name="worldOffset"/>만큼 옮긴다. 움직였으면 true.</summary>
        public bool Grab(int handle, Vector3 worldOffset)
        {
            if (handle < 0 || handle >= grabs.Count || grabs[handle].Count == 0) return false;
            Vector3 offset = transform.worldToLocalMatrix.MultiplyVector(worldOffset);
            foreach ((int id, float w, Vector3 origin) in grabs[handle])
                MoveWeld(id, origin + offset * w - WeldPosition(id));
            Settle();
            return true;
        }

        /// <summary>잡아 끌기를 끝낸다(손을 뗐을 때).</summary>
        public void EndGrab() => grabs.Clear();

        /// <summary>브러시 안의 용접 정점과 그 세기. 세기는 가운데 단단한 부분에서 1, 가장자리에서 0.</summary>
        private List<(int id, float weight)> Collect(Vector3 center, float radius, float hardness, SculptFalloff falloff)
        {
            var affected = new List<(int id, float weight)>();
            float inner = radius * Mathf.Clamp01(hardness) * 0.95f;
            float radiusSqr = radius * radius;
            List<int> near = TrianglesNear(center, radius);
            int current = ++mark;
            foreach (int t in near)
            {
                int i = t * 3;
                for (int c = 0; c < 3; c++)
                {
                    int id = weld[triangles[i + c]];
                    if (weldMark[id] == current) continue;
                    weldMark[id] = current;
                    float distanceSqr = (WeldPosition(id) - center).sqrMagnitude;
                    if (distanceSqr >= radiusSqr) continue;
                    float distance = Mathf.Sqrt(distanceSqr);
                    float t01 = distance <= inner ? 1f : 1f - (distance - inner) / (radius - inner);
                    affected.Add((id, Falloff(t01, falloff)));
                }
            }

            return affected;
        }

        /// <summary><paramref name="t"/>: 가장자리 0 → 가운데 1.</summary>
        private static float Falloff(float t, SculptFalloff falloff)
        {
            t = Mathf.Clamp01(t);
            switch (falloff)
            {
                case SculptFalloff.Sphere: return Mathf.Sqrt(1f - (1f - t) * (1f - t));
                case SculptFalloff.Sharp: return t * t;
                case SculptFalloff.Linear: return t;
                case SculptFalloff.Constant: return t > 0f ? 1f : 0f;
                default: return t * t * (3f - 2f * t);
            }
        }

        /// <summary>브러시 가운데 축으로 옆으로 모은다. 음수면 벌린다.</summary>
        private void Pinch(List<(int id, float weight)> affected, Vector3 center, Vector3 axis, float strength)
        {
            foreach ((int id, float w) in affected)
            {
                Vector3 toCenter = center - WeldPosition(id);
                toCenter -= axis * Vector3.Dot(toCenter, axis);
                MoveWeld(id, toCenter * (strength * w));
            }
        }

        /// <summary>계단 높이: 단마다 85%는 평평하고 나머지 15%가 가파른 벽.</summary>
        private static float TerraceHeight(float height, float step)
        {
            float level = Mathf.Floor(height / step);
            float fraction = height / step - level;
            float riser = Mathf.InverseLerp(0.85f, 1f, fraction);
            return (level + riser * riser * (3f - 2f * riser)) * step;
        }

        /// <summary>0..1 결 노이즈(Perlin 네 겹). 월드 좌표로 계산해 메시가 달라도 결이 이어진다.</summary>
        private static float Fbm(float x, float z)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Mathf.PerlinNoise(x + 37.1f * octave, z - 19.7f * octave) * amplitude;
                total += amplitude;
                x *= 2.03f;
                z *= 2.03f;
                amplitude *= 0.5f;
            }

            return sum / total;
        }

        private void MoveWeld(int id, Vector3 offset)
        {
            foreach (int v in weldVertices[id]) vertices[v] += offset;
            bounds.Encapsulate(WeldPosition(id));
            moved.Add(id);
        }

        private void Smooth(List<(int id, float weight)> affected, float strength)
        {
            var targets = new List<(int id, Vector3 offset)>(affected.Count);
            foreach ((int id, float w) in affected)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (int t in weldTriangles[id])
                {
                    int i = t * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        int other = weld[triangles[i + c]];
                        if (other == id) continue;
                        sum += WeldPosition(other);
                        count++;
                    }
                }

                if (count > 0) targets.Add((id, (sum / count - WeldPosition(id)) * (strength * w)));
            }

            foreach ((int id, Vector3 offset) in targets) MoveWeld(id, offset);
        }

        private bool Punch(Vector3 center, float radius)
        {
            float radiusSqr = radius * radius;
            bool changed = false;
            foreach (int t in TrianglesNear(center, radius))
            {
                int i = t * 3;
                Vector3 centroid = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                if ((centroid - center).sqrMagnitude > radiusSqr) continue;
                for (int c = 0; c < 3; c++) moved.Add(weld[triangles[i + c]]);
                UnlinkTriangle(t);
                triangles[i] = triangles[i + 1] = triangles[i + 2] = -1;
                changed = true;
            }

            if (changed) topologyDirty = true;
            Settle();
            return changed;
        }

        /// <summary>움직인 정점의 삼각형을 칸에 다시 넣고, 그 주변(한 고리 이웃) 법선만 다시 계산한다.</summary>
        private void Settle()
        {
            if (moved.Count == 0) return;
            int current = ++mark;
            var touched = new List<int>();
            foreach (int id in moved)
            {
                foreach (int t in weldTriangles[id])
                {
                    if (triangleMark[t] == current) continue;
                    triangleMark[t] = current;
                    Rebin(t);
                    int i = t * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        int w = weld[triangles[i + c]];
                        if (weldMark[w] == current) continue;
                        weldMark[w] = current;
                        touched.Add(w);
                    }
                }
            }

            foreach (int w in touched)
            {
                Vector3 n = WeldNormal(w);
                if (n == Vector3.zero) continue;
                foreach (int v in weldVertices[w]) normals[v] = n;
            }

            moved.Clear();
        }

        /// <summary>Smooth normal shared across welded vertices so seams do not show as shading lines.</summary>
        private Vector3 WeldNormal(int id)
        {
            Vector3 sum = Vector3.zero;
            foreach (int t in weldTriangles[id])
            {
                int i = t * 3;
                Vector3 p0 = vertices[triangles[i]], p1 = vertices[triangles[i + 1]], p2 = vertices[triangles[i + 2]];
                sum += Vector3.Cross(p1 - p0, p2 - p0);
            }

            return sum.sqrMagnitude > 1e-20f ? sum.normalized : Vector3.zero;
        }

        // ------------------------------------------------------------------ upload

        /// <summary>
        /// 메시에 반영한다. 모양(정점 수·삼각형)이 그대로면 위치·법선만 보낸다. <paramref name="final"/>이면
        /// 경계 상자·탄젠트까지 맞춘다(획이 끝날 때).
        /// </summary>
        public void Upload(bool final)
        {
            if (!topologyDirty && vertices.Count == uploadedVertexCount)
            {
                Mesh.SetVertices(vertices, 0, vertices.Count, final ? MeshUpdateFlags.Default : QuickUpload);
                Mesh.SetNormals(normals, 0, normals.Count, final ? MeshUpdateFlags.Default : QuickUpload);
                if (!final) Mesh.bounds = bounds;
                if (final)
                {
                    Mesh.RecalculateBounds();
                    bounds = Mesh.bounds;
                    if (hasTangents && uv0.Count == vertices.Count) Mesh.RecalculateTangents();
                }

                return;
            }

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
            bounds = Mesh.bounds;
            // 정점 수가 바뀌면 예전 탄젠트는 버려진다. 다시 계산은 메시 전체라 무거우니 획이 끝날 때 한 번만 한다.
            if (hasTangents && uv0.Count == vertices.Count && final) Mesh.RecalculateTangents();
            uploadedVertexCount = vertices.Count;
            topologyDirty = false;
        }
    }
}
