using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.EventTriggerZoneTool
{
    public sealed class EventTriggerZoneTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private string eventId = string.Empty;
        [SerializeField] private float radius = 5f;
        [SerializeField] private bool oneShot = true;

        public string ToolName => WorldBuilderLocalization.Get("tool.eventTrigger");
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

            root.Add(InspectorHelp.Build(ToolName, "help.eventTrigger"));

            TextField idField = new TextField("Event Id") { value = eventId };
            idField.RegisterValueChangedCallback(evt => eventId = evt.newValue);
            root.Add(idField);

            Slider radiusField = new Slider("Radius", 0.1f, 50f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt => radius = evt.newValue);
            root.Add(radiusField);

            Toggle oneShotField = new Toggle("One Shot") { value = oneShot };
            oneShotField.RegisterValueChangedCallback(evt => oneShot = evt.newValue);
            root.Add(oneShotField);

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
            EventTriggerZone[] all = Object.FindObjectsByType<EventTriggerZone>(FindObjectsSortMode.None);
            Handles.color = Color.yellow;
            for (int i = 0; i < all.Length; i++)
            {
                Vector3 center = all[i].transform.position;
                Handles.DrawWireDisc(center, Vector3.up, all[i].Radius);
                if (!string.IsNullOrEmpty(all[i].EventId))
                {
                    Handles.Label(center, all[i].EventId);
                }
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Event Trigger Zone");

            GameObject go = new GameObject("EventTriggerZone");
            go.transform.position = point;

            EventTriggerZone zone = go.AddComponent<EventTriggerZone>();
            zone.Radius = radius;
            zone.EventId = eventId;
            zone.OneShot = oneShot;

            Undo.RegisterCreatedObjectUndo(go, "Place Event Trigger Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new EventTriggerZoneEntry(point, eventId, radius, oneShot, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Event Trigger Zone");
        }
    }
}
