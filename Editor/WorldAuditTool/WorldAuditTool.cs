using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.WorldAuditTool
{
    /// <summary>
    /// One-click integrity audit across WorldDataStore, DirectRegionCatalog,
    /// BlenderAssetRegistry, generated chunk prefabs and the voxel store.
    /// </summary>
    public sealed class WorldAuditTool : IWorldBuilderTool
    {
        private readonly List<string> issues = new List<string>();
        private Label summaryLabel;
        private ScrollView list;

        public string ToolName => WorldBuilderLocalization.Get("tool.worldAudit");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.worldAudit"));

            Button run = new Button(Run) { text = WorldBuilderLocalization.Get("btn.runAudit") };
            root.Add(run);

            summaryLabel = new Label();
            summaryLabel.style.marginTop = 6f;
            root.Add(summaryLabel);

            Button export = new Button(ExportCsv) { text = WorldBuilderLocalization.Get("btn.exportCsv") };
            export.style.marginTop = 4f;
            root.Add(export);

            list = new ScrollView();
            list.style.marginTop = 6f;
            list.style.flexGrow = 1;
            root.Add(list);

            return root;
        }

        public void OnSceneGUI()
        {
        }

        private void Run()
        {
            issues.Clear();
            AuditWorldDataStore();
            AuditRegionCatalogs();
            AuditAssetRegistries();
            AuditGeneratedChunks();
            AuditVoxelStore();

            int errors = CountIssues('E');
            int warnings = issues.Count - errors;
            summaryLabel.text = $"{issues.Count} finding(s): {errors} error(s), {warnings} warning(s).";
            RebuildList();

            if (issues.Count == 0) Debug.Log("[WorldBuilder] World audit passed with no findings.");
            else Debug.LogWarning($"[WorldBuilder] World audit found {errors} error(s), {warnings} warning(s).");
            UndoHistory.Push(WorldBuilderLocalization.Get("tool.worldAudit"));
        }

        private int CountIssues(char severity)
        {
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Length > 2 && issues[i][1] == severity) count++;
            }
            return count;
        }

        private void AddError(string message) => issues.Add("[E] " + message);
        private void AddWarning(string message) => issues.Add("[W] " + message);

        private void AuditWorldDataStore()
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store == null)
            {
                AddWarning("No active WorldDataStore asset; skipping world data checks.");
                return;
            }

            HashSet<string> seenIds = new HashSet<string>();
            foreach (KeyValuePair<System.Type, List<IWorldDataEntry>> category in store.GetAllCategories())
            {
                for (int i = 0; i < category.Value.Count; i++)
                {
                    IWorldDataEntry entry = category.Value[i];
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.DisplayName))
                        AddWarning($"World data entry {entry.Id} ({category.Key.Name}) has an empty display name.");
                    if (float.IsNaN(entry.Position.x) || float.IsNaN(entry.Position.y) || float.IsNaN(entry.Position.z))
                        AddError($"World data entry '{entry.DisplayName}' has a NaN position.");
                    if (!seenIds.Add(entry.Id))
                        AddError($"Duplicate world data id: {entry.Id} ('{entry.DisplayName}').");
                }
            }
        }

        private void AuditRegionCatalogs()
        {
            string[] guids = AssetDatabase.FindAssets("t:DirectRegionCatalog");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                DirectRegionCatalog catalog = AssetDatabase.LoadAssetAtPath<DirectRegionCatalog>(path);
                if (catalog == null) continue;

                HashSet<long> seenCoordinates = new HashSet<long>();
                IReadOnlyList<DirectRegionReference> regions = catalog.Regions;
                int nullCount = 0;
                for (int i = 0; i < regions.Count; i++)
                {
                    DirectRegionReference reference = regions[i];
                    if (reference == null) continue;
                    if (reference.prefab == null)
                    {
                        nullCount++;
                        continue;
                    }
                    long key = reference.Coordinate.X * 100000L + reference.Coordinate.Z;
                    if (!seenCoordinates.Add(key))
                        AddError($"Catalog '{catalog.name}' has a duplicate region {reference.Coordinate.X},{reference.Coordinate.Z}.");
                }
                if (nullCount > 0) AddError($"Catalog '{catalog.name}' has {nullCount} region reference(s) with no prefab.");
            }
        }

        private void AuditAssetRegistries()
        {
            string[] guids = AssetDatabase.FindAssets("t:BlenderAssetRegistry");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Authoring.Chunks.BlenderAssetRegistry registry =
                    AssetDatabase.LoadAssetAtPath<Authoring.Chunks.BlenderAssetRegistry>(path);
                if (registry == null) continue;

                HashSet<string> seenIds = new HashSet<string>();
                IReadOnlyList<Authoring.Chunks.BlenderAssetRegistryEntry> entries = registry.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    Authoring.Chunks.BlenderAssetRegistryEntry entry = entries[i];
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.AssetId))
                        AddWarning($"Registry '{registry.name}' has an entry with empty assetId.");
                    else if (!seenIds.Add(entry.AssetId))
                        AddError($"Registry '{registry.name}' has duplicate assetId '{entry.AssetId}'.");
                    if (entry.Prefab == null)
                        AddError($"Registry '{registry.name}' entry '{entry.AssetId}' has no prefab.");
                }
            }
        }

        private void AuditGeneratedChunks()
        {
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            if (bridge == null || bridge.WorldGrid == null)
            {
                AddWarning("No BlenderBridgeSettings found; skipping generated chunk checks.");
                return;
            }

            string chunksFolder = bridge.GeneratedRoot + "/" + bridge.WorldGrid.WorldId + "/Chunks";
            if (!AssetDatabase.IsValidFolder(chunksFolder)) return;

            Dictionary<(int x, int z), bool> catalogRegions = new Dictionary<(int x, int z), bool>();
            string[] catalogs = AssetDatabase.FindAssets("t:DirectRegionCatalog");
            foreach (string guid in catalogs)
            {
                DirectRegionCatalog catalog =
                    AssetDatabase.LoadAssetAtPath<DirectRegionCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog == null) continue;
                foreach (DirectRegionReference reference in catalog.Regions)
                {
                    if (reference?.prefab == null) continue;
                    RegionCoord coordinate = reference.Coordinate;
                    catalogRegions[(coordinate.X, coordinate.Z)] = true;
                }
            }

            WorldGrid grid = bridge.WorldGrid.CreateGrid();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { chunksFolder });
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject chunk = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ChunkRoot root = chunk != null ? chunk.GetComponent<ChunkRoot>() : null;
                if (root == null)
                {
                    AddWarning($"Chunk folder contains a non-chunk prefab: {path}");
                    continue;
                }

                RegionCoord region = grid.ChunkToRegion(root.Coordinate);
                if (!catalogRegions.ContainsKey((region.X, region.Z)))
                {
                    AddWarning($"Chunk {root.Coordinate.X},{root.Coordinate.Z} is not covered by any region catalog " +
                               $"(region {region.X},{region.Z}). Run 'Rebuild Region Catalog'.");
                }
            }
        }

        private void AuditVoxelStore()
        {
            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null) return;

            foreach (Vector3Int coord in store.Coords)
            {
                if (!store.TryGetVoxelData(coord, out VoxelData voxels) ||
                    voxels.density == null || voxels.density.Length == 0)
                    AddWarning($"Voxel store has no density data at {coord}.");
            }
        }

        private void RebuildList()
        {
            list.Clear();
            for (int i = 0; i < issues.Count; i++)
            {
                string text = issues[i];
                Label label = new Label(text);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = text.StartsWith("[E]") ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.85f, 0.45f);
                list.Add(label);
            }
        }

        private void ExportCsv()
        {
            if (issues.Count == 0)
            {
                Debug.Log("[WorldBuilder] Nothing to export; run the audit first.");
                return;
            }

            string path = EditorUtility.SaveFilePanel("Export Audit CSV", Application.dataPath, "world_audit", "csv");
            if (string.IsNullOrEmpty(path)) return;

            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("severity,message");
                for (int i = 0; i < issues.Count; i++)
                {
                    string line = issues[i].Replace("\"", "\"\"");
                    int split = line.IndexOf(']');
                    string severity = split > 0 ? line.Substring(1, split - 1) : "W";
                    writer.WriteLine($"{severity},\"{line}\"");
                }
            }
            Debug.Log($"[WorldBuilder] Audit exported to {path}");
        }
    }
}
