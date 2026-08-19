using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.BiomeSetter
{
    public sealed class BiomeSetterTool : IWorldBuilderTool, IRaycastConsumer
    {
        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private BiomeType selectedBiome = BiomeType.Forest;
        [SerializeField] private bool eraseMode;

        private readonly IChunkBiomeMap biomeMap;
        private readonly IBiomeDataProvider biomeProvider;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();
        private readonly Dictionary<BiomeType, Color> colorLookup = new Dictionary<BiomeType, Color>();

        public BiomeSetterTool(IChunkBiomeMap biomeMap, IBiomeDataProvider biomeProvider)
        {
            this.biomeMap = biomeMap;
            this.biomeProvider = biomeProvider;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.biomeSetter");
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

            root.Add(InspectorHelp.Build(ToolName, "help.biomeSetter"));

            EnumField biome = new EnumField("Biome", selectedBiome);
            biome.RegisterValueChangedCallback(evt => selectedBiome = (BiomeType)evt.newValue);
            root.Add(biome);

            FloatField size = new FloatField("Chunk Size") { value = chunkSize };
            size.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            root.Add(size);

            Toggle erase = new Toggle("Erase Mode") { value = eraseMode };
            erase.RegisterValueChangedCallback(evt => eraseMode = evt.newValue);
            root.Add(erase);

            Label count = new Label();
            root.Add(count);

            Button refresh = new Button(RefreshColors) { text = "Refresh Biome Colors" };
            root.Add(refresh);

            root.schedule.Execute(() => count.text = "Assigned Chunks: " + biomeMap.Entries.Count).Every(200);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawChunks();

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (TryRaycast(out RaycastHit hit))
                {
                    Vector3Int coord = calculator.ToChunkCoord(hit.point, chunkSize);
                    if (eraseMode)
                    {
                        biomeMap.Remove(coord);
                        WorldDataStore store = WorldDataStoreLocator.Active;
                        if (store != null)
                        {
                            Undo.RecordObject(store, "Erase Biome");
                            store.RemoveWhere<BiomeEntry>(entry => entry.ChunkCoord == coord);
                            EditorUtility.SetDirty(store);
                        }
                    }
                    else
                    {
                        biomeMap.Set(coord, selectedBiome);
                        WorldDataStore store = WorldDataStoreLocator.Active;
                        if (store != null)
                        {
                            Undo.RecordObject(store, "Set Biome");
                            store.RemoveWhere<BiomeEntry>(entry => entry.ChunkCoord == coord);
                            Vector3 worldCenter = calculator.ToWorldCenter(coord, chunkSize);
                            store.Add(new BiomeEntry(worldCenter, selectedBiome, coord, chunkSize));
                            EditorUtility.SetDirty(store);
                        }
                    }
                    e.Use();
                    SceneView.RepaintAll();
                }
            }
        }

        private void DrawChunks()
        {
            float half = chunkSize * 0.5f;

            foreach (KeyValuePair<Vector3Int, BiomeType> entry in biomeMap.Entries)
            {
                Vector3 center = calculator.ToWorldCenter(entry.Key, chunkSize);
                Vector3[] corners =
                {
                    center + new Vector3(-half, 0f, -half),
                    center + new Vector3(-half, 0f, half),
                    center + new Vector3(half, 0f, half),
                    center + new Vector3(half, 0f, -half)
                };

                Color color = GetColor(entry.Value);
                Color face = new Color(color.r, color.g, color.b, 0.25f);
                Handles.DrawSolidRectangleWithOutline(corners, face, color);
            }
        }

        private Color GetColor(BiomeType biome)
        {
            if (colorLookup.TryGetValue(biome, out Color color))
            {
                return color;
            }

            return Color.gray;
        }

        private void RefreshColors()
        {
            colorLookup.Clear();
            BiomeData[] all = biomeProvider.GetAll();
            for (int i = 0; i < all.Length; i++)
            {
                colorLookup[all[i].BiomeType] = all[i].DebugColor;
            }
        }
    }
}
