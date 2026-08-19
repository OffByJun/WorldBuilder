using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Collab;

namespace WorldBuilder.Editor.ObjectOwnerTool
{
    public sealed class ObjectOwnerTool : IWorldBuilderTool
    {
        [SerializeField] private string ownerName = string.Empty;
        [SerializeField] private Color ownerColor = Color.cyan;

        private readonly List<OwnerTag> tags = new List<OwnerTag>();
        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.objectOwner");
        public string Category => WorldBuilderCategory.Collaboration;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.objectOwner"));

            TextField nameField = new TextField("Owner Name") { value = ownerName };
            nameField.RegisterValueChangedCallback(evt => ownerName = evt.newValue);
            root.Add(nameField);

            ColorField colorField = new ColorField("Owner Color") { value = ownerColor };
            colorField.RegisterValueChangedCallback(evt => ownerColor = evt.newValue);
            root.Add(colorField);

            Button tagButton = new Button(TagSelected) { text = "Tag Selected" };
            root.Add(tagButton);

            listView = new ListView(tags, 22, () => new Label(), BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 200f;
            listView.selectionChanged += OnSelectionChanged;
            root.Add(listView);

            RefreshList();
            return root;
        }

        private void BindItem(VisualElement element, int index)
        {
            Label label = (Label)element;
            if (tags[index] == null)
            {
                label.text = string.Empty;
                return;
            }

            label.text = tags[index].OwnerName + "  -  " + tags[index].gameObject.name;
            label.style.color = tags[index].OwnerColor;
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            int index = listView.selectedIndex;
            if (index < 0 || index >= tags.Count || tags[index] == null)
            {
                return;
            }

            Selection.activeGameObject = tags[index].gameObject;
        }

        private void TagSelected()
        {
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                OwnerTag tag = selected[i].GetComponent<OwnerTag>();
                if (tag == null)
                {
                    tag = Undo.AddComponent<OwnerTag>(selected[i]);
                }
                else
                {
                    Undo.RecordObject(tag, "Tag Owner");
                }

                tag.OwnerName = ownerName;
                tag.OwnerColor = ownerColor;
                EditorUtility.SetDirty(tag);
            }

            RefreshList();
        }

        private void RefreshList()
        {
            tags.Clear();
            tags.AddRange(SceneObjectCollector.CollectComponents<OwnerTag>(true));
            listView?.Rebuild();
            SceneView.RepaintAll();
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == null)
                {
                    continue;
                }

                Handles.color = tags[i].OwnerColor;
                Vector3 position = tags[i].transform.position;
                Handles.DrawWireCube(position, Vector3.one);
                Handles.Label(position, tags[i].OwnerName);
            }
        }
    }
}
