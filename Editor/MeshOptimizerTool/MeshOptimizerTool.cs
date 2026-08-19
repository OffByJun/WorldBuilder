using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.MeshOptimizerTool
{
    public sealed class MeshOptimizerTool : IWorldBuilderTool
    {
        [SerializeField] private bool useSelection;
        [SerializeField] private bool removeDuplicates = true;
        [SerializeField] private bool removeUnused = true;
        [SerializeField] private bool cleanUv;

        private MeshFilter targetFilter;
        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.meshOptimizer");
        public string Category => WorldBuilderCategory.Automation;

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

            root.Add(InspectorHelp.Build(ToolName, "help.meshOptimizer"));

            ObjectField target = new ObjectField("Target MeshFilter")
            {
                objectType = typeof(MeshFilter),
                allowSceneObjects = true,
                value = targetFilter
            };
            target.RegisterValueChangedCallback(evt => targetFilter = evt.newValue as MeshFilter);
            root.Add(target);

            Toggle selectionToggle = new Toggle("Use Selection") { value = useSelection };
            selectionToggle.RegisterValueChangedCallback(evt =>
            {
                useSelection = evt.newValue;
                target.SetEnabled(!useSelection);
            });
            target.SetEnabled(!useSelection);
            root.Add(selectionToggle);

            Toggle duplicates = new Toggle("Remove Duplicate Vertices") { value = removeDuplicates };
            duplicates.RegisterValueChangedCallback(evt => removeDuplicates = evt.newValue);
            root.Add(duplicates);

            Toggle unused = new Toggle("Remove Unused Vertices") { value = removeUnused };
            unused.RegisterValueChangedCallback(evt => removeUnused = evt.newValue);
            root.Add(unused);

            Toggle uv = new Toggle("Clean UV") { value = cleanUv };
            uv.RegisterValueChangedCallback(evt => cleanUv = evt.newValue);
            root.Add(uv);

            Button optimize = new Button(Optimize) { text = "Optimize" };
            root.Add(optimize);

            resultLabel = new Label(string.Empty);
            root.Add(resultLabel);

            return root;
        }

        private void Optimize()
        {
            MeshFilter filter = ResolveTarget();
            if (filter == null || filter.sharedMesh == null)
            {
                if (resultLabel != null)
                {
                    resultLabel.text = "No mesh selected.";
                }

                return;
            }

            MeshOptimizeOptions options = new MeshOptimizeOptions
            {
                removeDuplicates = removeDuplicates,
                removeUnused = removeUnused,
                cleanUv = cleanUv
            };

            MeshOptimizeResult result = MeshOptimizerService.Optimize(filter.sharedMesh, options);

            if (resultLabel != null)
            {
                resultLabel.text = "Vertices: " + result.beforeVertices + " -> " + result.afterVertices;
            }
        }

        private MeshFilter ResolveTarget()
        {
            if (!useSelection)
            {
                return targetFilter;
            }

            return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MeshFilter>() : null;
        }
    }
}
