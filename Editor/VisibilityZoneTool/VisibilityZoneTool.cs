using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.VisibilityZoneTool
{
    public sealed class VisibilityZoneTool : IWorldBuilderTool, IRaycastConsumer, IEnvironmentZoneProvider
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float visibility = 30f;
        [SerializeField] private Color fogColor = Color.gray;

        private readonly List<VisibilityZone> zones = new List<VisibilityZone>();
        private readonly List<Vector3> positions = new List<Vector3>();

        public string ToolName => WorldBuilderLocalization.Get("tool.visibilityZone");
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

            root.Add(InspectorHelp.Build(ToolName, "help.visibilityZone"));

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            Slider visibilityField = new Slider("Visibility", 0f, 100f) { value = visibility };
            visibilityField.RegisterValueChangedCallback(evt => visibility = evt.newValue);
            root.Add(visibilityField);

            ColorField fogField = new ColorField("Fog Color") { value = fogColor };
            fogField.RegisterValueChangedCallback(evt => fogColor = evt.newValue);
            root.Add(fogField);

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
            VisibilityZone[] all = Object.FindObjectsByType<VisibilityZone>(FindObjectsSortMode.None);
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            for (int i = 0; i < all.Length; i++)
            {
                Handles.DrawSolidDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Visibility Zone");

            GameObject go = new GameObject("VisibilityZone");
            go.transform.position = point;

            VisibilityZone zone = go.AddComponent<VisibilityZone>();
            zone.Radius = radius;
            zone.Visibility = visibility;
            zone.FogColor = fogColor;

            Undo.RegisterCreatedObjectUndo(go, "Place Visibility Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new VisibilityZoneEntry(point, radius, visibility, fogColor, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Visibility Zone");
        }

        public IReadOnlyList<Vector3> GetZonePositions()
        {
            zones.Clear();
            positions.Clear();

            VisibilityZone[] all = Object.FindObjectsByType<VisibilityZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                zones.Add(all[i]);
                positions.Add(all[i].transform.position);
            }

            return positions;
        }

        public float GetIntensityAt(int index)
        {
            return Mathf.Clamp01(zones[index].Visibility / 100f);
        }

        public Color GetColorAt(int index)
        {
            return zones[index].FogColor;
        }
    }
}
