using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.LODGeneratorTool
{
    public sealed class LODGeneratorTool : IWorldBuilderTool
    {
        [SerializeField] private bool useSelection = true;
        [SerializeField] private int lodCount = 3;

        private readonly float[] ratios = { 1f, 0.5f, 0.25f, 0.1f };

        private MeshFilter target;
        private VisualElement ratioContainer;
        private Label statusLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.lodGenerator");
        public string Category => WorldBuilderCategory.World;

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

            root.Add(InspectorHelp.Build(ToolName, "help.lodGenerator"));

            ObjectField targetField = new ObjectField("Target MeshFilter")
            {
                objectType = typeof(MeshFilter),
                allowSceneObjects = true,
                value = target
            };
            targetField.RegisterValueChangedCallback(evt => target = evt.newValue as MeshFilter);
            root.Add(targetField);

            Toggle selectionToggle = new Toggle("Use Selection") { value = useSelection };
            selectionToggle.RegisterValueChangedCallback(evt =>
            {
                useSelection = evt.newValue;
                targetField.SetEnabled(!useSelection);
            });
            targetField.SetEnabled(!useSelection);
            root.Add(selectionToggle);

            SliderInt countField = new SliderInt("LOD Levels", 2, 4) { value = lodCount };
            countField.RegisterValueChangedCallback(evt =>
            {
                lodCount = evt.newValue;
                RebuildRatioFields();
            });
            root.Add(countField);

            ratioContainer = new VisualElement();
            root.Add(ratioContainer);
            RebuildRatioFields();

            Button generate = new Button(Generate) { text = "Generate" };
            root.Add(generate);

            statusLabel = new Label(string.Empty);
            root.Add(statusLabel);

            return root;
        }

        private void RebuildRatioFields()
        {
            ratioContainer.Clear();
            for (int i = 0; i < lodCount; i++)
            {
                int index = i;
                Slider field = new Slider("LOD" + i + " Ratio", 0.01f, 1f) { value = ratios[i] };
                field.RegisterValueChangedCallback(evt => ratios[index] = evt.newValue);
                ratioContainer.Add(field);
            }
        }

        private void Generate()
        {
            MeshFilter filter = ResolveTarget();
            if (filter == null || filter.sharedMesh == null)
            {
                SetStatus("No mesh selected.");
                return;
            }

            MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
            Material[] materials = sourceRenderer != null ? sourceRenderer.sharedMaterials : new Material[0];
            Mesh sourceMesh = filter.sharedMesh;
            GameObject root = filter.gameObject;

            LOD[] lods = new LOD[lodCount];
            for (int i = 0; i < lodCount; i++)
            {
                GameObject lodObject = new GameObject(root.name + "_LOD" + i);
                Undo.RegisterCreatedObjectUndo(lodObject, "Generate LOD");
                lodObject.transform.SetParent(root.transform, false);

                MeshFilter lodFilter = Undo.AddComponent<MeshFilter>(lodObject);
                lodFilter.sharedMesh = i == 0 ? sourceMesh : LODMeshSimplifier.Simplify(sourceMesh, ratios[i], root.name + "_LOD" + i);

                MeshRenderer lodRenderer = Undo.AddComponent<MeshRenderer>(lodObject);
                lodRenderer.sharedMaterials = materials;

                float height = Mathf.Lerp(0.5f, 0.02f, lodCount > 1 ? (float)i / (lodCount - 1) : 0f);
                lods[i] = new LOD(height, new Renderer[] { lodRenderer });
            }

            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null)
            {
                group = Undo.AddComponent<LODGroup>(root);
            }
            else
            {
                Undo.RecordObject(group, "Generate LOD");
            }

            group.SetLODs(lods);
            group.RecalculateBounds();

            SetStatus("Generated " + lodCount + " LOD levels.");
        }

        private MeshFilter ResolveTarget()
        {
            if (!useSelection)
            {
                return target;
            }

            return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MeshFilter>() : null;
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }
        }
    }
}
