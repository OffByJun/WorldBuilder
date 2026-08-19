using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Editor.CreatureSpawnZoneTool
{
    public sealed class CreatureSpawnZoneTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private BiomeType biome = BiomeType.Ocean;
        [SerializeField] private int prefabId;
        [SerializeField] private float density = 1f;
        [SerializeField] private float radius = 5f;

        private readonly Dictionary<BiomeType, Color> colorLookup = new Dictionary<BiomeType, Color>();

        public string ToolName => WorldBuilderLocalization.Get("tool.creatureSpawn");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            RefreshColors();
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.creatureSpawn"));

            EnumField biomeField = new EnumField("Biome", biome);
            biomeField.RegisterValueChangedCallback(evt => biome = (BiomeType)evt.newValue);
            root.Add(biomeField);

            IntegerField prefabField = new IntegerField("Prefab Id") { value = prefabId };
            prefabField.RegisterValueChangedCallback(evt => prefabId = evt.newValue);
            root.Add(prefabField);

            Slider densityField = new Slider("Density", 0f, 10f) { value = density };
            densityField.RegisterValueChangedCallback(evt => density = evt.newValue);
            root.Add(densityField);

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
            CreatureSpawnZone[] all = Object.FindObjectsByType<CreatureSpawnZone>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Handles.color = GetColor(all[i].Biome);
                Handles.DrawWireDisc(all[i].transform.position, Vector3.up, all[i].Radius);
            }
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store != null) Undo.RecordObject(store, "Place Creature Spawn Zone");

            GameObject go = new GameObject("CreatureSpawnZone");
            go.transform.position = point;

            CreatureSpawnZone zone = go.AddComponent<CreatureSpawnZone>();
            zone.Radius = radius;
            zone.Biome = biome;
            zone.PrefabId = prefabId;
            zone.Density = density;

            Undo.RegisterCreatedObjectUndo(go, "Place Creature Spawn Zone");

            if (store != null)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                store.Add(new CreatureSpawnZoneEntry(point, biome, prefabId, density, radius, globalId));
                EditorUtility.SetDirty(store);
            }

            UndoHistory.Push("Place Creature Spawn Zone");
        }

        private Color GetColor(BiomeType value)
        {
            if (colorLookup.Count == 0)
            {
                RefreshColors();
            }

            return colorLookup.TryGetValue(value, out Color color) ? color : Color.gray;
        }

        private void RefreshColors()
        {
            colorLookup.Clear();

            string[] guids = AssetDatabase.FindAssets("t:BiomeData");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BiomeData data = AssetDatabase.LoadAssetAtPath<BiomeData>(path);
                if (data != null)
                {
                    colorLookup[data.BiomeType] = data.DebugColor;
                }
            }
        }
    }
}
