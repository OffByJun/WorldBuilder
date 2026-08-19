using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.UVVisualizerTool
{
    public enum UVChannel
    {
        UV0,
        UV1,
        UV2,
        UV3
    }

    public sealed class UVVisualizerTool : IWorldBuilderTool
    {
        [SerializeField] private bool useSelection = true;
        [SerializeField] private UVChannel channel = UVChannel.UV0;
        [SerializeField] private Color edgeColor = Color.green;
        [SerializeField] private Color seamColor = new Color(1f, 0.3f, 0.1f);
        [SerializeField] private float displaySize = 3f;

        private MeshFilter target;

        private readonly List<Vector2> uvBuffer = new List<Vector2>();
        private readonly Dictionary<long, int> edgeCounts = new Dictionary<long, int>();
        private readonly List<Vector3> edgeBuffer = new List<Vector3>();
        private readonly List<Vector3> seamBuffer = new List<Vector3>();
        private Vector3[] edgeSegments = System.Array.Empty<Vector3>();
        private Vector3[] seamSegments = System.Array.Empty<Vector3>();
        private Mesh cachedMesh;
        private UVChannel cachedChannel;
        private float cachedDisplaySize;
        private int cachedVertexCount;
        private int cachedSubMeshCount;

        public string ToolName => WorldBuilderLocalization.Get("tool.uvVisualizer");
        public string Category => WorldBuilderCategory.Rendering;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.uvVisualizer"));

            ObjectField targetField = new ObjectField("Target MeshFilter")
            {
                objectType = typeof(MeshFilter),
                allowSceneObjects = true,
                value = target
            };
            targetField.RegisterValueChangedCallback(evt => target = evt.newValue as MeshFilter);
            root.Add(targetField);

            Toggle selectionToggle = new Toggle("Use Selection") { value = useSelection };
            selectionToggle.RegisterValueChangedCallback(evt =>
            {
                useSelection = evt.newValue;
                targetField.SetEnabled(!useSelection);
            });
            targetField.SetEnabled(!useSelection);
            root.Add(selectionToggle);

            EnumField channelField = new EnumField("UV Channel", channel);
            channelField.RegisterValueChangedCallback(evt =>
            {
                channel = (UVChannel)evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(channelField);

            ColorField colorField = new ColorField("Edge Color") { value = edgeColor };
            colorField.RegisterValueChangedCallback(evt =>
            {
                edgeColor = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(colorField);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            MeshFilter filter = ResolveTarget();
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            if (!RebuildSegments(filter.sharedMesh)) return;

            Vector3 origin = filter.transform.position + Vector3.up * (filter.sharedMesh.bounds.size.y + 1f);
            Matrix4x4 previous = Handles.matrix;
            Handles.matrix = Matrix4x4.Translate(origin);
            if (edgeSegments.Length > 0)
            {
                Handles.color = edgeColor;
                Handles.DrawLines(edgeSegments);
            }
            if (seamSegments.Length > 0)
            {
                Handles.color = seamColor;
                Handles.DrawLines(seamSegments);
            }
            Handles.matrix = previous;
        }

        private bool RebuildSegments(Mesh mesh)
        {
            if (cachedMesh == mesh && cachedChannel == channel && cachedDisplaySize == displaySize &&
                cachedVertexCount == mesh.vertexCount && cachedSubMeshCount == mesh.subMeshCount)
            {
                return edgeSegments.Length > 0 || seamSegments.Length > 0;
            }

            uvBuffer.Clear();
            mesh.GetUVs((int)channel, uvBuffer);
            cachedMesh = mesh;
            cachedChannel = channel;
            cachedDisplaySize = displaySize;
            cachedVertexCount = mesh.vertexCount;
            edgeBuffer.Clear();
            seamBuffer.Clear();
            edgeCounts.Clear();

            cachedSubMeshCount = mesh.subMeshCount;
            int[] triangles = mesh.triangles;
            if (uvBuffer.Count == 0)
            {
                edgeSegments = System.Array.Empty<Vector3>();
                seamSegments = System.Array.Empty<Vector3>();
                return false;
            }

            for (int i = 0; i < triangles.Length; i += 3)
            {
                CountEdge(triangles[i], triangles[i + 1]);
                CountEdge(triangles[i + 1], triangles[i + 2]);
                CountEdge(triangles[i + 2], triangles[i]);
            }

            Vector3 right = Vector3.right * displaySize;
            Vector3 up = Vector3.forward * displaySize;
            foreach (KeyValuePair<long, int> entry in edgeCounts)
            {
                int a = (int)(entry.Key >> 32);
                int b = (int)(entry.Key & 0xFFFFFFFFL);
                if (a >= uvBuffer.Count || b >= uvBuffer.Count) continue;
                List<Vector3> target = entry.Value <= 1 ? seamBuffer : edgeBuffer;
                target.Add(right * uvBuffer[a].x + up * uvBuffer[a].y);
                target.Add(right * uvBuffer[b].x + up * uvBuffer[b].y);
            }

            edgeSegments = ToArray(edgeSegments, edgeBuffer);
            seamSegments = ToArray(seamSegments, seamBuffer);
            return edgeSegments.Length > 0 || seamSegments.Length > 0;
        }

        private static Vector3[] ToArray(Vector3[] target, List<Vector3> source)
        {
            if (target.Length != source.Count) target = new Vector3[source.Count];
            source.CopyTo(target);
            return target;
        }

        private void CountEdge(int a, int b)
        {
            long key = EdgeKey(a, b);
            edgeCounts.TryGetValue(key, out int count);
            edgeCounts[key] = count + 1;
        }

        private long EdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private MeshFilter ResolveTarget()
        {
            if (!useSelection)
            {
                return target;
            }

            return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MeshFilter>() : null;
        }
    }
}
