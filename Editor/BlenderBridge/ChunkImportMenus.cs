using System;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.BlenderBridge
{
    public static class ChunkImportMenus
    {
        [MenuItem("Tools/WorldBuilder/Chunks/Import All Blender Chunks")]
        private static void ImportAll()
        {
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(true);
            if (bridge == null) return;
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { bridge.SourceRoot });
            System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".chunk.json", StringComparison.OrdinalIgnoreCase)) found.Add(path);
            }
            string[] paths = found.ToArray();
            Array.Sort(paths, StringComparer.Ordinal);
            int rebuilt = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                ChunkImportResult result = ChunkImportPipeline.Import(paths[i], bridge);
                ChunkManifestImporter.LogReport(paths[i], result.Report);
                if (result.WasRebuilt) rebuilt++;
            }
            Debug.Log($"WorldBuilder: processed {paths.Length} Blender chunks; rebuilt {rebuilt}.");
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
