using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Editor.PrefabBrush;

namespace WorldBuilder.Editor.ScatterBakeTool
{
    public sealed class ScatterBakeTool : IWorldBuilderTool
    {
        private readonly IBiomeMap biomeMap;
        private PrefabBrushSettings brush;
        private BlenderBridgeSettings bridge;
        private Label summary;

        public ScatterBakeTool(IBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.scatterBake");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.scatterBake"));

            ObjectField brushField = new ObjectField("Prefab Brush Settings")
            {
                objectType = typeof(PrefabBrushSettings),
                value = brush != null ? brush : PrefabBrushSettingsLocator.LoadOrCreate()
            };
            brushField.RegisterValueChangedCallback(evt => brush = evt.newValue as PrefabBrushSettings);
            root.Add(brushField);

            ObjectField bridgeField = new ObjectField("Blender Bridge Settings")
            {
                objectType = typeof(BlenderBridgeSettings),
                value = bridge != null ? bridge : ChunkManifestImporter.FindSettings(false)
            };
            bridgeField.RegisterValueChangedCallback(evt => bridge = evt.newValue as BlenderBridgeSettings);
            root.Add(bridgeField);

            Button bake = new Button(Bake) { text = WorldBuilderLocalization.Get("btn.bakeScatter") };
            bake.style.marginTop = 8f;
            root.Add(bake);

            summary = new Label();
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginTop = 6f;
            root.Add(summary);

            return root;
        }

        public void OnSceneGUI()
        {
        }

        private void Bake()
        {
            PrefabBrushSettings settings = brush != null ? brush : PrefabBrushSettingsLocator.LoadOrCreate();
            BlenderBridgeSettings bridgeSettings = bridge != null ? bridge : ChunkManifestImporter.FindSettings(true);
            if (bridgeSettings == null || settings == null)
            {
                summary.text = "Brush settings and bridge settings are required.";
                return;
            }

            if (settings.strokes.Count == 0)
            {
                summary.text = "No recorded strokes. Paint with the Prefab Brush first.";
                return;
            }

            try
            {
                ScatterChunkBaker.BakeSummary result = ScatterChunkBaker.Bake(settings, bridgeSettings, biomeMap);
                string text = $"Updated {result.ChunksUpdated} chunk(s), added {result.PlacementsAdded} placement(s).";
                if (result.Skipped.Count > 0)
                {
                    text += $"\nSkipped {result.Skipped.Count}:";
                    for (int i = 0; i < System.Math.Min(5, result.Skipped.Count); i++) text += "\n- " + result.Skipped[i];
                    if (result.Skipped.Count > 5) text += "\n- ...";
                }
                summary.text = text;
                UndoHistory.Push($"Scatter Bake ({result.PlacementsAdded})");
            }
            catch (System.Exception exception)
            {
                summary.text = "Bake failed: " + exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
