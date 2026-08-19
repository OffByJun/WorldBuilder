using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor
{
    public sealed class WorldBuilderWindow : EditorWindow
    {
        private const string UIPath = "Packages/com.emiteat.worldbuilder/Editor/UI/";

        private WorldBuilderUIController controller;

        private Label titleLabel;
        private Label statusLabel;
        private Button languageToggle;
        private ScrollView toolList;
        private VisualElement inspectorContainer;

        [MenuItem("WorldBuilder/Open")]
        public static void Open()
        {
            WorldBuilderWindow window = GetWindow<WorldBuilderWindow>();
            window.titleContent = new GUIContent("WorldBuilder");
            window.minSize = new Vector2(600f, 400f);
            window.Show();
        }

        public void CreateGUI()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIPath + "WorldBuilderWindow.uxml");
            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UIPath + "WorldBuilderWindow.uss");
            VisualTreeAsset itemAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIPath + "ToolListItem.uxml");

            tree.CloneTree(rootVisualElement);
            rootVisualElement.styleSheets.Add(style);

            titleLabel = rootVisualElement.Q<Label>("title");
            statusLabel = rootVisualElement.Q<Label>("status-label");
            languageToggle = rootVisualElement.Q<Button>("language-toggle");
            toolList = rootVisualElement.Q<ScrollView>("tool-list");
            inspectorContainer = rootVisualElement.Q<VisualElement>("inspector-container");

            controller = new WorldBuilderUIController(itemAsset);
            controller.ToolSelected += OnToolSelected;

            languageToggle.clicked += OnLanguageToggleClicked;

            EnableTools();
            Rebuild();

            rootVisualElement.schedule.Execute(UpdateStatus).Every(500);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void OnSceneGui(SceneView sceneView)
        {
            IWorldBuilderTool tool = controller?.SelectedTool;
            if (tool != null)
            {
                tool.OnSceneGUI();
            }
        }

        private void EnableTools()
        {
            IReadOnlyList<IWorldBuilderTool> tools = WorldBuilderToolRegistry.GetAll();
            for (int i = 0; i < tools.Count; i++)
            {
                tools[i].OnEnable();
            }
        }

        private void Rebuild()
        {
            titleLabel.text = WorldBuilderLocalization.Get("header.title");
            languageToggle.text = WorldBuilderLocalization.Current == WorldBuilderLocalization.Language.Korean ? "KO" : "EN";

            controller.BuildToolList(toolList, inspectorContainer);

            IWorldBuilderTool toSelect = controller.SelectedTool;
            IReadOnlyList<IWorldBuilderTool> tools = WorldBuilderToolRegistry.GetAll();
            if (toSelect == null && tools.Count > 0)
            {
                toSelect = tools[0];
            }

            if (toSelect != null)
            {
                controller.OnToolSelected(toSelect, inspectorContainer);
            }
            else
            {
                statusLabel.text = WorldBuilderLocalization.Get("status.ready");
            }
        }

        private void OnToolSelected(IWorldBuilderTool tool)
        {
            statusLabel.text = tool != null ? tool.ToolName : WorldBuilderLocalization.Get("status.ready");
        }

        private void OnLanguageToggleClicked()
        {
            controller.OnLanguageToggle(statusLabel);
            Rebuild();
        }

        private void UpdateStatus()
        {
            IWorldBuilderTool tool = controller.SelectedTool;
            if (tool == null)
            {
                statusLabel.text = WorldBuilderLocalization.Get("status.ready");
                return;
            }

            IReadOnlyList<string> history = UndoHistory.Entries;
            if (history.Count > 0)
            {
                statusLabel.text = tool.ToolName + "  |  " + history[history.Count - 1];
            }
            else
            {
                statusLabel.text = tool.ToolName;
            }
        }
    }
}
