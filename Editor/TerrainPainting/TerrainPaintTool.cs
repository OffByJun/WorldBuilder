using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.TerrainPainting
{
    public sealed class TerrainPaintTool : IWorldBuilderTool, IUndoable, IRaycastConsumer
    {
        [SerializeField] private float brushRadius = 2f;
        [SerializeField] private float brushStrength = 0.5f;
        [SerializeField] private Color paintColor = Color.red;

        private readonly VertexPicker picker = new VertexPicker();
        private readonly List<int> affected = new List<int>();
        private MeshRenderer target;

        public string ToolName => WorldBuilderLocalization.Get("tool.terrainPaint");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public void RecordUndo(string label)
        {
            Mesh mesh = GetMesh();
            if (mesh != null)
            {
                Undo.RecordObject(mesh, label);
                UndoHistory.Push(label);
            }
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.terrainPaint"));

            ObjectField targetField = new ObjectField("Mesh Renderer")
            {
                objectType = typeof(MeshRenderer),
                allowSceneObjects = true,
                value = target
            };
            targetField.RegisterValueChangedCallback(evt => target = evt.newValue as MeshRenderer);
            root.Add(targetField);

            Slider radius = new Slider("Brush Radius", 0.1f, 50f) { value = brushRadius };
            radius.RegisterValueChangedCallback(evt => brushRadius = evt.newValue);
            root.Add(radius);

            Slider strength = new Slider("Brush Strength", 0f, 1f) { value = brushStrength };
            strength.RegisterValueChangedCallback(evt => brushStrength = evt.newValue);
            root.Add(strength);

            ColorField color = new ColorField("Paint Color") { value = paintColor };
            color.RegisterValueChangedCallback(evt => paintColor = evt.newValue);
            root.Add(color);

            HelpBox help = new HelpBox("Target needs a MeshFilter with a mesh.", HelpBoxMessageType.Warning);
            root.Add(help);

            root.schedule.Execute(() =>
            {
                bool warn = target != null && GetMesh() == null;
                help.style.display = warn ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(200);

            return root;
        }

        public void OnSceneGUI()
        {
            Mesh mesh = GetMesh();
            if (mesh == null)
            {
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            Handles.color = paintColor;
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
            SceneView.RepaintAll();

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                Paint(mesh, hit.point);
                e.Use();
            }
        }

        private void Paint(Mesh mesh, Vector3 center)
        {
            picker.FindWithinRadius(mesh, target.transform, center, brushRadius, affected);
            if (affected.Count == 0)
            {
                return;
            }

            RecordUndo("Paint Vertex Colors");

            Color32[] colors = mesh.colors32;
            if (colors == null || colors.Length != mesh.vertexCount)
            {
                colors = new Color32[mesh.vertexCount];
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = Color.white;
                }
            }

            Color32 destination = paintColor;
            for (int i = 0; i < affected.Count; i++)
            {
                int index = affected[i];
                colors[index] = Color32.Lerp(colors[index], destination, brushStrength);
            }

            mesh.colors32 = colors;
        }

        private Mesh GetMesh()
        {
            if (target == null)
            {
                return null;
            }

            MeshFilter filter = target.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
