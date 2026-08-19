using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor
{
    public interface IWorldBuilderTool
    {
        string ToolName { get; }
        string Category { get; }
        Texture2D ToolIcon { get; }

        void OnEnable();
        void OnSceneGUI();
        VisualElement CreateInspectorGUI();
    }
}
