using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.PressureZoneTool
{
    public sealed class PressureZoneTool : IWorldBuilderTool, IRaycastConsumer, IEnvironmentZoneProvider
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float pressure = 50f;
        [SerializeField] private float damagePerSecond = 5f;

        private readonly List<PressureZone> zones = new List<PressureZone>();
        private readonly List<Vector3> positions = new List<Vector3>();

        public string ToolName => WorldBuilderLocalization.Get("tool.pressureZone");
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

            root.Add(InspectorHelp.Build(ToolName, "help.pressureZone"));

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            Slider pressureField = new Slider("Pressure", 0f, 100f) { value = pressure };
            pressureField.RegisterValueChangedCallback(evt => pressure = evt.newValue);
            root.Add(pressureField);

            FloatField damageField = new FloatField("Damage / Sec") { value = damagePerSecond };
            damageField.RegisterValueChangedCallback(evt => damagePerSecond = evt.newValue);
            root.Add(damageField);

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
            PressureZone[] all = Object.FindObjectsByType<PressureZone>(FindObjectsSortMode.None);
            Handles.color = Color.red;
            for (int i = 0; i < all.Length; i++)
            {
                Handles.DrawWireDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Pressure Zone");

            GameObject go = new GameObject("PressureZone");
            go.transform.position = point;

            PressureZone zone = go.AddComponent<PressureZone>();
            zone.Radius = radius;
            zone.Pressure = pressure;
            zone.DamagePerSecond = damagePerSecond;

            Undo.RegisterCreatedObjectUndo(go, "Place Pressure Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new PressureZoneEntry(point, radius, pressure, damagePerSecond, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Pressure Zone");
        }

        public IReadOnlyList<Vector3> GetZonePositions()
        {
            zones.Clear();
            positions.Clear();

            PressureZone[] all = Object.FindObjectsByType<PressureZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                zones.Add(all[i]);
                positions.Add(all[i].transform.position);
            }

            return positions;
        }

        public float GetIntensityAt(int index)
        {
            return Mathf.Clamp01(zones[index].Pressure / 100f);
        }

        public Color GetColorAt(int index)
        {
            return Color.red;
        }
    }
}
