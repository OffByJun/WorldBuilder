using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.UnderwaterVisualizerTool
{
    /// <summary>
    /// Visualizes baked water data (WaterWorldRuntimeData) as a seafloor depth heat grid,
    /// plus scene WaterCurrentZone arrows scaled by strength. Works in edit mode.
    /// </summary>
    public sealed class UnderwaterVisualizerTool : IWorldBuilderTool
    {
        private static readonly Color ShallowColor = new Color(0.35f, 0.9f, 1f, 0.55f);
        private static readonly Color DeepColor = new Color(0f, 0.05f, 0.35f, 0.8f);

        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private float viewRadius = 128f;
        [SerializeField] private float cellSize = 4f;
        [SerializeField] private float maxDepthScale = 30f;
        [SerializeField] private bool showDepthGrid = true;
        [SerializeField] private bool showCurrentZones = true;
        [SerializeField] private bool showProbe = true;
        [SerializeField] private LayerMask terrainMask = ~0;

        private WaterQueryService service;

        public string ToolName => WorldBuilderLocalization.Get("tool.underwater");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            service = waterData != null ? new WaterQueryService(waterData) : null;
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.underwater"));

            ObjectField dataField = new ObjectField("Water Runtime Data")
            {
                objectType = typeof(WaterWorldRuntimeData),
                value = waterData
            };
            dataField.RegisterValueChangedCallback(evt =>
            {
                waterData = evt.newValue as WaterWorldRuntimeData;
                service = waterData != null ? new WaterQueryService(waterData) : null;
            });
            root.Add(dataField);

            FloatField radius = new FloatField("View Radius (m)") { value = viewRadius };
            radius.RegisterValueChangedCallback(evt => viewRadius = Mathf.Max(4f, evt.newValue));
            root.Add(radius);

            FloatField cell = new FloatField("Cell Size (m)") { value = cellSize };
            cell.RegisterValueChangedCallback(evt =>
            {
                cellSize = Mathf.Clamp(evt.newValue, 0.5f, 64f);
            });
            root.Add(cell);

            FloatField maxDepth = new FloatField("Max Depth Scale") { value = maxDepthScale };
            maxDepth.RegisterValueChangedCallback(evt => maxDepthScale = Mathf.Max(0.5f, evt.newValue));
            root.Add(maxDepth);

            Toggle depthGrid = new Toggle("Show Depth Grid") { value = showDepthGrid };
            depthGrid.RegisterValueChangedCallback(evt => showDepthGrid = evt.newValue);
            root.Add(depthGrid);

            Toggle currents = new Toggle("Show Current Zones") { value = showCurrentZones };
            currents.RegisterValueChangedCallback(evt => showCurrentZones = evt.newValue);
            root.Add(currents);

            Toggle probe = new Toggle("Show Cursor Probe") { value = showProbe };
            probe.RegisterValueChangedCallback(evt => showProbe = evt.newValue);
            root.Add(probe);

            LayerMaskField mask = new LayerMaskField("Terrain Mask", terrainMask);
            mask.RegisterValueChangedCallback(evt => terrainMask = evt.newValue);
            root.Add(mask);

            return root;
        }

        public void OnSceneGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            if (showCurrentZones) DrawCurrentZones();

            if (service != null && waterData != null)
            {
                if (showDepthGrid) DrawDepthGrid(view.pivot);
                if (showProbe) DrawProbe(view);
            }
            else if (showProbe)
            {
                Handles.Label(view.pivot + Vector3.up * 2f,
                    WorldBuilderLocalization.Get("hint.underwaterNoData"));
            }
        }

        private void DrawDepthGrid(Vector3 pivot)
        {
            Vector3 origin = waterData.WorldOrigin;
            float step = Mathf.Max(0.5f, cellSize);
            int cells = Mathf.CeilToInt(viewRadius / step);
            Vector3 start = new Vector3(
                Mathf.Floor((pivot.x - origin.x) / step) * step + origin.x - cells * step * 0.5f,
                0f,
                Mathf.Floor((pivot.z - origin.z) / step) * step + origin.z - cells * step * 0.5f);

            for (int x = 0; x < cells; x++)
            {
                for (int z = 0; z < cells; z++)
                {
                    Vector3 basePos = start + new Vector3(x * step, 0f, z * step);
                    if (!TrySampleSeafloor(basePos, out WaterSample sample, out Vector3 floor)) continue;

                    Color color = DepthColor(sample.Depth);
                    Vector3 a = floor + new Vector3(-step * 0.5f, 0.05f, -step * 0.5f);
                    Vector3 b = floor + new Vector3(step * 0.5f, 0.05f, -step * 0.5f);
                    Vector3 c = floor + new Vector3(step * 0.5f, 0.05f, step * 0.5f);
                    Vector3 d = floor + new Vector3(-step * 0.5f, 0.05f, step * 0.5f);
                    Handles.DrawSolidRectangleWithOutline(new[] { a, b, c, d }, color, color);
                }
            }
        }

        private bool TrySampleSeafloor(Vector3 xzPosition, out WaterSample sample, out Vector3 floor)
        {
            sample = WaterSample.Air;
            floor = default;
            Ray ray = new Ray(new Vector3(xzPosition.x, 1000f, xzPosition.z), Vector3.down);
            if (!Physics.Raycast(ray, out RaycastHit hit, 4000f, terrainMask)) return false;

            floor = hit.point;
            Vector3 submerged = floor + Vector3.down * 0.01f;
            sample = service.Sample(submerged);
            return sample.IsInWater;
        }

        private Color DepthColor(float depth)
        {
            float t = Mathf.Clamp01(depth / maxDepthScale);
            return Color.Lerp(ShallowColor, DeepColor, t);
        }

        private static void DrawCurrentZones()
        {
            Runtime.Zones.WaterCurrentZone[] zones =
                Object.FindObjectsByType<Runtime.Zones.WaterCurrentZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                Runtime.Zones.WaterCurrentZone zone = zones[i];
                float strength = zone.Strength;
                Handles.color = new Color(0.2f, 1f, 0.6f, Mathf.Clamp01(0.4f + strength * 0.15f));
                Vector3 position = zone.transform.position;
                float radius = 2f + strength;
                Handles.DrawWireDisc(position, Vector3.up, radius);
                Vector3 direction = zone.Direction.sqrMagnitude > 0.001f
                    ? zone.Direction.normalized
                    : zone.transform.forward;
                Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(direction),
                    2f + strength * 2f, EventType.Repaint);
            }
        }

        private void DrawProbe(SceneView view)
        {
            Event e = Event.current;
            Camera camera = Camera.current;
            if (e == null || camera == null) return;

            Ray ray = camera.ScreenPointToRay(EditorGUIUtility.GUIToScreenPoint(e.mousePosition));
            if (!Physics.Raycast(ray, out RaycastHit hit, 4000f, terrainMask)) return;

            WaterSample sample = service.Sample(hit.point + Vector3.down * 0.01f);
            if (!sample.IsInWater)
            {
                Handles.Label(hit.point + Vector3.up * 1f, "Air");
                return;
            }

            Handles.color = Color.cyan;
            Handles.DrawLine(hit.point, hit.point + Vector3.up * sample.Depth);
            string flowText = sample.FlowSpeed > 0.001f
                ? $", flow {sample.FlowSpeed:F2} m/s"
                : string.Empty;
            Handles.Label(hit.point + Vector3.up * 1f,
                $"depth {sample.Depth:F1}m{flowText}");
        }
    }
}
