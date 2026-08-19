using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.SceneSnapshotTool
{
    [Serializable]
    public class SnapshotData
    {
        public string label;
        public string timestamp;
        public List<ObjectSnapshot> objects;
    }

    [Serializable]
    public class ObjectSnapshot
    {
        public string objectName;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public bool activeSelf;
    }

    public sealed class SceneSnapshotTool : IWorldBuilderTool
    {
        [Serializable]
        private sealed class SnapshotCollection
        {
            public List<SnapshotData> snapshots = new List<SnapshotData>();
        }

        private const string PrefKey = "WB_SceneSnapshots";

        private readonly List<SnapshotData> snapshots = new List<SnapshotData>();

        private string pendingLabel = string.Empty;
        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.sceneSnapshot");
        public string Category => WorldBuilderCategory.Automation;

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

            root.Add(InspectorHelp.Build(ToolName, "help.sceneSnapshot"));

            TextField nameField = new TextField("Snapshot Name") { value = pendingLabel };
            nameField.RegisterValueChangedCallback(evt => pendingLabel = evt.newValue);
            root.Add(nameField);

            Button save = new Button(() => Save(nameField)) { text = "Save" };
            root.Add(save);

            listView = new ListView(snapshots, 24, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 180f;
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

            Button restore = new Button { text = "Restore", name = "restore" };
            row.Add(restore);

            Button delete = new Button { text = "Delete", name = "delete" };
            row.Add(delete);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            Label label = element.Q<Label>("label");
            label.text = snapshots[index].label + "  (" + snapshots[index].timestamp + ")";

            Button restore = element.Q<Button>("restore");
            restore.clickable = new Clickable(() => Restore(index));

            Button delete = element.Q<Button>("delete");
            delete.clickable = new Clickable(() => Delete(index));
        }

        private void Save(TextField nameField)
        {
            SnapshotData data = new SnapshotData
            {
                label = string.IsNullOrEmpty(pendingLabel) ? "Snapshot " + (snapshots.Count + 1) : pendingLabel,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                objects = new List<ObjectSnapshot>()
            };

            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);
            for (int i = 0; i < all.Count; i++)
            {
                Transform t = all[i].transform;
                data.objects.Add(new ObjectSnapshot
                {
                    objectName = all[i].name,
                    position = t.position,
                    rotation = t.rotation,
                    scale = t.localScale,
                    activeSelf = all[i].activeSelf
                });
            }

            snapshots.Add(data);
            pendingLabel = string.Empty;
            nameField.SetValueWithoutNotify(string.Empty);

            Persist();
            listView?.Rebuild();
        }

        private void Restore(int index)
        {
            if (index < 0 || index >= snapshots.Count)
            {
                return;
            }

            Dictionary<string, GameObject> byName = new Dictionary<string, GameObject>();
            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);
            for (int i = 0; i < all.Count; i++)
            {
                byName[all[i].name] = all[i];
            }

            List<ObjectSnapshot> objects = snapshots[index].objects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!byName.TryGetValue(objects[i].objectName, out GameObject target) || target == null)
                {
                    continue;
                }

                Undo.RecordObject(target.transform, "Restore Snapshot");
                Undo.RecordObject(target, "Restore Snapshot");

                target.transform.position = objects[i].position;
                target.transform.rotation = objects[i].rotation;
                target.transform.localScale = objects[i].scale;
                target.SetActive(objects[i].activeSelf);

                EditorUtility.SetDirty(target);
            }
        }

        private void Delete(int index)
        {
            if (index < 0 || index >= snapshots.Count)
            {
                return;
            }

            snapshots.RemoveAt(index);
            Persist();
            listView?.Rebuild();
        }

        private void Persist()
        {
            SnapshotCollection collection = new SnapshotCollection();
            collection.snapshots.AddRange(snapshots);
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(collection));
        }

        private void Load()
        {
            snapshots.Clear();

            string json = EditorPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            SnapshotCollection collection = JsonUtility.FromJson<SnapshotCollection>(json);
            if (collection?.snapshots != null)
            {
                snapshots.AddRange(collection.snapshots);
            }
        }
    }
}
