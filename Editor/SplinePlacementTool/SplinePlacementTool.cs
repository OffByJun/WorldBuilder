using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.SplinePlacementTool
{
    /// <summary>
    /// Places prefabs along a Unity Spline: roads, riverside props, coastlines, fences.
    /// Instances are grouped under a single root and can be regenerated or cleared.
    /// </summary>
    public sealed class SplinePlacementTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private SplineContainer container;
        [SerializeField] private float spacing = 4f;
        [SerializeField] private float lateralOffsetRange;
        [SerializeField] private float yOffset;
        [SerializeField] private bool alignToTangent = true;
        [SerializeField] private bool randomYaw;
        [SerializeField] private Vector2 scaleRange = new Vector2(1f, 1f);
        [SerializeField] private bool snapToSurface = true;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private string rootName = "SplinePlacements";
        [SerializeField] private List<GameObject> prefabs = new List<GameObject>();

        public string ToolName => WorldBuilderLocalization.Get("tool.spline");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.spline"));

            ObjectField splineField = new ObjectField("Spline Container")
            {
                objectType = typeof(SplineContainer),
                value = container
            };
            splineField.RegisterValueChangedCallback(evt => container = evt.newValue as SplineContainer);
            root.Add(splineField);

            Slider spacingField = new Slider("Spacing", 0.25f, 50f) { value = spacing };
            spacingField.RegisterValueChangedCallback(evt => spacing = evt.newValue);
            root.Add(spacingField);

            Slider offset = new Slider("Lateral Offset Range", 0f, 20f) { value = lateralOffsetRange };
            offset.RegisterValueChangedCallback(evt => lateralOffsetRange = evt.newValue);
            root.Add(offset);

            FloatField yOff = new FloatField("Y Offset") { value = yOffset };
            yOff.RegisterValueChangedCallback(evt => yOffset = evt.newValue);
            root.Add(yOff);

            Toggle align = new Toggle("Align To Tangent") { value = alignToTangent };
            align.RegisterValueChangedCallback(evt => alignToTangent = evt.newValue);
            root.Add(align);

            Toggle yaw = new Toggle("Random Yaw") { value = randomYaw };
            yaw.RegisterValueChangedCallback(evt => randomYaw = evt.newValue);
            root.Add(yaw);

            Vector2Field scale = new Vector2Field("Scale Range") { value = scaleRange };
            scale.RegisterValueChangedCallback(evt => scaleRange = evt.newValue);
            root.Add(scale);

            Toggle snap = new Toggle("Snap To Surface") { value = snapToSurface };
            snap.RegisterValueChangedCallback(evt => snapToSurface = evt.newValue);
            root.Add(snap);

            LayerMaskField mask = new LayerMaskField("Surface Mask", surfaceMask);
            mask.RegisterValueChangedCallback(evt => surfaceMask = evt.newValue);
            root.Add(mask);

            TextField nameField = new TextField("Root Name") { value = rootName };
            nameField.RegisterValueChangedCallback(evt => rootName = evt.newValue);
            root.Add(nameField);

            root.Add(BuildPrefabList());

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            Button generate = new Button(Generate) { text = WorldBuilderLocalization.Get("btn.generateSpline") };
            generate.style.flexGrow = 1;
            Button clear = new Button(ClearGenerated) { text = WorldBuilderLocalization.Get("btn.clear") };
            clear.style.flexGrow = 1;
            buttons.Add(generate);
            buttons.Add(clear);
            buttons.style.marginTop = 8f;
            root.Add(buttons);

            return root;
        }

        public void OnSceneGUI()
        {
            if (container == null || container.Spline == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            Handles.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            const int samples = 128;
            Vector3 previous = default;
            bool started = false;
            Transform transform = container.transform;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 worldPos = transform.TransformPoint(SplineUtility.EvaluatePosition(container.Spline, t));
                if (started) Handles.DrawLine(previous, worldPos);
                previous = worldPos;
                started = true;
            }
        }

        private void Generate()
        {
            if (container == null || container.Spline == null)
            {
                Debug.LogWarning("[WorldBuilder] Assign a SplineContainer first.");
                return;
            }

            List<GameObject> valid = prefabs.FindAll(p => p != null);
            if (valid.Count == 0)
            {
                Debug.LogWarning("[WorldBuilder] Add at least one prefab.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(string.IsNullOrWhiteSpace(rootName) ? "SplinePlacements" : rootName);
                Undo.RegisterCreatedObjectUndo(root, "Spline Placement Root");
            }
            Undo.RecordObject(root, "Spline Placement");

            System.Random rng = new System.Random(System.Guid.NewGuid().GetHashCode());
            Transform transform = container.transform;
            float length = SplineUtility.CalculateLength(container.Spline, container.transform.localToWorldMatrix);
            int count = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.25f, spacing)));
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float3 position = SplineUtility.EvaluatePosition(container.Spline, t);
                float3 tangent = SplineUtility.EvaluateTangent(container.Spline, t);
                Vector3 worldPos = transform.TransformPoint(position);
                Vector3 worldTangent = transform.TransformDirection(tangent);

                if (lateralOffsetRange > 0f)
                {
                    Vector3 lateral = Vector3.Cross(Vector3.up, worldTangent).normalized;
                    float offset = Mathf.Lerp(-lateralOffsetRange, lateralOffsetRange, (float)rng.NextDouble());
                    worldPos += lateral * offset;
                }

                Quaternion rotation = Quaternion.identity;
                if (alignToTangent && worldTangent.sqrMagnitude > 0.000001f)
                {
                    Vector3 forward = new Vector3(worldTangent.x, 0f, worldTangent.z).normalized;
                    if (forward.sqrMagnitude > 0.000001f) rotation = Quaternion.LookRotation(forward, Vector3.up);
                }
                if (randomYaw)
                {
                    rotation *= Quaternion.AngleAxis((float)(rng.NextDouble() * 360.0), Vector3.up);
                }

                float scale = Mathf.Lerp(
                    Mathf.Min(scaleRange.x, scaleRange.y),
                    Mathf.Max(scaleRange.x, scaleRange.y),
                    (float)rng.NextDouble());

                if (snapToSurface && Physics.Raycast(worldPos + Vector3.up * 100f, Vector3.down,
                        out RaycastHit hit, 2000f, surfaceMask))
                {
                    worldPos = hit.point;
                    rotation *= Quaternion.FromToRotation(Vector3.up, hit.normal);
                }

                worldPos += Vector3.up * yOffset;

                GameObject prefab = valid[rng.Next(valid.Count)];
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (instance == null) continue;
                instance.transform.position = worldPos;
                instance.transform.rotation = rotation;
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.SetParent(root.transform, true);
                Undo.RegisterCreatedObjectUndo(instance, "Spline Placement");
                placed++;
            }

            Undo.CollapseUndoOperations(group);
            UndoHistory.Push($"Spline Placement ({placed})");
            Debug.Log($"[WorldBuilder] Placed {placed} instances along spline ({length:F1}m, {count} slots).");
        }

        private void ClearGenerated()
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) return;
            Undo.DestroyObjectImmediate(root);
            UndoHistory.Push("Clear Spline Placements");
        }

        private VisualElement BuildPrefabList()
        {
            VisualElement section = new VisualElement();
            section.Add(new Label("Prefabs"));

            VisualElement list = new VisualElement();
            section.Add(list);

            void Rebuild()
            {
                list.Clear();
                for (int i = 0; i < prefabs.Count; i++)
                {
                    int index = i;
                    VisualElement row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;

                    ObjectField field = new ObjectField { objectType = typeof(GameObject), value = prefabs[index] };
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        prefabs[index] = evt.newValue as GameObject;
                        Rebuild();
                    });

                    Button remove = new Button(() =>
                    {
                        prefabs.RemoveAt(index);
                        Rebuild();
                    }) { text = "X" };

                    row.Add(field);
                    row.Add(remove);
                    list.Add(row);
                }
            }

            Button add = new Button(() =>
            {
                prefabs.Add(null);
                Rebuild();
            }) { text = "Add" };

            section.Add(add);
            Rebuild();
            return section;
        }
    }
}
