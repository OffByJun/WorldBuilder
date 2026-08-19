using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.SpawnEditing
{
    public sealed class SpawnEditTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private GameObject spawnerPrefab;
        [SerializeField] private bool removeMode;
        [SerializeField] private float removeThreshold = 1f;

        private readonly ISpawnerSceneQuery query;

        public SpawnEditTool(ISpawnerSceneQuery query)
        {
            this.query = query;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.spawnEdit");
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

            root.Add(InspectorHelp.Build(ToolName, "help.spawnEdit"));

            ObjectField prefab = new ObjectField("Spawner Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = spawnerPrefab
            };
            prefab.RegisterValueChangedCallback(evt => spawnerPrefab = evt.newValue as GameObject);
            root.Add(prefab);

            Toggle remove = new Toggle("Remove Mode") { value = removeMode };
            remove.RegisterValueChangedCallback(evt => removeMode = evt.newValue);
            root.Add(remove);

            Slider threshold = new Slider("Remove Threshold", 0.1f, 10f) { value = removeThreshold };
            threshold.RegisterValueChangedCallback(evt => removeThreshold = evt.newValue);
            root.Add(threshold);

            HelpBox help = new HelpBox("Prefab must contain a component implementing ISpawner.", HelpBoxMessageType.Warning);
            root.Add(help);

            root.schedule.Execute(() =>
            {
                bool warn = spawnerPrefab != null && spawnerPrefab.GetComponent<ISpawner>() == null;
                help.style.display = warn ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(200);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawLabels();

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (!TryRaycast(out RaycastHit hit))
                {
                    return;
                }

                if (removeMode)
                {
                    RemoveNear(hit.point);
                }
                else
                {
                    Place(hit.point);
                }
                e.Use();
                SceneView.RepaintAll();
            }
        }

        private void DrawLabels()
        {
            IReadOnlyList<ISpawner> spawners = query.GetAll();
            for (int i = 0; i < spawners.Count; i++)
            {
                ISpawner spawner = spawners[i];
                Handles.Label(spawner.SpawnPosition + Vector3.up * 0.5f, "id:" + spawner.PrefabId);
            }
        }

        private void Place(Vector3 point)
        {
            if (spawnerPrefab == null || spawnerPrefab.GetComponent<ISpawner>() == null)
            {
                return;
            }

            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Spawner");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(spawnerPrefab);
            instance.transform.position = point;
            Undo.RegisterCreatedObjectUndo(instance, "Place Spawner");

            if (store != null)
            {
                ISpawner spawner = instance.GetComponent<ISpawner>();
                int prefabId = spawner != null ? spawner.PrefabId : 0;
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(instance).ToString();
                store.Add(new SpawnPointEntry(point, prefabId, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Spawner");
        }

        private void RemoveNear(Vector3 point)
        {
            IReadOnlyList<ISpawner> spawners = query.GetAll();
            float best = float.MaxValue;
            ISpawner nearest = null;

            for (int i = 0; i < spawners.Count; i++)
            {
                float distance = Vector3.Distance(spawners[i].SpawnPosition, point);
                if (distance < best)
                {
                    best = distance;
                    nearest = spawners[i];
                }
            }

            if (nearest == null || best > removeThreshold)
            {
                return;
            }

            if (nearest is MonoBehaviour behaviour && behaviour != null)
            {
                Undo.DestroyObjectImmediate(behaviour.gameObject);
                UndoHistory.Push("Remove Spawner");
            }
        }
    }
}
