using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PathTool
{
    public sealed class PathTool : IWorldBuilderTool, IUndoable
    {
        [SerializeField] private float pathWidth = 2f;
        [SerializeField] private int segments = 32;
        [SerializeField] private List<Vector3> controlPoints = new List<Vector3>();

        private MeshFilter target;

        public string ToolName => WorldBuilderLocalization.Get("tool.path");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public void RecordUndo(string label)
        {
            if (target != null)
            {
                Undo.RecordObject(target, label);
                UndoHistory.Push(label);
            }
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.path"));

            ObjectField targetField = new ObjectField("Mesh Filter")
            {
                objectType = typeof(MeshFilter),
                allowSceneObjects = true,
                value = target
            };
            targetField.RegisterValueChangedCallback(evt => target = evt.newValue as MeshFilter);
            root.Add(targetField);

            Slider width = new Slider("Path Width", 0.1f, 50f) { value = pathWidth };
            width.RegisterValueChangedCallback(evt => pathWidth = evt.newValue);
            root.Add(width);

            SliderInt segmentField = new SliderInt("Segments", 1, 256) { value = segments };
            segmentField.RegisterValueChangedCallback(evt => segments = evt.newValue);
            root.Add(segmentField);

            VisualElement list = new VisualElement();
            root.Add(list);

            Button add = new Button(() =>
            {
                Vector3 next = controlPoints.Count > 0 ? controlPoints[controlPoints.Count - 1] + Vector3.forward * 5f : Vector3.zero;
                controlPoints.Add(next);
                RebuildList(list);
                Rebuild();
            })
            {
                text = "Add Control Point"
            };
            root.Add(add);

            Button rebuild = new Button(Rebuild) { text = "Rebuild Path" };
            root.Add(rebuild);

            RebuildList(list);
            return root;
        }

        private void RebuildList(VisualElement list)
        {
            list.Clear();

            for (int i = 0; i < controlPoints.Count; i++)
            {
                int index = i;

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;

                Vector3Field field = new Vector3Field(index.ToString()) { value = controlPoints[index] };
                field.style.flexGrow = 1f;
                field.RegisterValueChangedCallback(evt =>
                {
                    controlPoints[index] = evt.newValue;
                    Rebuild();
                });

                Button remove = new Button(() =>
                {
                    controlPoints.RemoveAt(index);
                    RebuildList(list);
                    Rebuild();
                })
                {
                    text = "X"
                };
                remove.style.width = 22f;

                row.Add(field);
                row.Add(remove);
                list.Add(row);
            }
        }

        public void OnSceneGUI()
        {
            bool changed = false;

            for (int i = 0; i < controlPoints.Count; i++)
            {
                float size = HandleUtility.GetHandleSize(controlPoints[i]) * 0.1f;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(controlPoints[i], size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    controlPoints[i] = moved;
                    changed = true;
                }
            }

            if (changed)
            {
                Rebuild();
                SceneView.RepaintAll();
            }
        }

        private void Rebuild()
        {
            if (target == null)
            {
                return;
            }

            RecordUndo("Edit Path");
            target.sharedMesh = PathMeshBuilder.Build(controlPoints, pathWidth, segments);
        }
    }
}
