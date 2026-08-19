using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.AirPocketTool
{
    public sealed class AirPocketTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private Vector3 size = Vector3.one * 4f;
        [SerializeField] private string label = string.Empty;

        public string ToolName => WorldBuilderLocalization.Get("tool.airPocket");
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

            root.Add(InspectorHelp.Build(ToolName, "help.airPocket"));

            Vector3Field sizeField = new Vector3Field("Size") { value = size };
            sizeField.RegisterValueChangedCallback(evt => size = evt.newValue);
            root.Add(sizeField);

            TextField labelField = new TextField("Label") { value = label };
            labelField.RegisterValueChangedCallback(evt => label = evt.newValue);
            root.Add(labelField);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawAndEdit();

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

        private void DrawAndEdit()
        {
            AirPocketVolume[] all = Object.FindObjectsByType<AirPocketVolume>(FindObjectsSortMode.None);
            Handles.color = Color.cyan;

            for (int i = 0; i < all.Length; i++)
            {
                AirPocketVolume volume = all[i];
                Vector3 center = volume.transform.position;

                Handles.DrawWireCube(center, volume.Size);
                if (!string.IsNullOrEmpty(volume.Label))
                {
                    Handles.Label(center + Vector3.up * (volume.Size.y * 0.5f), volume.Label);
                }

                float handleSize = HandleUtility.GetHandleSize(center) * 0.15f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(center, handleSize, Vector3.zero, Handles.CubeHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(volume.transform, "Move Air Pocket");
                    volume.transform.position = moved;
                    UndoHistory.Push("Move Air Pocket");
                    SceneView.RepaintAll();
                }
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Air Pocket");

            GameObject go = new GameObject("AirPocket");
            go.transform.position = point;

            AirPocketVolume volume = go.AddComponent<AirPocketVolume>();
            volume.Size = size;
            volume.Label = label;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;

            Undo.RegisterCreatedObjectUndo(go, "Place Air Pocket");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new AirPocketEntry(point, size, label, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Air Pocket");
        }
    }
}
