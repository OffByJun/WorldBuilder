using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.LayerBatchTool
{
    public sealed class LayerBatchTool : IWorldBuilderTool
    {
        [SerializeField] private int sourceLayer;
        [SerializeField] private int targetLayer;
        [SerializeField] private bool sceneWide = true;
        [SerializeField] private bool includeChildren = true;

        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.layerBatch");
        public string Category => WorldBuilderCategory.Productivity;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.layerBatch"));

            LayerField source = new LayerField("Source Layer", sourceLayer);
            source.RegisterValueChangedCallback(evt => sourceLayer = evt.newValue);
            root.Add(source);

            LayerField target = new LayerField("Target Layer", targetLayer);
            target.RegisterValueChangedCallback(evt => targetLayer = evt.newValue);
            root.Add(target);

            Toggle scope = new Toggle("Scene Wide") { value = sceneWide };
            scope.RegisterValueChangedCallback(evt => sceneWide = evt.newValue);
            root.Add(scope);

            Toggle children = new Toggle("Include Children") { value = includeChildren };
            children.RegisterValueChangedCallback(evt => includeChildren = evt.newValue);
            root.Add(children);

            Button apply = new Button(Apply) { text = "Apply" };
            root.Add(apply);

            resultLabel = new Label(string.Empty);
            root.Add(resultLabel);

            return root;
        }

        private void Apply()
        {
            int changed = LayerBatchService.Apply(sourceLayer, targetLayer, sceneWide, includeChildren);
            if (resultLabel != null)
            {
                resultLabel.text = "Changed: " + changed;
            }
        }
    }
}
