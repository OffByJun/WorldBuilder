using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Authoring.Chunks;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Editor.PrefabBrush;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Editor.ScatterBakeTool
{
    /// <summary>
    /// Unity-only scatter pipeline: replays recorded Prefab Brush strokes and bakes them
    /// into existing Blender chunk manifests as placement documents, then re-imports the
    /// affected chunks through the standard ChunkImportPipeline. No Blender round-trip.
    /// </summary>
    public static class ScatterChunkBaker
    {
        public sealed class BakeSummary
        {
            public int ChunksUpdated;
            public int PlacementsAdded;
            public readonly List<string> Skipped = new List<string>();
        }

        public static BakeSummary Bake(PrefabBrushSettings brush, BlenderBridgeSettings bridge,
            IBiomeMap biomeMap)
        {
            if (brush == null) throw new ArgumentNullException(nameof(brush));
            if (bridge == null || bridge.WorldGrid == null)
                throw new ArgumentException("BlenderBridgeSettings with WorldGridSettings is required.", nameof(bridge));

            WorldGrid grid = bridge.WorldGrid.CreateGrid();
            var calculator = new ChunkCoordCalculator();

            var grouped = new Dictionary<ChunkCoord, List<BrushPlacement>>();
            foreach (BrushStroke stroke in brush.strokes)
            {
                List<BrushPlacement> placements = StrokePlacementBuilder.Build(brush, stroke, biomeMap, calculator);
                GroupIntoChunks(placements, grid, grouped);
            }
            return BakeGrouped(grouped, bridge);
        }

        /// <summary>
        /// Shared entry point for programmatic placement sources (PCG ecology, runtime exports…).
        /// </summary>
        public static BakeSummary BakePlacements(IEnumerable<BrushPlacement> placements,
            BlenderBridgeSettings bridge)
        {
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            if (bridge == null || bridge.WorldGrid == null)
                throw new ArgumentException("BlenderBridgeSettings with WorldGridSettings is required.", nameof(bridge));

            var grouped = new Dictionary<ChunkCoord, List<BrushPlacement>>();
            GroupIntoChunks(new List<BrushPlacement>(placements), bridge.WorldGrid.CreateGrid(), grouped);
            return BakeGrouped(grouped, bridge);
        }

        private static void GroupIntoChunks(List<BrushPlacement> placements, WorldGrid grid,
            Dictionary<ChunkCoord, List<BrushPlacement>> grouped)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                BrushPlacement placement = placements[i];
                ChunkCoord chunk = grid.WorldToChunk(placement.position);
                if (!grouped.TryGetValue(chunk, out List<BrushPlacement> bucket))
                {
                    bucket = new List<BrushPlacement>();
                    grouped[chunk] = bucket;
                }
                bucket.Add(placement);
            }
        }

        private static BakeSummary BakeGrouped(Dictionary<ChunkCoord, List<BrushPlacement>> grouped,
            BlenderBridgeSettings bridge)
        {
            var summary = new BakeSummary();

            Dictionary<GameObject, string> assetIds = BuildReverseRegistry(bridge.AssetRegistry);

            List<string> manifestPaths = FindManifests(bridge.SourceRoot);
            Dictionary<ChunkCoord, string> manifestsByChunk = new Dictionary<ChunkCoord, string>();
            for (int i = 0; i < manifestPaths.Count; i++)
            {
                try
                {
                    ChunkManifest manifest = ChunkManifestCodec.Parse(File.ReadAllText(manifestPaths[i]));
                    manifestsByChunk[new ChunkCoord(manifest.chunk.x, manifest.chunk.z)] =
                        ToAssetPath(manifestPaths[i]);
                }
                catch (Exception)
                {
                    // Unreadable manifests are reported by the validation pipeline; skip here.
                }
            }

            List<KeyValuePair<ChunkCoord, List<BrushPlacement>>> chunks = new List<KeyValuePair<ChunkCoord, List<BrushPlacement>>>(grouped);
            chunks.Sort((left, right) => left.Key.CompareTo(right.Key));

            AssetDatabase.Refresh();
            foreach (KeyValuePair<ChunkCoord, List<BrushPlacement>> pair in chunks)
            {
                ChunkCoord chunk = pair.Key;
                if (!manifestsByChunk.TryGetValue(chunk, out string manifestPath))
                {
                    summary.Skipped.Add($"{WorldCoordNaming.ChunkName(chunk)}: no .chunk.json under source root.");
                    continue;
                }

                int added = WritePlacements(manifestPath, chunk, pair.Value, assetIds, bridge);
                if (added < 0)
                {
                    summary.Skipped.Add($"{WorldCoordNaming.ChunkName(chunk)}: registry is missing prefabs.");
                    continue;
                }

                ChunkImportResult result = ChunkImportPipeline.Import(manifestPath, bridge);
                ChunkManifestImporter.LogReport(manifestPath, result.Report);
                if (result.Report.HasErrors)
                {
                    summary.Skipped.Add($"{WorldCoordNaming.ChunkName(chunk)}: import failed, see report.");
                    continue;
                }

                summary.ChunksUpdated++;
                summary.PlacementsAdded += added;
            }

            return summary;
        }

        private static int WritePlacements(string manifestAssetPath, ChunkCoord chunk,
            List<BrushPlacement> placements, Dictionary<GameObject, string> assetIds, BlenderBridgeSettings bridge)
        {
            string manifestFullPath = ToFullPath(manifestAssetPath);
            ChunkManifest manifest = ChunkManifestCodec.Parse(File.ReadAllText(manifestFullPath));

            ChunkPlacementDocument document = new ChunkPlacementDocument
            {
                version = ChunkPlacementDocument.CurrentVersion,
                worldId = manifest.worldId,
                chunk = new ChunkManifestCoord { x = chunk.X, z = chunk.Z }
            };

            List<ChunkPlacementRecord> records = new List<ChunkPlacementRecord>();

            // Keep any pre-existing placements first so they survive the merge.
            if (manifest.content?.placements?.IsPresent ?? false)
            {
                string existingPath = ChunkManifestCodec.ResolveContentPath(manifestFullPath, manifest.content.placements);
                if (File.Exists(existingPath))
                {
                    try
                    {
                        ChunkPlacementDocument existing = ChunkManifestCodec.ParsePlacements(File.ReadAllText(existingPath));
                        if (existing?.objects != null) records.AddRange(existing.objects);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[WorldBuilder] Could not merge existing placements: {exception.Message}");
                    }
                }
            }

            Vector3 chunkOrigin = bridge.WorldGrid.CreateGrid().ChunkToWorldOrigin(chunk);
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++) usedIds.Add(records[i].stableId);

            int addedCount = 0;
            foreach (BrushPlacement placement in placements)
            {
                if (!assetIds.TryGetValue(placement.prefab, out string assetId))
                {
                    Debug.LogWarning($"[WorldBuilder] Prefab '{placement.prefab.name}' is not registered in the BlenderAssetRegistry; skipping.");
                    continue;
                }

                Matrix4x4 matrix = Matrix4x4.TRS(
                    placement.position - new Vector3(chunkOrigin.x, 0f, chunkOrigin.z),
                    placement.rotation,
                    placement.scale);

                float[] values = new float[16];
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    values[row * 4 + column] = matrix[row, column];

                string stableId = "wb_" + DeterministicHash.Sha256(assetId + "|" + FormatMatrix(values)).Substring(0, 24);
                if (!usedIds.Add(stableId)) continue;

                records.Add(new ChunkPlacementRecord
                {
                    stableId = stableId,
                    name = placement.prefab.name,
                    role = ChunkPlacementRecord.InstanceRole,
                    assetId = assetId,
                    layer = 0,
                    matrix = values
                });
                addedCount++;
            }

            document.objects = records.ToArray();
            string json = JsonUtility.ToJson(document);

            string placementsFileName = Path.GetFileNameWithoutExtension(manifestFullPath) + ".placements.json";
            string placementsFullPath = Path.Combine(Path.GetDirectoryName(manifestFullPath) ?? ".", placementsFileName);
            File.WriteAllText(placementsFullPath, json);

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            manifest.content.placements.path = placementsFileName;
            manifest.content.placements.bytes = bytes.Length;
            manifest.content.placements.sha256 = Sha256Hex(bytes);
            manifest.content.placementCount = records.Count;
            manifest.contentHash = ChunkManifestCodec.ComputeContentHash(manifest);

            File.WriteAllText(manifestFullPath, ChunkManifestCodec.Serialize(manifest));
            AssetDatabase.ImportAsset(ToAssetPath(placementsFullPath));
            return addedCount;
        }

        private static Dictionary<GameObject, string> BuildReverseRegistry(BlenderAssetRegistry registry)
        {
            Dictionary<GameObject, string> reverse = new Dictionary<GameObject, string>();
            if (registry == null) return reverse;
            IReadOnlyList<BlenderAssetRegistryEntry> entries = registry.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                BlenderAssetRegistryEntry entry = entries[i];
                if (entry == null || entry.Prefab == null || string.IsNullOrEmpty(entry.AssetId)) continue;
                if (!reverse.ContainsKey(entry.Prefab)) reverse[entry.Prefab] = entry.AssetId;
            }
            return reverse;
        }

        private static List<string> FindManifests(string sourceRoot)
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { sourceRoot });
            List<string> found = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".chunk.json", StringComparison.OrdinalIgnoreCase)) found.Add(path);
            }
            return found;
        }

        internal static string FormatMatrix(float[] values)
        {
            StringBuilder builder = new StringBuilder(values.Length * 10);
            for (int i = 0; i < values.Length; i++) builder.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            return builder.ToString();
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                foreach (byte hash in sha.ComputeHash(bytes)) builder.Append(hash.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            return FileUtil.GetProjectRelativePath(fullPath).Replace('\\', '/');
        }

        private static string ToFullPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath)) return Path.GetFullPath(assetPath);
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }
    }
}
