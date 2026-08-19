using System.Collections.Generic;

namespace WorldBuilder.Editor.PrefabBrush
{
    public static class ModifierNodeRegistry
    {
        private static readonly List<IModifierNode> Prototypes = new List<IModifierNode>();

        public static IReadOnlyList<IModifierNode> GetAll()
        {
            return Prototypes;
        }

        public static void Register(IModifierNode node)
        {
            if (node == null)
            {
                return;
            }

            Prototypes.Add(node);
        }

        public static void Clear()
        {
            Prototypes.Clear();
        }
    }
}
