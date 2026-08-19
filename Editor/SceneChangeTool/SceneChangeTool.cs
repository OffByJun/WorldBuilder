using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.SceneChangeTool
{
    public sealed class SceneChangeTool : IWorldBuilderTool
    {
        private enum ChangeType
        {
            Added,
            Removed,
            Moved
        }

        [Serializable]
        private struct BaselineEntry
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [Serializable]
        private sealed class BaselineCollection
        {
            public List<BaselineEntry> entries = new List<BaselineEntry>();
        }

        private struct ChangeEntry
        {
            public string name;
            public ChangeType type;
            public Vector3 position;
            public GameObject target;
        }

        private const string PrefKey = "WB_SceneChangeBaseline";

        private readonly List<ChangeEntry> changes = new List<ChangeEntry>();
        private readonly List<string> changeStrings = new List<string>();

        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.sceneChange");
        public string Category => WorldBuilderCategory.Collaboration;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.sceneChange"));

            Button save = new Button(SaveBaseline) { text = "Save Baseline" };
            root.Add(save);

            Button scan = new Button(ScanChanges) { text = "Scan Changes" };
            root.Add(scan);

            listView = new ListView(changeStrings, 20, () => new Label(), BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 220f;
            listView.selectionChanged += OnSelectionChanged;
            root.Add(listView);

            return root;
        }

        private void BindItem(VisualElement element, int index)
        {
            Label label = (Label)element;
            label.text = changeStrings[index];
            label.style.color = ColorFor(changes[index].type);
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            int index = listView.selectedIndex;
            if (index < 0 || index >= changes.Count || changes[index].target == null)
            {
                return;
            }

            Selection.activeGameObject = changes[index].target;
        }

        private void SaveBaseline()
        {
            BaselineCollection collection = new BaselineCollection();
            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);
            for (int i = 0; i < all.Count; i++)
            {
                Transform t = all[i].transform;
                collection.entries.Add(new BaselineEntry
                {
                    name = all[i].name,
                    position = t.position,
                    rotation = t.rotation,
                    scale = t.localScale
                });
            }

            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(collection));
        }

        private void ScanChanges()
        {
            changes.Clear();
            changeStrings.Clear();

            string json = EditorPrefs.GetString(PrefKey, string.Empty);
            BaselineCollection baseline = string.IsNullOrEmpty(json) ? new BaselineCollection() : JsonUtility.FromJson<BaselineCollection>(json);

            Dictionary<string, BaselineEntry> baselineMap = new Dictionary<string, BaselineEntry>();
            for (int i = 0; i < baseline.entries.Count; i++)
            {
                baselineMap[baseline.entries[i].name] = baseline.entries[i];
            }

            Dictionary<string, GameObject> currentMap = new Dictionary<string, GameObject>();
            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);
            for (int i = 0; i < all.Count; i++)
            {
                currentMap[all[i].name] = all[i];
            }

            foreach (KeyValuePair<string, GameObject> pair in currentMap)
            {
                if (!baselineMap.TryGetValue(pair.Key, out BaselineEntry entry))
                {
                    AddChange(pair.Key, ChangeType.Added, pair.Value.transform.position, pair.Value);
                    continue;
                }

                Transform t = pair.Value.transform;
                if (t.position != entry.position || t.rotation != entry.rotation || t.localScale != entry.scale)
                {
                    AddChange(pair.Key, ChangeType.Moved, t.position, pair.Value);
                }
            }

            foreach (KeyValuePair<string, BaselineEntry> pair in baselineMap)
            {
                if (!currentMap.ContainsKey(pair.Key))
                {
                    AddChange(pair.Key, ChangeType.Removed, pair.Value.position, null);
                }
            }

            listView?.Rebuild();
            SceneView.RepaintAll();
        }

        private void AddChange(string name, ChangeType type, Vector3 position, GameObject target)
        {
            changes.Add(new ChangeEntry { name = name, type = type, position = position, target = target });
            changeStrings.Add("[" + type + "] " + name);
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            for (int i = 0; i < changes.Count; i++)
            {
                Handles.color = ColorFor(changes[i].type);
                Vector3 center = changes[i].target != null ? changes[i].target.transform.position : changes[i].position;
                Handles.DrawWireCube(center, Vector3.one);
            }
        }

        private Color ColorFor(ChangeType type)
        {
            switch (type)
            {
                case ChangeType.Added:
                    return Color.green;
                case ChangeType.Removed:
                    return Color.red;
                default:
                    return Color.yellow;
            }
        }
    }
}
