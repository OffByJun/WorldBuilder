using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.MaterialCompareTool
{
    public sealed class MaterialCompareTool : IWorldBuilderTool
    {
        private static readonly Color DiffColor = new Color(1f, 0.549f, 0f);

        private readonly List<MaterialComparisonRow> rows = new List<MaterialComparisonRow>();

        private Material left;
        private Material right;
        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.materialCompare");
        public string Category => WorldBuilderCategory.Rendering;

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

            root.Add(InspectorHelp.Build(ToolName, "help.materialCompare"));

            ObjectField leftField = new ObjectField("Left")
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            leftField.RegisterValueChangedCallback(evt => left = evt.newValue as Material);
            root.Add(leftField);

            ObjectField rightField = new ObjectField("Right")
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            rightField.RegisterValueChangedCallback(evt => right = evt.newValue as Material);
            root.Add(rightField);

            Button compare = new Button(Compare) { text = "Compare" };
            root.Add(compare);

            listView = new ListView(rows, 22, MakeItem, BindItem)
            {
                selectionType = SelectionType.None
            };
            listView.style.minHeight = 240f;
            root.Add(listView);

            return root;
        }

        private VisualElement MakeItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Label name = new Label { name = "name" };
            name.style.width = 140f;
            row.Add(name);

            Label leftValue = new Label { name = "left" };
            leftValue.style.flexGrow = 1f;
            row.Add(leftValue);

            Label rightValue = new Label { name = "right" };
            rightValue.style.flexGrow = 1f;
            row.Add(rightValue);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            MaterialComparisonRow data = rows[index];

            Label name = element.Q<Label>("name");
            Label leftValue = element.Q<Label>("left");
            Label rightValue = element.Q<Label>("right");

            name.text = data.propertyName;
            leftValue.text = data.leftValue;
            rightValue.text = data.rightValue;

            Color color = data.different ? DiffColor : Color.white;
            name.style.color = color;
            leftValue.style.color = color;
            rightValue.style.color = color;
        }

        private void Compare()
        {
            rows.Clear();
            rows.AddRange(MaterialCompareService.Compare(left, right));
            listView?.Rebuild();
        }
    }
}
