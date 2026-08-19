using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.ColliderFitterTool
{
    public sealed class ColliderFitterTool : IWorldBuilderTool
    {
        [SerializeField] private bool useSelection = true;
        [SerializeField] private ColliderFitType fitType = ColliderFitType.Box;
        [SerializeField] private bool replaceExisting = true;

        private GameObject target;

        public string ToolName => WorldBuilderLocalization.Get("tool.colliderFitter");
        public string Category => WorldBuilderCategory.Physics;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.colliderFitter"));

            ObjectField targetField = new ObjectField("Target")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = target
            };
            targetField.RegisterValueChangedCallback(evt =>
            {
                target = evt.newValue as GameObject;
                SceneView.RepaintAll();
            });
            root.Add(targetField);

            Toggle selectionToggle = new Toggle("Use Selection") { value = useSelection };
            selectionToggle.RegisterValueChangedCallback(evt =>
            {
                useSelection = evt.newValue;
                targetField.SetEnabled(!useSelection);
                SceneView.RepaintAll();
            });
            targetField.SetEnabled(!useSelection);
            root.Add(selectionToggle);

            EnumField typeField = new EnumField("Collider Type", fitType);
            typeField.RegisterValueChangedCallback(evt =>
            {
                fitType = (ColliderFitType)evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(typeField);

            Toggle replaceToggle = new Toggle("Replace Existing") { value = replaceExisting };
            replaceToggle.RegisterValueChangedCallback(evt => replaceExisting = evt.newValue);
            root.Add(replaceToggle);

            Button fit = new Button(Fit) { text = "Fit" };
            root.Add(fit);

            return root;
        }

        private void Fit()
        {
            GameObject resolved = ResolveTarget();
            if (resolved != null)
            {
                ColliderFitterService.Fit(resolved, fitType, replaceExisting);
            }
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            GameObject resolved = ResolveTarget();
            if (resolved == null || !ColliderFitterService.TryGetBounds(resolved, out Bounds bounds))
            {
                return;
            }

            Handles.color = Color.green;
            Vector3 worldCenter = resolved.transform.TransformPoint(bounds.center);

            switch (fitType)
            {
                case ColliderFitType.Sphere:
                    Handles.DrawWireDisc(worldCenter, Vector3.up, bounds.extents.magnitude);
                    Handles.DrawWireDisc(worldCenter, Vector3.right, bounds.extents.magnitude);
                    Handles.DrawWireDisc(worldCenter, Vector3.forward, bounds.extents.magnitude);
                    break;
                default:
                    Handles.matrix = Matrix4x4.TRS(resolved.transform.position, resolved.transform.rotation, resolved.transform.lossyScale);
                    Handles.DrawWireCube(bounds.center, bounds.size);
                    Handles.matrix = Matrix4x4.identity;
                    break;
            }
        }

        private GameObject ResolveTarget()
        {
            return useSelection ? Selection.activeGameObject : target;
        }
    }
}
