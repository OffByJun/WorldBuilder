using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.WaterCurrentTool
{
    public sealed class WaterCurrentTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private float strength = 5f;
        [SerializeField] private Vector3 direction = Vector3.forward;

        private bool dragging;
        private Vector3 dragStart;
        private Vector3 dragCurrent;

        public string ToolName => WorldBuilderLocalization.Get("tool.waterCurrent");
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

            root.Add(InspectorHelp.Build(ToolName, "help.waterCurrent"));

            Slider strengthField = new Slider("Strength", 0f, 10f) { value = strength };
            strengthField.RegisterValueChangedCallback(evt => strength = evt.newValue);
            root.Add(strengthField);

            Vector3Field directionField = new Vector3Field("Direction") { value = direction };
            directionField.RegisterValueChangedCallback(evt => direction = evt.newValue);
            root.Add(directionField);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawZones();

            if (dragging)
            {
                DrawPreview();
            }

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.button != 0 || e.alt)
            {
                return;
            }

            if (e.type == EventType.MouseDown && TryRaycast(out RaycastHit downHit))
            {
                dragging = true;
                dragStart = downHit.point;
                dragCurrent = downHit.point;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging && TryRaycast(out RaycastHit dragHit))
            {
                dragCurrent = dragHit.point;
                SceneView.RepaintAll();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && dragging)
            {
                Vector3 delta = dragCurrent - dragStart;
                Vector3 placed = delta.sqrMagnitude > 0.0001f ? delta.normalized : direction;
                Place(dragStart, placed);
                dragging = false;
                e.Use();
                SceneView.RepaintAll();
            }
        }

        private void DrawPreview()
        {
            Vector3 delta = dragCurrent - dragStart;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Handles.color = Color.yellow;
            Handles.ArrowHandleCap(0, dragStart, Quaternion.LookRotation(delta.normalized), delta.magnitude, EventType.Repaint);
        }

        private void DrawZones()
        {
            WaterCurrentZone[] all = Object.FindObjectsByType<WaterCurrentZone>(FindObjectsSortMode.None);
            Handles.color = Color.blue;
            for (int i = 0; i < all.Length; i++)
            {
                Vector3 dir = all[i].Direction;
                if (dir.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                Handles.ArrowHandleCap(0, all[i].transform.position, Quaternion.LookRotation(dir.normalized), all[i].Strength, EventType.Repaint);
            }
        }

        private void Place(Vector3 point, Vector3 dir)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Water Current");

            GameObject go = new GameObject("WaterCurrent");
            go.transform.position = point;

            WaterCurrentZone zone = go.AddComponent<WaterCurrentZone>();
            zone.Direction = dir;
            zone.Strength = strength;

            Undo.RegisterCreatedObjectUndo(go, "Place Water Current");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new WaterCurrentEntry(point, dir, strength, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Water Current");
        }
    }
}
