using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Editor.BlenderBridge
{
    public sealed class ChunkManifestImporter : AssetPostprocessor
    {
        private static readonly SortedSet<string> Queue = new SortedSet<string>(StringComparer.Ordinal);
        private static bool scheduled;

        private void OnPreprocessModel()
        {
            if (!HasAdjacentManifest(assetPath)) return;
            ModelImporter model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.bakeAxisConversion = true;
            model.importAnimation = false;
            model.importBlendShapes = false;
            model.importCameras = false;
            model.importLights = false;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.CalculateMikk;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.addCollider = false;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            BlenderBridgeSettings bridge = FindSettings(false);
            if (bridge == null || !bridge.AutoImport) return;
            for (int i = 0; i < imported.Length; i++) QueueRelatedManifest(imported[i]);
            Schedule();
        }

        internal static BlenderBridgeSettings FindSettings(bool logErrors)
        {
            string[] guids = AssetDatabase.FindAssets("t:BlenderBridgeSettings");
            if (guids.Length != 1)
            {
                if (logErrors)
                    Debug.LogError($"WorldBuilder: exactly one BlenderBridgeSettings asset is required; found {guids.Length}.");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<BlenderBridgeSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        internal static void LogReport(string manifestPath, WorldBakeReport report)
        {
            foreach (BakeIssue issue in report.Issues)
            {
                string message = $"WorldBuilder [{issue.Code}] {manifestPath}: {issue.Message}";
                if (issue.Severity == BakeIssueSeverity.Error) Debug.LogError(message);
                else if (issue.Severity == BakeIssueSeverity.Warning) Debug.LogWarning(message);
                else Debug.Log(message);
            }
        }

        private static void QueueRelatedManifest(string assetPath)
        {
            if (assetPath.EndsWith(".chunk.json", StringComparison.OrdinalIgnoreCase))
            {
                Queue.Add(assetPath.Replace('\\', '/'));
                return;
            }
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.EndsWith(".placements.json", StringComparison.OrdinalIgnoreCase)) return;
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            string[] manifests = Directory.GetFiles(directory, "*.chunk.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < manifests.Length; i++)
                Queue.Add(FileUtil.GetProjectRelativePath(Path.GetFullPath(manifests[i])).Replace('\\', '/'));
        }

        private static bool HasAdjacentManifest(string modelAssetPath)
        {
            string directory = Path.GetDirectoryName(modelAssetPath);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory) &&
                   Directory.GetFiles(directory, "*.chunk.json", SearchOption.TopDirectoryOnly).Length > 0;
        }

        private static void Schedule()
        {
            if (scheduled || Queue.Count == 0) return;
            scheduled = true;
            EditorApplication.delayCall += ProcessQueue;
        }

        private static void ProcessQueue()
        {
            scheduled = false;
            BlenderBridgeSettings bridge = FindSettings(true);
            if (bridge == null) { Queue.Clear(); return; }
            string[] manifests = new string[Queue.Count];
            Queue.CopyTo(manifests);
            Queue.Clear();
            for (int i = 0; i < manifests.Length; i++)
            {
                ChunkImportResult result = ChunkImportPipeline.Import(manifests[i], bridge);
                LogReport(manifests[i], result.Report);
                if (!result.Report.HasErrors && result.WasRebuilt)
                    Debug.Log($"WorldBuilder: rebuilt chunk prefab '{result.ChunkPrefabPath}'.");
            }
        }
    }
}
