using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.BinImporter
{
    public sealed class BinImporterTool : IWorldBuilderTool
    {
        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private PrefabRegistry prefabRegistry;

        private readonly IChunkBiomeMap biomeMap;
        private readonly IVoxelStore voxelStore;
        private readonly BinImporter importer = new BinImporter();
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();

        private string filePath;
        private int lastSpawnCount;

        public BinImporterTool(IChunkBiomeMap biomeMap, IVoxelStore voxelStore)
        {
            this.biomeMap = biomeMap;
            this.voxelStore = voxelStore;
            filePath = WorldBinPath.Full;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.binImport");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.binImport"));

            FloatField size = new FloatField("Chunk Size") { value = chunkSize };
            size.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            root.Add(size);

            ObjectField registry = new ObjectField("Prefab Registry")
            {
                objectType = typeof(PrefabRegistry),
                allowSceneObjects = false,
                value = prefabRegistry
            };
            registry.RegisterValueChangedCallback(evt => prefabRegistry = evt.newValue as PrefabRegistry);
            root.Add(registry);

            TextField path = new TextField("File Path") { value = filePath };
            path.RegisterValueChangedCallback(evt => filePath = evt.newValue);
            root.Add(path);

            Button load = new Button(Load) { text = WorldBuilderLocalization.Get("btn.load") };
            root.Add(load);

            HelpBox help = new HelpBox("Assign a PrefabRegistry to restore spawned objects. Without it, only biome and voxel data are restored.", HelpBoxMessageType.Info);
            root.Add(help);

            Label spawnCount = new Label();
            root.Add(spawnCount);

            root.schedule.Execute(() =>
            {
                help.style.display = prefabRegistry == null ? DisplayStyle.Flex : DisplayStyle.None;
                spawnCount.text = "Last Imported Spawns: " + lastSpawnCount;
            }).Every(200);

            return root;
        }

        public void OnSceneGUI()
        {
        }

        private void Load()
        {
            if (!File.Exists(filePath))
            {
                EditorUtility.DisplayDialog("Bin Importer", "File not found:\n" + filePath, "OK");
                return;
            }

            ChunkData[] chunks;
            try
            {
                chunks = importer.Parse(filePath);
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Bin Importer", "Failed to parse:\n" + exception.Message, "OK");
                return;
            }

            lastSpawnCount = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                ChunkData chunk = chunks[i];
                Vector3Int coord = calculator.ToChunkCoord(chunk.position, chunkSize);

                biomeMap.Set(coord, chunk.biome);

                if (chunk.voxels.density != null)
                {
                    voxelStore.SetVoxelData(coord, chunk.voxels);
                }

                RestoreSpawns(chunk.spawns);
            }

            SceneView.RepaintAll();
        }

        private void RestoreSpawns(SpawnData[] spawns)
        {
            if (spawns == null)
            {
                return;
            }

            for (int i = 0; i < spawns.Length; i++)
            {
                SpawnData spawn = spawns[i];
                lastSpawnCount++;

                if (prefabRegistry == null || !prefabRegistry.TryGet(spawn.prefabId, out GameObject prefab) || prefab == null)
                {
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
                instance.transform.localScale = spawn.scale;
                Undo.RegisterCreatedObjectUndo(instance, "Import Spawn");
                UndoHistory.Push("Import Spawn");
            }
        }
    }
}
