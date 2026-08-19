using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBatchTool
{
    public sealed class PrefabBatchTool : IWorldBuilderTool
    {
        [SerializeField] private bool sceneWide = true;

        public string ToolName => WorldBuilderLocalization.Get("tool.prefabBatch");
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

            root.Add(InspectorHelp.Build(ToolName, "help.prefabBatch"));

            Toggle scope = new Toggle("Scene Wide") { value = sceneWide };
            scope.RegisterValueChangedCallback(evt => sceneWide = evt.newValue);
            root.Add(scope);

            TabView tabs = new TabView();
            tabs.Add(BuildApplyTab());
            tabs.Add(BuildRevertTab());
            root.Add(tabs);

            return root;
        }

        private Tab BuildApplyTab()
        {
            Tab tab = new Tab("Apply Overrides");
            Button apply = new Button(() => PrefabBatchService.ApplyOverrides(sceneWide)) { text = "Apply" };
            tab.Add(apply);
            return tab;
        }

        private Tab BuildRevertTab()
        {
            Tab tab = new Tab("Revert Overrides");
            Button revert = new Button(() => PrefabBatchService.RevertOverrides(sceneWide)) { text = "Revert" };
            tab.Add(revert);
            return tab;
        }
    }
}
