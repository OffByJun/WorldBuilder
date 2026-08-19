using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Authoring.Water;
using WorldBuilder.Baking.Core;
using WorldBuilder.Baking.Water;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Editor.WaterAuthoring
{
    public static class WaterBakeMenu
    {
        [MenuItem("Tools/WorldBuilder/Water/Bake Scene Query Data")]
        private static void BakeScene()
        {
            WorldGridSettings settings = FindUniqueSettings();
            if (settings == null) return;
            WaterBodyAuthoring[] found = Object.FindObjectsByType<WaterBodyAuthoring>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            List<WaterBodyAuthoring> bodies = new List<WaterBodyAuthoring>(found);
            WaterBakeStep waterStep = new WaterBakeStep(bodies);
            WorldBakeContext context = new WorldBakeContext(settings);
            WorldBakeReport report = new WorldBakePipeline(new IWorldBakeStep[] { waterStep }).Run(context);
            Log(report);
            if (report.HasErrors)
            {
                if (waterStep.Result != null) Object.DestroyImmediate(waterStep.Result);
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save Water Runtime Data",
                "WaterWorldRuntimeData", "asset", "Choose a generated data asset path.");
            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(waterStep.Result);
                return;
            }
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(waterStep.Result, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = waterStep.Result;
            Debug.Log($"WorldBuilder: baked {bodies.Count} water bodies to '{path}' ({context.BuildOutputHash()}).");
        }

        private static WorldGridSettings FindUniqueSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:WorldGridSettings");
            if (guids.Length != 1)
            {
                Debug.LogError($"WorldBuilder: expected exactly one authoritative WorldGridSettings asset, found {guids.Length}.");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<WorldGridSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void Log(WorldBakeReport report)
        {
            foreach (BakeIssue issue in report.Issues)
            {
                string message = $"WorldBuilder [{issue.Code}] {issue.Path}: {issue.Message}";
                if (issue.Severity == BakeIssueSeverity.Error) Debug.LogError(message);
                else if (issue.Severity == BakeIssueSeverity.Warning) Debug.LogWarning(message);
                else Debug.Log(message);
            }
        }
    }
}
