using System.Collections.Generic;

namespace WorldBuilder.Editor
{
    public static class UndoHistory
    {
        private const int MaxEntries = 50;
        private static readonly List<string> Labels = new List<string>();

        public static IReadOnlyList<string> Entries => Labels;

        public static void Push(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            Labels.Add(label);

            while (Labels.Count > MaxEntries)
            {
                Labels.RemoveAt(0);
            }
        }

        public static void Clear()
        {
            Labels.Clear();
        }
    }
}
