using System;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Editor.BlenderBridge
{
    public static class ChunkImportMenus
    {
        [MenuItem("Tools/WorldBuilder/Chunks/Import All Blender Chunks")]
        private static void ImportAll()
        {
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(true);
            if (bridge == null) return;
            string[] paths = FindChunkManifests(bridge);
            int rebuilt = 0;
            bool cancelled = false;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    float progress = paths.Length == 0 ? 1f : i / (float)paths.Length;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "WorldBuilder",
                            $"Importing chunk {i + 1}/{paths.Length}: {System.IO.Path.GetFileName(paths[i])}",
                            progress))
                    {
                        cancelled = true;
                        break;
                    }

                    ChunkImportResult result = ChunkImportPipeline.Import(paths[i], bridge);
                    ChunkManifestImporter.LogReport(paths[i], result.Report);
                    if (result.WasRebuilt) rebuilt++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Debug.Log(cancelled
                ? $"WorldBuilder: import cancelled after processing {System.Math.Min(rebuilt, paths.Length)} chunk(s)."
                : $"WorldBuilder: processed {paths.Length} Blender chunks; rebuilt {rebuilt}.");
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Validate All Blender Chunks")]
        private static void ValidateAll()
        {
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(true);
            if (bridge == null) return;
            string[] paths = FindChunkManifests(bridge);
            int errors = 0;
            int warnings = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                WorldBakeReport report = ChunkImportPipeline.Validate(paths[i], bridge, out _);
                for (int issue = 0; issue < report.Issues.Count; issue++)
                {
                    BakeIssue item = report.Issues[issue];
                    switch (item.Severity)
                    {
                        case BakeIssueSeverity.Error:
                            errors++;
                            Debug.LogError($"[WB {item.Code}] {System.IO.Path.GetFileName(paths[i])}: {item.Message}");
                            break;
                        case BakeIssueSeverity.Warning:
                            warnings++;
                            Debug.LogWarning($"[WB {item.Code}] {System.IO.Path.GetFileName(paths[i])}: {item.Message}");
                            break;
                    }
                }
            }

            if (errors == 0 && warnings == 0)
                Debug.Log($"WorldBuilder: all {paths.Length} chunk manifests are valid.");
            else
                Debug.LogError($"WorldBuilder validation finished with {errors} error(s), {warnings} warning(s) across {paths.Length} chunks.");
        }

        private static string[] FindChunkManifests(BlenderBridgeSettings bridge)
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { bridge.SourceRoot });
            System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".chunk.json", StringComparison.OrdinalIgnoreCase)) found.Add(path);
            }
            string[] paths = found.ToArray();
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Rebuild Region Catalog")]
        private static void RebuildRegions()
        {
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(true);
            if (bridge == null) return;
            RegionCatalogBuilder.RebuildAll(bridge);
            Debug.Log("WorldBuilder: rebuilt all region prefabs and the direct-reference catalog.");
        }
    }
}
