using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.TemperatureZoneTool
{
    public sealed class TemperatureZoneTool : IWorldBuilderTool, IRaycastConsumer, IEnvironmentZoneProvider
    {
        [SerializeField] private float temperature = 20f;
        [SerializeField] private float radius = 5f;

        private readonly List<TemperatureZone> zones = new List<TemperatureZone>();
        private readonly List<Vector3> positions = new List<Vector3>();

        public string ToolName => WorldBuilderLocalization.Get("tool.temperatureZone");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.temperatureZone"));

            Slider temperatureField = new Slider("Temperature", -20f, 100f) { value = temperature };
            temperatureField.RegisterValueChangedCallback(evt => temperature = evt.newValue);
            root.Add(temperatureField);

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawZones();

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && TryRaycast(out RaycastHit hit))
            {
                Place(hit.point);
                e.Use();
                SceneView.RepaintAll();
            }
        }

        private void DrawZones()
        {
            TemperatureZone[] all = Object.FindObjectsByType<TemperatureZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Handles.color = ColorFor(all[i].Temperature);
                Handles.DrawWireDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private static Color ColorFor(float value)
        {
            return value > 0f ? new Color(1f, 0.5f, 0f) : new Color(0.2f, 0.4f, 1f);
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Temperature Zone");

            GameObject go = new GameObject("TemperatureZone");
            go.transform.position = point;

            TemperatureZone zone = go.AddComponent<TemperatureZone>();
            zone.Radius = radius;
            zone.Temperature = temperature;

            Undo.RegisterCreatedObjectUndo(go, "Place Temperature Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new TemperatureZoneEntry(point, radius, temperature, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Temperature Zone");
        }

        public IReadOnlyList<Vector3> GetZonePositions()
        {
            zones.Clear();
            positions.Clear();

            TemperatureZone[] all = Object.FindObjectsByType<TemperatureZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                zones.Add(all[i]);
                positions.Add(all[i].transform.position);
            }

            return positions;
        }

        public float GetIntensityAt(int index)
        {
            return Mathf.Clamp01((zones[index].Temperature + 20f) / 120f);
        }

        public Color GetColorAt(int index)
        {
            return ColorFor(zones[index].Temperature);
        }
    }
}
