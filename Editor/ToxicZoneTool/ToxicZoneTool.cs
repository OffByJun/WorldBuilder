using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.ToxicZoneTool
{
    public sealed class ToxicZoneTool : IWorldBuilderTool, IRaycastConsumer, IEnvironmentZoneProvider
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float intensity = 0.5f;
        [SerializeField] private float damagePerSecond = 5f;

        private readonly List<ToxicZone> zones = new List<ToxicZone>();
        private readonly List<Vector3> positions = new List<Vector3>();

        public string ToolName => WorldBuilderLocalization.Get("tool.toxicZone");
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

            root.Add(InspectorHelp.Build(ToolName, "help.toxicZone"));

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            Slider intensityField = new Slider("Intensity", 0f, 1f) { value = intensity };
            intensityField.RegisterValueChangedCallback(evt => intensity = evt.newValue);
            root.Add(intensityField);

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
            ToxicZone[] all = Object.FindObjectsByType<ToxicZone>(FindObjectsSortMode.None);
            Handles.color = Color.green;
            for (int i = 0; i < all.Length; i++)
            {
                Handles.DrawWireDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Toxic Zone");

            GameObject go = new GameObject("ToxicZone");
            go.transform.position = point;

            ToxicZone zone = go.AddComponent<ToxicZone>();
            zone.Radius = radius;
            zone.Intensity = intensity;
            zone.DamagePerSecond = damagePerSecond;

            Undo.RegisterCreatedObjectUndo(go, "Place Toxic Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new ToxicZoneEntry(point, radius, intensity, damagePerSecond, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Toxic Zone");
        }

        public IReadOnlyList<Vector3> GetZonePositions()
        {
            zones.Clear();
            positions.Clear();

            ToxicZone[] all = Object.FindObjectsByType<ToxicZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                zones.Add(all[i]);
                positions.Add(all[i].transform.position);
            }

            return positions;
        }

        public float GetIntensityAt(int index)
        {
            return Mathf.Clamp01(zones[index].Intensity);
        }

        public Color GetColorAt(int index)
        {
            return Color.green;
        }
    }
}
