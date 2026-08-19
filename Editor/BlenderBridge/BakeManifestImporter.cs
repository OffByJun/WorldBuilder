using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Editor.BlenderBridge
{
    public sealed class BakeManifestImporter : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom, bool didDomainReload)
        {
            BlenderBridgeSettings bridge = null;
            for (int i = 0; i < imported.Length; i++)
            {
                string path = imported[i];
                if (!path.EndsWith(".bake.json", StringComparison.OrdinalIgnoreCase)) continue;
                bridge ??= ChunkManifestImporter.FindSettings(false);
                if (bridge == null || bridge.WorldGrid == null) return;
                try
                {
                    BakeManifest value = BakeManifestCodec.Parse(File.ReadAllText(Path.GetFullPath(path)));
                    WorldBakeReport report = BakeManifestCodec.Validate(value, bridge.WorldGrid);
                    ChunkManifestImporter.LogReport(path, report);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"WorldBuilder: invalid bake manifest '{path}': {exception.Message}");
                }
            }
        }
    }
}
