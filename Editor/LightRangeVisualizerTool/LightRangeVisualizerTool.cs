using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.LightRangeVisualizerTool
{
    public sealed class LightRangeVisualizerTool : IWorldBuilderTool
    {
        [SerializeField] private bool showAll;
        [SerializeField] private Color pointColor = Color.yellow;
        [SerializeField] private Color spotColor = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color directionalColor = Color.white;
        [SerializeField] private float opacity = 0.8f;

        public string ToolName => WorldBuilderLocalization.Get("tool.lightRange");
        public string Category => WorldBuilderCategory.Debug;

        public Texture2D ToolIcon => null;

        private readonly SceneComponentCache<Light> cache = new SceneComponentCache<Light>();

        public void OnEnable() => cache.Hook();

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.lightRange"));

            Toggle show = new Toggle("Show All") { value = showAll };
            show.RegisterValueChangedCallback(evt =>
            {
                showAll = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(show);

            root.Add(ColorPicker("Point", pointColor, value => pointColor = value));
            root.Add(ColorPicker("Spot", spotColor, value => spotColor = value));
            root.Add(ColorPicker("Directional", directionalColor, value => directionalColor = value));

            Slider opacityField = new Slider("Opacity", 0f, 1f) { value = opacity };
            opacityField.RegisterValueChangedCallback(evt =>
            {
                opacity = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(opacityField);

            return root;
        }

        private ColorField ColorPicker(string label, Color initial, System.Action<Color> setter)
        {
            ColorField field = new ColorField(label) { value = initial };
            field.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                SceneView.RepaintAll();
            });
            return field;
        }

        public void OnSceneGUI()
        {
            if (!showAll || !WorldBuilderSceneGUI.IsRepaint)
            {
                return;
            }

            Light[] lights = cache.Items;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                Draw(lights[i]);
            }
        }

        private void Draw(Light light)
        {
            Transform t = light.transform;

            switch (light.type)
            {
                case LightType.Point:
                    Handles.color = WithOpacity(pointColor);
                    Handles.DrawWireDisc(t.position, Vector3.up, light.range);
                    Handles.DrawWireDisc(t.position, Vector3.right, light.range);
                    Handles.DrawWireDisc(t.position, Vector3.forward, light.range);
                    break;
                case LightType.Spot:
                    DrawSpot(light, t);
                    break;
                case LightType.Directional:
                    Handles.color = WithOpacity(directionalColor);
                    Handles.ArrowHandleCap(0, t.position, t.rotation, HandleUtility.GetHandleSize(t.position) * 2f, EventType.Repaint);
                    break;
            }
        }

        private void DrawSpot(Light light, Transform t)
        {
            Handles.color = WithOpacity(spotColor);

            float range = light.range;
            float halfAngle = light.spotAngle * 0.5f * Mathf.Deg2Rad;
            float radius = Mathf.Tan(halfAngle) * range;
            Vector3 forward = t.forward;
            Vector3 endCenter = t.position + forward * range;

            Handles.DrawWireArc(endCenter, forward, t.up, 360f, radius);

            Vector3 up = t.up * radius;
            Vector3 right = t.right * radius;
            Handles.DrawLine(t.position, endCenter + up);
            Handles.DrawLine(t.position, endCenter - up);
            Handles.DrawLine(t.position, endCenter + right);
            Handles.DrawLine(t.position, endCenter - right);
        }

        private Color WithOpacity(Color color)
        {
            return new Color(color.r, color.g, color.b, opacity);
        }
    }
}
