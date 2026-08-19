using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.TransformBatchTool
{
    public sealed class TransformBatchTool : IWorldBuilderTool
    {
        [SerializeField] private AlignAxis alignAxis = AlignAxis.X;
        [SerializeField] private AlignMode alignMode = AlignMode.Center;
        [SerializeField] private AlignAxis distributeAxis = AlignAxis.X;
        [SerializeField] private float distributeSpacing = 2f;
        [SerializeField] private bool resetPosition = true;
        [SerializeField] private bool resetRotation = true;
        [SerializeField] private bool resetScale = true;

        public string ToolName => WorldBuilderLocalization.Get("tool.transformBatch");
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

            root.Add(InspectorHelp.Build(ToolName, "help.transformBatch"));

            TabView tabs = new TabView();
            tabs.Add(BuildAlignTab());
            tabs.Add(BuildDistributeTab());
            tabs.Add(BuildResetTab());
            root.Add(tabs);

            return root;
        }

        private Tab BuildAlignTab()
        {
            Tab tab = new Tab("Align");

            EnumField axisField = new EnumField("Axis", alignAxis);
            axisField.RegisterValueChangedCallback(evt => alignAxis = (AlignAxis)evt.newValue);
            tab.Add(axisField);

            EnumField modeField = new EnumField("Mode", alignMode);
            modeField.RegisterValueChangedCallback(evt => alignMode = (AlignMode)evt.newValue);
            tab.Add(modeField);

            Button align = new Button(() => TransformBatchService.Align(Targets(), alignAxis, alignMode)) { text = "Align" };
            tab.Add(align);

            return tab;
        }

        private Tab BuildDistributeTab()
        {
            Tab tab = new Tab("Distribute");

            EnumField axisField = new EnumField("Axis", distributeAxis);
            axisField.RegisterValueChangedCallback(evt => distributeAxis = (AlignAxis)evt.newValue);
            tab.Add(axisField);

            FloatField spacingField = new FloatField("Spacing") { value = distributeSpacing };
            spacingField.RegisterValueChangedCallback(evt => distributeSpacing = evt.newValue);
            tab.Add(spacingField);

            Button distribute = new Button(() => TransformBatchService.Distribute(Targets(), distributeAxis, distributeSpacing)) { text = "Distribute" };
            tab.Add(distribute);

            return tab;
        }

        private Tab BuildResetTab()
        {
            Tab tab = new Tab("Reset");

            Toggle position = new Toggle("Position") { value = resetPosition };
            position.RegisterValueChangedCallback(evt => resetPosition = evt.newValue);
            tab.Add(position);

            Toggle rotation = new Toggle("Rotation") { value = resetRotation };
            rotation.RegisterValueChangedCallback(evt => resetRotation = evt.newValue);
            tab.Add(rotation);

            Toggle scale = new Toggle("Scale") { value = resetScale };
            scale.RegisterValueChangedCallback(evt => resetScale = evt.newValue);
            tab.Add(scale);

            Button reset = new Button(() => TransformBatchService.Reset(Targets(), resetPosition, resetRotation, resetScale)) { text = "Reset" };
            tab.Add(reset);

            return tab;
        }

        private List<GameObject> Targets()
        {
            return new List<GameObject>(Selection.gameObjects);
        }
    }
}
