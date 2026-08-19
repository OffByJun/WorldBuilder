using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PlacementRuleTool
{
    [Serializable]
    public class PlacementRule
    {
        public string label;
        public GameObject sourceType;
        public GameObject allowedNeighbor;
        public float minDistance;
        public float maxDistance;
    }

    public sealed class PlacementRuleTool : IWorldBuilderTool
    {
        private readonly List<PlacementRule> rules = new List<PlacementRule>();
        private readonly List<GameObject> violations = new List<GameObject>();

        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.placementRule");
        public string Category => WorldBuilderCategory.Automation;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.placementRule"));

            ListView list = new ListView(rules, 120, MakeItem, BindItem)
            {
                showAddRemoveFooter = true,
                reorderable = false,
                selectionType = SelectionType.Single,
                showBoundCollectionSize = false
            };
            list.itemsAdded += indices => EnsureRules();
            list.style.minHeight = 200f;
            root.Add(list);

            Button validate = new Button(Validate) { text = "Validate" };
            root.Add(validate);

            resultLabel = new Label("0");
            root.Add(resultLabel);

            return root;
        }

        private void EnsureRules()
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null)
                {
                    rules[i] = new PlacementRule { label = "Rule " + (i + 1) };
                }
            }
        }

        private VisualElement MakeItem()
        {
            VisualElement box = new VisualElement();
            box.style.borderBottomWidth = 1f;
            box.style.borderBottomColor = new Color(0f, 0f, 0f, 0.2f);
            box.style.paddingBottom = 4f;

            TextField label = new TextField("Label") { name = "label" };
            box.Add(label);

            ObjectField source = new ObjectField("Source") { name = "source", objectType = typeof(GameObject), allowSceneObjects = false };
            box.Add(source);

            ObjectField neighbor = new ObjectField("Neighbor") { name = "neighbor", objectType = typeof(GameObject), allowSceneObjects = false };
            box.Add(neighbor);

            FloatField min = new FloatField("Min Distance") { name = "min" };
            box.Add(min);

            FloatField max = new FloatField("Max Distance") { name = "max" };
            box.Add(max);

            return box;
        }

        private void BindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= rules.Count)
            {
                return;
            }

            if (rules[index] == null)
            {
                rules[index] = new PlacementRule { label = "Rule " + (index + 1) };
            }

            PlacementRule rule = rules[index];

            TextField label = element.Q<TextField>("label");
            label.SetValueWithoutNotify(rule.label);
            label.RegisterValueChangedCallback(evt => rule.label = evt.newValue);

            ObjectField source = element.Q<ObjectField>("source");
            source.SetValueWithoutNotify(rule.sourceType);
            source.RegisterValueChangedCallback(evt => rule.sourceType = evt.newValue as GameObject);

            ObjectField neighbor = element.Q<ObjectField>("neighbor");
            neighbor.SetValueWithoutNotify(rule.allowedNeighbor);
            neighbor.RegisterValueChangedCallback(evt => rule.allowedNeighbor = evt.newValue as GameObject);

            FloatField min = element.Q<FloatField>("min");
            min.SetValueWithoutNotify(rule.minDistance);
            min.RegisterValueChangedCallback(evt => rule.minDistance = evt.newValue);

            FloatField max = element.Q<FloatField>("max");
            max.SetValueWithoutNotify(rule.maxDistance);
            max.RegisterValueChangedCallback(evt => rule.maxDistance = evt.newValue);
        }

        private void Validate()
        {
            violations.Clear();

            List<GameObject> all = SceneObjectCollector.CollectGameObjects(true);

            for (int r = 0; r < rules.Count; r++)
            {
                PlacementRule rule = rules[r];
                if (rule == null || rule.sourceType == null || rule.allowedNeighbor == null)
                {
                    continue;
                }

                List<GameObject> sources = InstancesOf(all, rule.sourceType);
                List<GameObject> neighbors = InstancesOf(all, rule.allowedNeighbor);

                for (int s = 0; s < sources.Count; s++)
                {
                    if (!HasValidNeighbor(sources[s], neighbors, rule.minDistance, rule.maxDistance))
                    {
                        if (!violations.Contains(sources[s]))
                        {
                            violations.Add(sources[s]);
                        }
                    }
                }
            }

            if (resultLabel != null)
            {
                resultLabel.text = "Violations: " + violations.Count;
            }

            SceneView.RepaintAll();
        }

        private bool HasValidNeighbor(GameObject source, List<GameObject> neighbors, float minDistance, float maxDistance)
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] == source)
                {
                    continue;
                }

                float distance = Vector3.Distance(source.transform.position, neighbors[i].transform.position);
                if (distance >= minDistance && distance <= maxDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private List<GameObject> InstancesOf(List<GameObject> all, GameObject prefab)
        {
            List<GameObject> result = new List<GameObject>();

            for (int i = 0; i < all.Count; i++)
            {
                GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(all[i]);
                GameObject root = asset != null ? asset.transform.root.gameObject : null;
                if (asset == prefab || root == prefab)
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            Handles.color = Color.red;

            for (int i = 0; i < violations.Count; i++)
            {
                if (violations[i] == null)
                {
                    continue;
                }

                Bounds bounds = ComputeBounds(violations[i]);
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
