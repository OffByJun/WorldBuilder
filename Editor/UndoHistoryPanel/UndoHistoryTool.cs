using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.UndoHistoryPanel
{
    public sealed class UndoHistoryTool : IWorldBuilderTool
    {
        public string ToolName => WorldBuilderLocalization.Get("tool.undoHistory");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.undoHistory"));

            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.SpaceBetween;

            Label label = new Label("History");
            Button clear = new Button(UndoHistory.Clear) { text = WorldBuilderLocalization.Get("btn.clear") };
            bar.Add(label);
            bar.Add(clear);
            root.Add(bar);

            VisualElement list = new VisualElement();
            root.Add(list);

            int lastCount = -1;
            root.schedule.Execute(() =>
            {
                int count = UndoHistory.Entries.Count;
                if (count != lastCount)
                {
                    lastCount = count;
                    RebuildList(list);
                }
            }).Every(300);

            return root;
        }

        public void OnSceneGUI()
        {
        }

        private void RebuildList(VisualElement list)
        {
            list.Clear();

            IReadOnlyList<string> entries = UndoHistory.Entries;
            if (entries.Count == 0)
            {
                list.Add(new HelpBox("No recorded operations.", HelpBoxMessageType.Info));
                return;
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Button entry = new Button(() =>
                {
                    Undo.PerformUndo();
                    SceneView.RepaintAll();
                })
                {
                    text = entries[i]
                };
                list.Add(entry);
            }
        }
    }
}
