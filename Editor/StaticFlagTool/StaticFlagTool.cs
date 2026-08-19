using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.StaticFlagTool
{
    public sealed class StaticFlagTool : IWorldBuilderTool
    {
        [SerializeField] private bool sceneWide;
        [SerializeField] private bool includeChildren = true;
        [SerializeField] private StaticEditorFlags flags = StaticEditorFlags.ContributeGI;

        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.staticFlag");
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

            root.Add(InspectorHelp.Build(ToolName, "help.staticFlag"));

            Toggle scope = new Toggle("Scene Wide") { value = sceneWide };
            scope.RegisterValueChangedCallback(evt => sceneWide = evt.newValue);
            root.Add(scope);

            EnumFlagsField flagsField = new EnumFlagsField("Static Flags", flags);
            flagsField.RegisterValueChangedCallback(evt => flags = (StaticEditorFlags)evt.newValue);
            root.Add(flagsField);

            Toggle children = new Toggle("Include Children") { value = includeChildren };
            children.RegisterValueChangedCallback(evt => includeChildren = evt.newValue);
            root.Add(children);

            Button setButton = new Button(() => Apply(flags)) { text = "Set Static" };
            root.Add(setButton);

            Button clearButton = new Button(() => Apply((StaticEditorFlags)0)) { text = "Clear Static" };
            root.Add(clearButton);

            resultLabel = new Label(string.Empty);
            root.Add(resultLabel);

            return root;
        }

        private void Apply(StaticEditorFlags value)
        {
            List<GameObject> roots = SceneObjectCollector.CollectGameObjects(sceneWide);
            int changed = 0;

            for (int i = 0; i < roots.Count; i++)
            {
                if (includeChildren)
                {
                    Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < transforms.Length; j++)
                    {
                        changed += SetFlags(transforms[j].gameObject, value);
                    }
                }
                else
                {
                    changed += SetFlags(roots[i], value);
                }
            }

            if (resultLabel != null)
            {
                resultLabel.text = "Changed: " + changed;
            }
        }

        private int SetFlags(GameObject target, StaticEditorFlags value)
        {
            Undo.RecordObject(target, "Static Flag");
            GameObjectUtility.SetStaticEditorFlags(target, value);
            EditorUtility.SetDirty(target);
            return 1;
        }
    }
}
