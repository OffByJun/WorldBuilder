using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush
{
    public interface IEnvironmentBrush
    {
        string DisplayName { get; }
        VisualElement BuildUI();
        void GenerateVariants(PrefabBrushSettings settings);
    }
}
