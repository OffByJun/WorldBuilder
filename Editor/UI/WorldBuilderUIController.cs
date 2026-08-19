using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor
{
    public sealed class WorldBuilderUIController
    {
        private const string FoldoutPrefKeyPrefix = "WB_Category_";

        private static readonly string[] CategoryOrder =
        {
            WorldBuilderCategory.Hub,
            WorldBuilderCategory.Productivity,
            WorldBuilderCategory.Debug,
            WorldBuilderCategory.Automation,
            WorldBuilderCategory.Import,
            WorldBuilderCategory.Rendering,
            WorldBuilderCategory.Audio,
            WorldBuilderCategory.Build,
            WorldBuilderCategory.Collaboration,
            WorldBuilderCategory.Physics,
            WorldBuilderCategory.World,
            WorldBuilderCategory.AstraNope,
            WorldBuilderCategory.Fallback
        };

        private readonly VisualTreeAsset itemAsset;
        private readonly Dictionary<IWorldBuilderTool, Button> buttons = new Dictionary<IWorldBuilderTool, Button>();

        private Button selectedButton;

        public IWorldBuilderTool SelectedTool { get; private set; }

        public event Action<IWorldBuilderTool> ToolSelected;

        public WorldBuilderUIController(VisualTreeAsset itemAsset)
        {
            this.itemAsset = itemAsset;
        }

        public void BuildToolList(VisualElement container, VisualElement inspectorContainer)
        {
            container.Clear();
            buttons.Clear();
            selectedButton = null;

            Dictionary<string, List<IWorldBuilderTool>> grouped = GroupByCategory(WorldBuilderToolRegistry.GetAll());

            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                AppendCategory(container, inspectorContainer, CategoryOrder[i], grouped);
                grouped.Remove(CategoryOrder[i]);
            }

            foreach (KeyValuePair<string, List<IWorldBuilderTool>> remaining in grouped)
            {
                AppendCategory(container, inspectorContainer, remaining.Key, remaining.Value);
            }
        }

        public void OnToolSelected(IWorldBuilderTool tool, VisualElement inspectorContainer)
        {
            if (selectedButton != null)
            {
                selectedButton.RemoveFromClassList("selected");
                selectedButton = null;
            }

            SelectedTool = tool;
            inspectorContainer.Clear();

            if (tool == null)
            {
                ToolSelected?.Invoke(null);
                return;
            }

            if (buttons.TryGetValue(tool, out Button button))
            {
                button.AddToClassList("selected");
                selectedButton = button;
            }

            tool.OnEnable();
            inspectorContainer.Add(tool.CreateInspectorGUI());

            ToolSelected?.Invoke(tool);
        }

        public void SelectTool(IWorldBuilderTool tool, VisualElement inspectorContainer)
        {
            OnToolSelected(tool, inspectorContainer);
        }

        public void OnLanguageToggle(Label statusLabel)
        {
            WorldBuilderLocalization.Current =
                WorldBuilderLocalization.Current == WorldBuilderLocalization.Language.Korean
                    ? WorldBuilderLocalization.Language.English
                    : WorldBuilderLocalization.Language.Korean;

            statusLabel.text = WorldBuilderLocalization.Get("status.ready");
        }

        private void AppendCategory(
            VisualElement container,
            VisualElement inspectorContainer,
            string category,
            Dictionary<string, List<IWorldBuilderTool>> grouped)
        {
            if (grouped.TryGetValue(category, out List<IWorldBuilderTool> tools))
            {
                AppendCategory(container, inspectorContainer, category, tools);
            }
        }

        private void AppendCategory(
            VisualElement container,
            VisualElement inspectorContainer,
            string category,
            List<IWorldBuilderTool> tools)
        {
            Foldout foldout = CreateCategoryFoldout(category);

            for (int i = 0; i < tools.Count; i++)
            {
                foldout.Add(CreateToolItem(tools[i], inspectorContainer));
            }

            container.Add(foldout);
        }

        private static Dictionary<string, List<IWorldBuilderTool>> GroupByCategory(IReadOnlyList<IWorldBuilderTool> tools)
        {
            Dictionary<string, List<IWorldBuilderTool>> grouped = new Dictionary<string, List<IWorldBuilderTool>>();

            for (int i = 0; i < tools.Count; i++)
            {
                IWorldBuilderTool tool = tools[i];
                string category = string.IsNullOrEmpty(tool.Category) ? WorldBuilderCategory.Fallback : tool.Category;

                if (!grouped.TryGetValue(category, out List<IWorldBuilderTool> list))
                {
                    list = new List<IWorldBuilderTool>();
                    grouped[category] = list;
                }

                list.Add(tool);
            }

            return grouped;
        }

        private static Foldout CreateCategoryFoldout(string category)
        {
            Foldout foldout = new Foldout { text = category };
            foldout.AddToClassList("category-foldout");

            string key = FoldoutPrefKeyPrefix + category;
            foldout.SetValueWithoutNotify(EditorPrefs.GetBool(key, true));
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                {
                    EditorPrefs.SetBool(key, evt.newValue);
                }
            });

            return foldout;
        }

        private VisualElement CreateToolItem(IWorldBuilderTool tool, VisualElement inspectorContainer)
        {
            VisualElement item = itemAsset.Instantiate();
            Button button = item.Q<Button>("tool-button");
            Image icon = item.Q<Image>("tool-icon");
            Label label = item.Q<Label>("tool-name");

            icon.image = tool.ToolIcon;
            label.text = tool.ToolName;
            button.userData = tool;
            button.clicked += () => OnToolSelected(tool, inspectorContainer);

            buttons[tool] = button;

            return item;
        }
    }
}
