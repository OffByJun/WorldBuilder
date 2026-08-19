using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.ObjectSnapTool
{
    public enum SnapMode
    {
        Grid,
        Surface
    }

    public sealed class ObjectSnapTool : IWorldBuilderTool
    {
        [SerializeField] private SnapMode mode = SnapMode.Grid;
        [SerializeField] private Vector3 gridSize = Vector3.one;
        [SerializeField] private int surfaceMask = ~0;
        [SerializeField] private float surfaceOffset;
        [SerializeField] private bool enabled;

        private VisualElement gridSection;
        private VisualElement surfaceSection;

        public string ToolName => WorldBuilderLocalization.Get("tool.objectSnap");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.objectSnap"));

            EnumField modeField = new EnumField("Snap Mode", mode);
            modeField.RegisterValueChangedCallback(evt =>
            {
                mode = (SnapMode)evt.newValue;
                UpdateSections();
            });
            root.Add(modeField);

            gridSection = new VisualElement();
            Vector3Field gridField = new Vector3Field("Grid Size") { value = gridSize };
            gridField.RegisterValueChangedCallback(evt => gridSize = evt.newValue);
            gridSection.Add(gridField);
            root.Add(gridSection);

            surfaceSection = new VisualElement();
            LayerMaskField maskField = new LayerMaskField("Layer Mask", surfaceMask);
            maskField.RegisterValueChangedCallback(evt => surfaceMask = evt.newValue);
            surfaceSection.Add(maskField);
            FloatField offsetField = new FloatField("Offset") { value = surfaceOffset };
            offsetField.RegisterValueChangedCallback(evt => surfaceOffset = evt.newValue);
            surfaceSection.Add(offsetField);
            root.Add(surfaceSection);

            Toggle enableToggle = new Toggle("Enable") { value = enabled };
            enableToggle.RegisterValueChangedCallback(evt => enabled = evt.newValue);
            root.Add(enableToggle);

            UpdateSections();
            return root;
        }

        private void UpdateSections()
        {
            if (gridSection != null)
            {
                gridSection.style.display = mode == SnapMode.Grid ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (surfaceSection != null)
            {
                surfaceSection.style.display = mode == SnapMode.Surface ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void OnSceneGUI()
        {
            if (!enabled)
            {
                return;
            }

            Event e = Event.current;
            if (e.type != EventType.MouseUp || e.button != 0)
            {
                return;
            }

            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                Snap(selected[i].transform);
            }
        }

        private void Snap(Transform target)
        {
            Undo.RecordObject(target, "Object Snap");

            if (mode == SnapMode.Grid)
            {
                target.position = SnapToGrid(target.position);
                return;
            }

            Vector3 origin = target.position + Vector3.up * 100f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f, surfaceMask))
            {
                target.position = hit.point + hit.normal * surfaceOffset;
            }
        }

        private Vector3 SnapToGrid(Vector3 position)
        {
            return new Vector3(
                gridSize.x > 0f ? Mathf.Round(position.x / gridSize.x) * gridSize.x : position.x,
                gridSize.y > 0f ? Mathf.Round(position.y / gridSize.y) * gridSize.y : position.y,
                gridSize.z > 0f ? Mathf.Round(position.z / gridSize.z) * gridSize.z : position.z);
        }
    }
}
