using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.GameplayTools
{
    public static class PoiCandidateMenu
    {
        [MenuItem("WorldBuilder/Audit/Suggest POI Candidates")]
        public static void Suggest()
        {
            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset.");
                return;
            }
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            var sampler = new VoxelWorldSampler(store, 128f);
            var area = new Bounds(new Vector3(pivot.x, 16f, pivot.z),
                new Vector3(512f, 96f, 512f));
            var list = PoiCandidateAnalyzer.Analyze(sampler, null, area, seaLevel: 0f);

            var report = new System.Text.StringBuilder($"POI candidates: {list.Count}\n");
            foreach (var candidate in list)
                report.AppendLine(
                    $"  [{candidate.Reason}] {candidate.Position} score={candidate.Score:F1}");

            foreach (var candidate in list)
            {
                var marker = new GameObject($"POI_{candidate.Reason}");
                marker.transform.position = candidate.Position;
                Undo.RegisterCreatedObjectUndo(marker, "Suggest POI");
            }
            Debug.Log(report.ToString());
        }
    }
}
