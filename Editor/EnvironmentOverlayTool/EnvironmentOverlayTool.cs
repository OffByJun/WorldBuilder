using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.EnvironmentOverlayTool
{
    public sealed class EnvironmentOverlayTool : IWorldBuilderTool
    {
        public enum OverlayType
        {
            Temperature,
            Toxic,
            Pressure,
            Visibility
        }

        [SerializeField] private OverlayType overlayType = OverlayType.Temperature;
        [SerializeField] private float opacity = 0.4f;

        private readonly IReadOnlyList<IEnvironmentZoneProvider> providers;

        public EnvironmentOverlayTool(IReadOnlyList<IEnvironmentZoneProvider> providers)
        {
            this.providers = providers;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.envOverlay");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.envOverlay"));

            EnumField typeField = new EnumField("Overlay Type", overlayType);
            typeField.RegisterValueChangedCallback(evt => overlayType = (OverlayType)evt.newValue);
            root.Add(typeField);

            Slider opacityField = new Slider("Opacity", 0f, 1f) { value = opacity };
            opacityField.RegisterValueChangedCallback(evt => opacity = evt.newValue);
            root.Add(opacityField);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            if (providers == null)
            {
                return;
            }

            Color low = LowColor();
            Color high = HighColor();

            for (int p = 0; p < providers.Count; p++)
            {
                IEnvironmentZoneProvider provider = providers[p];
                IReadOnlyList<Vector3> positions = provider.GetZonePositions();

                for (int i = 0; i < positions.Count; i++)
                {
                    float intensity = Mathf.Clamp01(provider.GetIntensityAt(i));
                    Color gradient = Color.Lerp(low, high, intensity);
                    gradient.a = opacity;

                    Handles.color = gradient;
                    float discSize = HandleUtility.GetHandleSize(positions[i]) * 1.5f;
                    Handles.DrawSolidDisc(positions[i], Vector3.up, discSize);
                }
            }
        }

        private Color LowColor()
        {
            switch (overlayType)
            {
                case OverlayType.Temperature:
                    return new Color(0.2f, 0.4f, 1f);
                case OverlayType.Toxic:
                    return new Color(0.6f, 1f, 0.6f);
                case OverlayType.Pressure:
                    return new Color(1f, 0.8f, 0.6f);
                default:
                    return new Color(0.8f, 0.8f, 0.8f);
            }
        }

        private Color HighColor()
        {
            switch (overlayType)
            {
                case OverlayType.Temperature:
                    return new Color(1f, 0.3f, 0f);
                case OverlayType.Toxic:
                    return new Color(0f, 0.6f, 0f);
                case OverlayType.Pressure:
                    return new Color(1f, 0f, 0f);
                default:
                    return new Color(0.2f, 0.2f, 0.2f);
            }
        }
    }
}
