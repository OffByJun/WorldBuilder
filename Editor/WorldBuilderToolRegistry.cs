using System.Collections.Generic;

namespace WorldBuilder.Editor
{
    public static class WorldBuilderToolRegistry
    {
        private static readonly List<IWorldBuilderTool> Tools = new List<IWorldBuilderTool>();

        public static void Register(IWorldBuilderTool tool)
        {
            if (tool == null)
            {
                return;
            }

            if (Tools.Contains(tool))
            {
                return;
            }

            Tools.Add(tool);
        }

        public static IReadOnlyList<IWorldBuilderTool> GetAll()
        {
            return Tools;
        }

        public static T GetByInterface<T>() where T : class
        {
            for (int i = 0; i < Tools.Count; i++)
            {
                if (Tools[i] is T match) return match;
            }
            return null;
        }
    }
}
