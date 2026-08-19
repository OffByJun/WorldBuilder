using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.SceneSearchTool
{
    public enum SearchFilterType
    {
        Name,
        Component,
        Layer,
        Tag
    }

    public sealed class SceneSearchTool : IWorldBuilderTool
    {
        [SerializeField] private SearchFilterType filterType = SearchFilterType.Name;

        private readonly List<GameObject> results = new List<GameObject>();

        private string query = string.Empty;
        private ListView listView;
        private Label countLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.sceneSearch");
        public string Category => WorldBuilderCategory.Productivity;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.sceneSearch"));

            TextField queryField = new TextField("Query") { value = query };
            queryField.RegisterValueChangedCallback(evt =>
            {
                query = evt.newValue;
                Refresh();
            });
            root.Add(queryField);

            EnumField filterField = new EnumField("Filter", filterType);
            filterField.RegisterValueChangedCallback(evt =>
            {
                filterType = (SearchFilterType)evt.newValue;
                Refresh();
            });
            root.Add(filterField);

            countLabel = new Label("0");
            root.Add(countLabel);

            listView = new ListView(results, 20, () => new Label(), BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 200f;
            listView.selectionChanged += OnSelectionChanged;
            root.Add(listView);

            Refresh();
            return root;
        }

        private void BindItem(VisualElement element, int index)
        {
            Label label = (Label)element;
            label.text = results[index] != null ? results[index].name : string.Empty;
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            int index = listView.selectedIndex;
            if (index < 0 || index >= results.Count || results[index] == null)
            {
                return;
            }

            Selection.activeGameObject = results[index];
            EditorGUIUtility.PingObject(results[index]);
            SceneView.RepaintAll();
        }

        private void Refresh()
        {
            results.Clear();

            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);
            for (int i = 0; i < all.Count; i++)
            {
                if (Matches(all[i]))
                {
                    results.Add(all[i]);
                }
            }

            listView?.Rebuild();
            if (countLabel != null)
            {
                countLabel.text = results.Count.ToString();
            }

            SceneView.RepaintAll();
        }

        private bool Matches(GameObject target)
        {
            if (string.IsNullOrEmpty(query))
            {
                return false;
            }

            switch (filterType)
            {
                case SearchFilterType.Component:
                    return HasComponentNamed(target, query);
                case SearchFilterType.Layer:
                    return LayerMask.LayerToName(target.layer).IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
                case SearchFilterType.Tag:
                    return target.tag.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    return target.name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private bool HasComponentNamed(GameObject target, string componentName)
        {
            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                if (components[i].GetType().Name.IndexOf(componentName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            Handles.color = Color.cyan;

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] == null)
                {
                    continue;
                }

                Bounds bounds = ComputeBounds(results[i]);
                Handles.DrawWireCube(bounds.center, bounds.size);
            }
        }

        private Bounds ComputeBounds(GameObject target)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds;
            }

            return new Bounds(target.transform.position, Vector3.one);
        }
    }
}
