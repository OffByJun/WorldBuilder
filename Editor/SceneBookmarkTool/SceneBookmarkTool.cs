using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.SceneBookmarkTool
{
    public sealed class SceneBookmarkTool : IWorldBuilderTool
    {
        [Serializable]
        public struct BookmarkEntry
        {
            public string label;
            public Vector3 position;
            public Quaternion rotation;
        }

        [Serializable]
        private sealed class BookmarkCollection
        {
            public List<BookmarkEntry> entries = new List<BookmarkEntry>();
        }

        private const string PrefKey = "WB_SceneBookmarks";

        private readonly List<BookmarkEntry> bookmarks = new List<BookmarkEntry>();

        private string pendingLabel = string.Empty;
        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.sceneBookmark");
        public string Category => WorldBuilderCategory.Productivity;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            Load();
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.sceneBookmark"));

            TextField nameField = new TextField("Bookmark Name") { value = pendingLabel };
            nameField.RegisterValueChangedCallback(evt => pendingLabel = evt.newValue);
            root.Add(nameField);

            Button save = new Button(() => Save(nameField)) { text = "Save" };
            root.Add(save);

            listView = new ListView(bookmarks, 22, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 160f;
            listView.selectionChanged += OnSelectionChanged;
            root.Add(listView);

            return root;
        }

        private VisualElement MakeItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;

            Label label = new Label();
            label.name = "label";
            label.style.flexGrow = 1f;
            row.Add(label);

            Button delete = new Button { text = "Delete" };
            delete.name = "delete";
            row.Add(delete);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            Label label = element.Q<Label>("label");
            label.text = bookmarks[index].label;

            Button delete = element.Q<Button>("delete");
            delete.clickable = new Clickable(() => Delete(index));
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            int index = listView.selectedIndex;
            if (index < 0 || index >= bookmarks.Count)
            {
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            BookmarkEntry entry = bookmarks[index];
            view.LookAt(entry.position, entry.rotation);
        }

        private void Save(TextField nameField)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            BookmarkEntry entry = new BookmarkEntry
            {
                label = string.IsNullOrEmpty(pendingLabel) ? "Bookmark " + (bookmarks.Count + 1) : pendingLabel,
                position = view.camera.transform.position,
                rotation = view.camera.transform.rotation
            };

            bookmarks.Add(entry);
            pendingLabel = string.Empty;
            nameField.SetValueWithoutNotify(string.Empty);

            Persist();
            listView?.Rebuild();
        }

        private void Delete(int index)
        {
            if (index < 0 || index >= bookmarks.Count)
            {
                return;
            }

            bookmarks.RemoveAt(index);
            Persist();
            listView?.Rebuild();
        }

        private void Persist()
        {
            BookmarkCollection collection = new BookmarkCollection();
            collection.entries.AddRange(bookmarks);
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(collection));
        }

        private void Load()
        {
            bookmarks.Clear();

            string json = EditorPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            BookmarkCollection collection = JsonUtility.FromJson<BookmarkCollection>(json);
            if (collection?.entries != null)
            {
                bookmarks.AddRange(collection.entries);
            }
        }
    }
}
