using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.DrawCallHeatmapTool
{
    public sealed class DrawCallHeatmapTool : IWorldBuilderTool
    {
        [SerializeField] private int lowThreshold = 1;
        [SerializeField] private int highThreshold = 4;

        private readonly List<Renderer> renderers = new List<Renderer>();

        public string ToolName => WorldBuilderLocalization.Get("tool.drawCallHeatmap");
        public string Category => WorldBuilderCategory.Debug;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.drawCallHeatmap"));

            Button refresh = new Button(Refresh) { text = "Refresh" };
            root.Add(refresh);

            SliderInt lowField = new SliderInt("Low Threshold", 1, 16) { value = lowThreshold };
            lowField.RegisterValueChangedCallback(evt =>
            {
                lowThreshold = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(lowField);

            SliderInt highField = new SliderInt("High Threshold", 1, 32) { value = highThreshold };
            highField.RegisterValueChangedCallback(evt =>
            {
                highThreshold = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(highField);

            root.Add(BuildLegend());

            return root;
        }

        private VisualElement BuildLegend()
        {
            VisualElement legend = new VisualElement();
            legend.style.flexDirection = FlexDirection.Row;
            legend.Add(LegendSwatch(Color.green, "Low"));
            legend.Add(LegendSwatch(Color.yellow, "Mid"));
            legend.Add(LegendSwatch(Color.red, "High"));
            return legend;
        }

        private VisualElement LegendSwatch(Color color, string text)
        {
            VisualElement item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.marginRight = 8f;

            VisualElement swatch = new VisualElement();
            swatch.style.width = 14f;
            swatch.style.height = 14f;
            swatch.style.backgroundColor = color;
            swatch.style.marginRight = 4f;
            item.Add(swatch);

            item.Add(new Label(text));
            return item;
        }

        private void Refresh()
        {
            renderers.Clear();
            renderers.AddRange(SceneObjectCollector.CollectComponents<Renderer>(true));
            SceneView.RepaintAll();
        }

        private readonly Vector3[] corners = new Vector3[4];

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                int drawCalls = EstimateDrawCalls(renderers[i]);
                Color color = ColorFor(drawCalls);

                Bounds bounds = renderers[i].bounds;
                corners[0] = new Vector3(bounds.min.x, bounds.min.y, bounds.center.z);
                corners[1] = new Vector3(bounds.min.x, bounds.max.y, bounds.center.z);
                corners[2] = new Vector3(bounds.max.x, bounds.max.y, bounds.center.z);
                corners[3] = new Vector3(bounds.max.x, bounds.min.y, bounds.center.z);

                Color fill = new Color(color.r, color.g, color.b, 0.25f);
                Handles.DrawSolidRectangleWithOutline(corners, fill, color);
                Handles.Label(bounds.center, drawCalls.ToString());
            }
        }

        private int EstimateDrawCalls(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            return materials != null ? materials.Length : 0;
        }

        private Color ColorFor(int drawCalls)
        {
            if (drawCalls <= lowThreshold)
            {
                return Color.green;
            }

            if (drawCalls >= highThreshold)
            {
                return Color.red;
            }

            return Color.yellow;
        }
    }
}
