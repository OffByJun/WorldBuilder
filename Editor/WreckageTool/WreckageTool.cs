using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.WreckageTool
{
    public sealed class WreckageTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private int logNumber;

        public string ToolName => WorldBuilderLocalization.Get("tool.wreckage");
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

            root.Add(InspectorHelp.Build(ToolName, "help.wreckage"));

            ObjectField prefabField = new ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = prefab
            };
            prefabField.RegisterValueChangedCallback(evt => prefab = evt.newValue as GameObject);
            root.Add(prefabField);

            IntegerField logField = new IntegerField("Log Number") { value = logNumber };
            logField.RegisterValueChangedCallback(evt => logNumber = evt.newValue);
            root.Add(logField);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawTags();

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

        private void DrawTags()
        {
            WreckageTag[] all = Object.FindObjectsByType<WreckageTag>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].LogNumber > 0)
                {
                    Handles.Label(all[i].transform.position, "LOG_" + all[i].LogNumber.ToString("D3"));
                }
            }
        }

        private void Place(Vector3 point)
        {
            if (prefab == null)
            {
                return;
            }

            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Wreckage");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = point;

            WreckageTag tag = instance.GetComponent<WreckageTag>();
            if (tag == null)
            {
                tag = instance.AddComponent<WreckageTag>();
            }
            tag.LogNumber = logNumber;

            Undo.RegisterCreatedObjectUndo(instance, "Place Wreckage");

            if (store != null)
            {
                string prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(instance).ToString();
                store.Add(new WreckageEntry(point, prefabGuid, logNumber, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Wreckage");
        }
    }
}
