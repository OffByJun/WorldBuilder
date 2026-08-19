using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush
{
    public sealed class ModifierGraphWindow : EditorWindow
    {
        private ModifierGraph graph;
        private ModifierGraphView view;
        private ObjectField graphField;
        private VisualElement inspectorPanel;

        [MenuItem("WorldBuilder/Modifier Graph")]
        public static void Open()
        {
            Open(null);
        }

        public static void Open(ModifierGraph target)
        {
            ModifierGraphWindow window = GetWindow<ModifierGraphWindow>();
            window.titleContent = new GUIContent("Modifier Graph");
            window.minSize = new Vector2(820f, 480f);
            if (target != null)
            {
                window.Load(target);
            }

            window.Show();
        }

        private void CreateGUI()
        {
            Toolbar toolbar = new Toolbar();
            graphField = new ObjectField("Graph") { objectType = typeof(ModifierGraph), value = graph };
            graphField.RegisterValueChangedCallback(evt => Load(evt.newValue as ModifierGraph));
            toolbar.Add(graphField);
            toolbar.Add(new ToolbarButton(Save) { text = "Save" });
            rootVisualElement.Add(toolbar);

            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            rootVisualElement.Add(body);

            view = new ModifierGraphView();
            view.style.flexGrow = 1;
            view.NodeSelected = OnNodeSelected;
            body.Add(view);

            inspectorPanel = new VisualElement();
            inspectorPanel.style.width = 300f;
            inspectorPanel.style.paddingLeft = 6f;
            inspectorPanel.style.paddingRight = 6f;
            inspectorPanel.style.paddingTop = 6f;
            inspectorPanel.style.borderLeftWidth = 1f;
            inspectorPanel.style.borderLeftColor = new Color(0f, 0f, 0f, 0.4f);
            body.Add(inspectorPanel);

            if (graph != null)
            {
                view.Populate(graph);
            }
        }

        private void Load(ModifierGraph target)
        {
            graph = target;
            graphField?.SetValueWithoutNotify(target);
            inspectorPanel?.Clear();
            view?.Populate(target);
        }

        private void OnNodeSelected(ModifierNodeBase node)
        {
            if (inspectorPanel == null)
            {
                return;
            }

            inspectorPanel.Clear();
            if (graph == null || node == null)
            {
                return;
            }

            int index = graph.nodes.IndexOf(node);
            if (index < 0)
            {
                return;
            }

            inspectorPanel.Add(new Label(node.NodeName) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4f } });

            SerializedObject serialized = new SerializedObject(graph);
            SerializedProperty element = serialized.FindProperty("nodes").GetArrayElementAtIndex(index);
            PropertyField field = new PropertyField(element);
            inspectorPanel.Add(field);
            inspectorPanel.Bind(serialized);
        }

        private void Save()
        {
            if (graph == null)
            {
                return;
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
        }
    }

    public sealed class ModifierGraphView : GraphView
    {
        private readonly Dictionary<string, Node> nodeViews = new Dictionary<string, Node>();
        private ModifierGraph graph;
        private bool building;

        public Action<ModifierNodeBase> NodeSelected;

        public ModifierGraphView()
        {
            style.flexGrow = 1;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            GridBackground grid = new GridBackground();
            grid.StretchToParentSize();
            Insert(0, grid);

            graphViewChanged = OnGraphViewChanged;
        }

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            NotifySelection();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            NotifySelection();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            NotifySelection();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> result = new List<Port>();
            ports.ForEach(port =>
            {
                if (startPort == port || startPort.node == port.node || startPort.direction == port.direction)
                {
                    return;
                }

                Port outputPort = startPort.direction == Direction.Output ? startPort : port;
                Port inputPort = startPort.direction == Direction.Input ? startPort : port;
                PortBinding outBinding = outputPort.userData as PortBinding;
                PortBinding inBinding = inputPort.userData as PortBinding;

                if (outBinding != null && inBinding != null && !inBinding.isChannel &&
                    outBinding.node != null && inBinding.node != null &&
                    IsUpstream(outBinding.node, inBinding.node, new HashSet<IModifierNode>()))
                {
                    return;
                }

                result.Add(port);
            });

            return result;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (graph != null)
            {
                Vector2 graphPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
                IReadOnlyList<IModifierNode> prototypes = ModifierNodeRegistry.GetAll();
                for (int i = 0; i < prototypes.Count; i++)
                {
                    if (prototypes[i] is ModifierNodeBase prototype)
                    {
                        ModifierNodeBase captured = prototype;
                        evt.menu.AppendAction($"{captured.Category}/{captured.NodeName}", action => CreateNode(captured, graphPosition));
                    }
                }
            }

            base.BuildContextualMenu(evt);
        }

        public void Populate(ModifierGraph target)
        {
            building = true;

            DeleteElements(graphElements.ToList());
            nodeViews.Clear();
            graph = target;

            if (graph != null)
            {
                CreateChannelNode(ModifierChannel.PositionOffset, 80f);
                CreateChannelNode(ModifierChannel.Rotation, 240f);
                CreateChannelNode(ModifierChannel.Scale, 400f);

                for (int i = 0; i < graph.nodes.Count; i++)
                {
                    if (graph.nodes[i] is ModifierNodeBase node)
                    {
                        CreateNodeView(node);
                    }
                }

                ReconnectEdges();
            }

            building = false;
        }

        private void CreateNode(ModifierNodeBase prototype, Vector2 position)
        {
            Undo.RecordObject(graph, "Add Modifier Node");
            ModifierNodeBase node = prototype.CreateInstance();
            node.GraphPosition = position;
            graph.nodes.Add(node);
            EditorUtility.SetDirty(graph);

            building = true;
            CreateNodeView(node);
            building = false;
        }

        private void CreateChannelNode(ModifierChannel channel, float y)
        {
            string guid = "__channel_" + channel;
            Node node = new Node { title = channel + " (Output)", viewDataKey = guid };

            for (int axis = 0; axis < 3; axis++)
            {
                Port input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(float));
                input.portName = ((Axis)axis).ToString();
                input.userData = new PortBinding { isChannel = true, channel = channel, index = axis };
                node.inputContainer.Add(input);
            }

            node.capabilities &= ~Capabilities.Deletable;
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.SetPosition(new Rect(760f, y, 170f, 140f));

            AddElement(node);
            nodeViews[guid] = node;
        }

        private void CreateNodeView(ModifierNodeBase node)
        {
            Node view = new Node { title = node.NodeName, viewDataKey = node.Guid };

            if (ColorUtility.TryParseHtmlString(CategoryColor(node.Category), out Color color))
            {
                view.titleContainer.style.backgroundColor = color;
            }

            for (int p = 0; p < node.InputPortCount; p++)
            {
                Port input = view.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(float));
                input.portName = node.GetInputPortName(p);
                input.userData = new PortBinding { node = node, index = p };
                view.inputContainer.Add(input);
            }

            Port output = view.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
            output.portName = "Out";
            output.userData = new PortBinding { node = node, isOutput = true };
            view.outputContainer.Add(output);

            view.RefreshExpandedState();
            view.RefreshPorts();
            view.SetPosition(new Rect(node.GraphPosition.x, node.GraphPosition.y, 190f, 130f));

            AddElement(view);
            nodeViews[node.Guid] = view;
        }

        private void ReconnectEdges()
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                if (!(graph.nodes[i] is ModifierNodeBase node))
                {
                    continue;
                }

                for (int p = 0; p < node.InputPortCount; p++)
                {
                    ConnectFromSource(node.GetInput(p), GetInputPortAt(node.Guid, p));
                }
            }

            foreach (ModifierChannel channel in (ModifierChannel[])Enum.GetValues(typeof(ModifierChannel)))
            {
                if (!nodeViews.TryGetValue("__channel_" + channel, out Node channelView))
                {
                    continue;
                }

                List<Port> channelPorts = channelView.inputContainer.Query<Port>().ToList();
                for (int axis = 0; axis < channelPorts.Count; axis++)
                {
                    ConnectFromSource(graph.GetChannelInput(channel, axis), channelPorts[axis]);
                }
            }
        }

        private void ConnectFromSource(IModifierNode source, Port inputPort)
        {
            if (source == null || inputPort == null)
            {
                return;
            }

            if (!nodeViews.TryGetValue(((ModifierNodeBase)source).Guid, out Node sourceView))
            {
                return;
            }

            Port outputPort = sourceView.outputContainer.Q<Port>();
            if (outputPort == null)
            {
                return;
            }

            AddElement(outputPort.ConnectTo(inputPort));
        }

        private Port GetInputPortAt(string guid, int index)
        {
            if (!nodeViews.TryGetValue(guid, out Node view))
            {
                return null;
            }

            List<Port> inputs = view.inputContainer.Query<Port>().ToList();
            return index >= 0 && index < inputs.Count ? inputs[index] : null;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (building || graph == null)
            {
                return change;
            }

            Undo.RecordObject(graph, "Edit Modifier Graph");

            if (change.elementsToRemove != null)
            {
                for (int i = 0; i < change.elementsToRemove.Count; i++)
                {
                    if (change.elementsToRemove[i] is Edge edge)
                    {
                        ClearConnection(edge);
                    }
                    else if (change.elementsToRemove[i] is Node node)
                    {
                        RemoveNodeView(node);
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                for (int i = 0; i < change.edgesToCreate.Count; i++)
                {
                    ApplyConnection(change.edgesToCreate[i]);
                }
            }

            if (change.movedElements != null)
            {
                for (int i = 0; i < change.movedElements.Count; i++)
                {
                    if (change.movedElements[i] is Node node)
                    {
                        UpdateNodePosition(node);
                    }
                }
            }

            EditorUtility.SetDirty(graph);
            return change;
        }

        private void ApplyConnection(Edge edge)
        {
            PortBinding from = edge.output?.userData as PortBinding;
            PortBinding to = edge.input?.userData as PortBinding;
            if (from == null || to == null || !from.isOutput || from.node == null)
            {
                return;
            }

            if (to.isChannel)
            {
                graph.SetChannelInput(to.channel, to.index, from.node);
            }
            else
            {
                to.node?.SetInput(to.index, from.node);
            }
        }

        private void ClearConnection(Edge edge)
        {
            PortBinding to = edge.input?.userData as PortBinding;
            if (to == null)
            {
                return;
            }

            if (to.isChannel)
            {
                graph.SetChannelInput(to.channel, to.index, null);
            }
            else
            {
                to.node?.SetInput(to.index, null);
            }
        }

        private void RemoveNodeView(Node view)
        {
            string guid = view.viewDataKey;
            if (string.IsNullOrEmpty(guid) || ModifierGraph.IsChannelGuid(guid))
            {
                return;
            }

            for (int i = 0; i < graph.nodes.Count; i++)
            {
                if (graph.nodes[i] is ModifierNodeBase node && node.Guid == guid)
                {
                    graph.RemoveNode(node);
                    break;
                }
            }

            nodeViews.Remove(guid);
        }

        private void UpdateNodePosition(Node view)
        {
            string guid = view.viewDataKey;
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            Rect rect = view.GetPosition();
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                if (graph.nodes[i] is ModifierNodeBase node && node.Guid == guid)
                {
                    node.GraphPosition = new Vector2(rect.x, rect.y);
                    return;
                }
            }
        }

        private void NotifySelection()
        {
            if (NodeSelected == null)
            {
                return;
            }

            for (int i = 0; i < selection.Count; i++)
            {
                if (selection[i] is Node node && !string.IsNullOrEmpty(node.viewDataKey) && !ModifierGraph.IsChannelGuid(node.viewDataKey))
                {
                    NodeSelected(FindNode(node.viewDataKey));
                    return;
                }
            }

            NodeSelected(null);
        }

        private ModifierNodeBase FindNode(string guid)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                if (graph.nodes[i] is ModifierNodeBase node && node.Guid == guid)
                {
                    return node;
                }
            }

            return null;
        }

        private static bool IsUpstream(ModifierNodeBase node, IModifierNode candidate, HashSet<IModifierNode> visited)
        {
            if (node == null)
            {
                return false;
            }

            for (int p = 0; p < node.InputPortCount; p++)
            {
                IModifierNode input = node.GetInput(p);
                if (input == null)
                {
                    continue;
                }

                if (ReferenceEquals(input, candidate))
                {
                    return true;
                }

                if (input is ModifierNodeBase upstream && visited.Add(upstream) && IsUpstream(upstream, candidate, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static string CategoryColor(ModifierNodeCategory category)
        {
            switch (category)
            {
                case ModifierNodeCategory.Noise: return "#1A6B3C";
                case ModifierNodeCategory.Math: return "#1A3B6B";
                case ModifierNodeCategory.Spatial: return "#6B3B1A";
                case ModifierNodeCategory.Mask: return "#6B1A1A";
                default: return "#2D2D30";
            }
        }

        private sealed class PortBinding
        {
            public ModifierNodeBase node;
            public bool isChannel;
            public ModifierChannel channel;
            public int index;
            public bool isOutput;
        }
    }
}
