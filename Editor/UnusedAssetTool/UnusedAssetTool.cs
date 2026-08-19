using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.UnusedAssetTool
{
    public sealed class UnusedAssetTool : IWorldBuilderTool
    {
        private readonly List<string> unused = new List<string>();

        private DefaultAsset folder;
        private ListView listView;
        private Label statusLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.unusedAsset");
        public string Category => WorldBuilderCategory.Build;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.unusedAsset"));

            ObjectField folderField = new ObjectField("Search Folder")
            {
                objectType = typeof(DefaultAsset),
                allowSceneObjects = false
            };
            folderField.RegisterValueChangedCallback(evt => folder = evt.newValue as DefaultAsset);
            root.Add(folderField);

            Button scan = new Button(Scan) { text = "Scan" };
            root.Add(scan);

            statusLabel = new Label("0");
            root.Add(statusLabel);

            listView = new ListView(unused, 24, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 220f;
            root.Add(listView);

            Button deleteAll = new Button(DeleteAll) { text = "Delete All" };
            root.Add(deleteAll);

            return root;
        }

        private VisualElement MakeItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Label path = new Label { name = "path" };
            path.style.flexGrow = 1f;
            path.style.overflow = Overflow.Hidden;
            row.Add(path);

            Button ping = new Button { text = "Ping", name = "ping" };
            row.Add(ping);

            Button delete = new Button { text = "Delete", name = "delete" };
            row.Add(delete);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            string path = unused[index];
            element.Q<Label>("path").text = path;
            element.Q<Button>("ping").clickable = new Clickable(() => Ping(path));
            element.Q<Button>("delete").clickable = new Clickable(() => Delete(path));
        }

        private void Scan()
        {
            unused.Clear();
            unused.AddRange(UnusedAssetService.Scan(folder != null ? AssetDatabase.GetAssetPath(folder) : string.Empty));
            listView?.Rebuild();
            SetStatus();
        }

        private void Ping(string path)
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
            }
        }

        private void Delete(string path)
        {
            if (AssetDatabase.DeleteAsset(path))
            {
                unused.Remove(path);
                listView?.Rebuild();
                SetStatus();
            }
        }

        private void DeleteAll()
        {
            if (unused.Count == 0)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Unused Assets",
                "Delete " + unused.Count + " unused assets? This cannot be undone.",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            for (int i = 0; i < unused.Count; i++)
            {
                AssetDatabase.DeleteAsset(unused[i]);
            }

            unused.Clear();
            AssetDatabase.Refresh();
            listView?.Rebuild();
            SetStatus();
        }

        private void SetStatus()
        {
            if (statusLabel != null)
            {
                statusLabel.text = "Unused: " + unused.Count;
            }
        }
    }
}
