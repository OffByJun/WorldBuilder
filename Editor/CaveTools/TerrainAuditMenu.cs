using System.Text;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.CaveTools
{
    /// <summary>Terrain audit runner and ecology preset asset creation menus.</summary>
    public static class TerrainAuditMenu
    {
        [MenuItem("WorldBuilder/Audit/Run Terrain Checks")]
        public static void RunTerrainChecks()
        {
            Runtime.Data.VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset found.");
                return;
            }

            var report = new StringBuilder();
            int total = 0;

            var isolated = WorldAuditRules.CheckIsolatedChunks(store);
            total += isolated.Count;
            report.AppendLine($"Isolated chunks: {isolated.Count}");
            foreach (Runtime.Terrain.AuditIssue issue in isolated)
                report.AppendLine($"  {issue.Chunk} — {issue.Message}");

            var sanity = WorldAuditRules.CheckDensitySanity(store);
            total += sanity.Count;
            report.AppendLine($"Density anomalies: {sanity.Count}");
            foreach (Runtime.Terrain.AuditIssue issue in sanity)
                report.AppendLine($"  {issue.Chunk} — {issue.Message}");

            var borders = WorldAuditRules.CheckBorderContinuity(store);
            total += borders.Count;
            report.AppendLine($"Border mismatches: {borders.Count}");
            foreach (Runtime.Terrain.AuditIssue issue in borders)
                report.AppendLine($"  {issue.Chunk} — {issue.Message}");

            Debug.Log($"[WorldBuilder] Terrain audit finished — {total} issue(s).\n{report}",
                store);
        }

        [MenuItem("WorldBuilder/PCG/Create Rule Set/Coral Reef Ecology")]
        public static void CreateCoralReefRules() => CreateEcology(ScatterRuleSetFactory.EcologyKind.CoralReef);

        [MenuItem("WorldBuilder/PCG/Create Rule Set/Kelp Forest Ecology")]
        public static void CreateKelpForestRules() => CreateEcology(ScatterRuleSetFactory.EcologyKind.KelpForest);

        [MenuItem("WorldBuilder/PCG/Create Rule Set/Cave Interior Ecology")]
        public static void CreateCaveInteriorRules() => CreateEcology(ScatterRuleSetFactory.EcologyKind.CaveInterior);

        private static void CreateEcology(ScatterRuleSetFactory.EcologyKind kind)
        {
            ScatterRuleSet set = ScatterRuleSetFactory.Create(kind,
                $"{kind}Ecology");
            string directory = GetActiveFolder();
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{kind}Ecology.asset");
            AssetDatabase.CreateAsset(set, path);
            Selection.activeObject = set;
            Debug.Log($"[WorldBuilder] Created {path}. Assign prefabs to its rules to activate.", set);
        }

        private static string GetActiveFolder()
        {
            Object active = Selection.activeObject;
            string activePath = active != null ? AssetDatabase.GetAssetPath(active) : null;
            if (!string.IsNullOrEmpty(activePath) &&
                AssetDatabase.IsValidFolder(activePath))
                return activePath;
            if (!string.IsNullOrEmpty(activePath))
                return System.IO.Path.GetDirectoryName(activePath)?.Replace('\\', '/') ?? "Assets";
            return "Assets";
        }
    }
}
