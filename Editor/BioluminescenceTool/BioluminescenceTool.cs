using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.BioluminescenceTool
{
    public sealed class BioluminescenceTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float intensity = 0.5f;
        [SerializeField] private Color color = Color.cyan;

        public string ToolName => WorldBuilderLocalization.Get("tool.bioluminescence");
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

            root.Add(InspectorHelp.Build(ToolName, "help.bioluminescence"));

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            Slider intensityField = new Slider("Intensity", 0f, 1f) { value = intensity };
            intensityField.RegisterValueChangedCallback(evt => intensity = evt.newValue);
            root.Add(intensityField);

            ColorField colorField = new ColorField("Color") { value = color };
            colorField.RegisterValueChangedCallback(evt => color = evt.newValue);
            root.Add(colorField);

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
            BioluminescenceZone[] all = Object.FindObjectsByType<BioluminescenceZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Handles.color = all[i].Color;
                Handles.DrawWireDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Bioluminescence Zone");

            GameObject go = new GameObject("BioluminescenceZone");
            go.transform.position = point;

            BioluminescenceZone zone = go.AddComponent<BioluminescenceZone>();
            zone.Radius = radius;
            zone.Intensity = intensity;
            zone.Color = color;

            Undo.RegisterCreatedObjectUndo(go, "Place Bioluminescence Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new BioluminescenceEntry(point, radius, intensity, color, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Bioluminescence Zone");
        }
    }
}
