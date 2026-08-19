using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.DepthLayerVisualizer
{
    public sealed class DepthLayerVisualizerTool : IWorldBuilderTool
    {
        [SerializeField] private float shallowDepth = 10f;
        [SerializeField] private float midDepth = 40f;
        [SerializeField] private float deepDepth = 100f;
        [SerializeField] private Color shallowColor = new Color(0.5f, 0.8f, 1f, 0.2f);
        [SerializeField] private Color midColor = new Color(0.1f, 0.3f, 0.9f, 0.2f);
        [SerializeField] private Color deepColor = new Color(0.05f, 0.1f, 0.4f, 0.2f);

        private const float PlaneExtent = 250f;

        public string ToolName => WorldBuilderLocalization.Get("tool.depthLayer");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.depthLayer"));

            Slider shallowField = new Slider("Shallow Depth", 0f, 500f) { value = shallowDepth };
            shallowField.RegisterValueChangedCallback(evt => shallowDepth = evt.newValue);
            root.Add(shallowField);

            Slider midField = new Slider("Mid Depth", 0f, 500f) { value = midDepth };
            midField.RegisterValueChangedCallback(evt => midDepth = evt.newValue);
            root.Add(midField);

            Slider deepField = new Slider("Deep Depth", 0f, 500f) { value = deepDepth };
            deepField.RegisterValueChangedCallback(evt => deepDepth = evt.newValue);
            root.Add(deepField);

            ColorField shallowColorField = new ColorField("Shallow Color") { value = shallowColor };
            shallowColorField.RegisterValueChangedCallback(evt => shallowColor = evt.newValue);
            root.Add(shallowColorField);

            ColorField midColorField = new ColorField("Mid Color") { value = midColor };
            midColorField.RegisterValueChangedCallback(evt => midColor = evt.newValue);
            root.Add(midColorField);

            ColorField deepColorField = new ColorField("Deep Color") { value = deepColor };
            deepColorField.RegisterValueChangedCallback(evt => deepColor = evt.newValue);
            root.Add(deepColorField);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            Vector3 pivot = view.pivot;
            DrawLayer(pivot, -shallowDepth * 0.5f, shallowColor);
            DrawLayer(pivot, -(shallowDepth + midDepth) * 0.5f, midColor);
            DrawLayer(pivot, -(midDepth + deepDepth) * 0.5f, deepColor);
        }

        private void DrawLayer(Vector3 pivot, float y, Color color)
        {
            Vector3 center = new Vector3(pivot.x, y, pivot.z);
            Vector3[] corners =
            {
                center + new Vector3(-PlaneExtent, 0f, -PlaneExtent),
                center + new Vector3(-PlaneExtent, 0f, PlaneExtent),
                center + new Vector3(PlaneExtent, 0f, PlaneExtent),
                center + new Vector3(PlaneExtent, 0f, -PlaneExtent)
            };

            Color outline = new Color(color.r, color.g, color.b, 1f);
            Handles.DrawSolidRectangleWithOutline(corners, color, outline);
        }
    }
}
