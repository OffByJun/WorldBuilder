using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.ComponentGizmos
{
    /// <summary>Depth ring + fish table summary for fishing spots.</summary>
    [CustomEditor(typeof(FishingSpot))]
    public sealed class FishingSpotGizmoEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var spot = (FishingSpot)target;
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.8f);
            Vector3 p = spot.transform.position;
            Handles.DrawWireDisc(p, Vector3.up, 3f);
            Handles.Label(p + Vector3.up * 1.5f,
                $"FishingSpot · {spot.Table.Count} fish entries");
        }
    }

    /// <summary>Growth state badge for harvestable nodes.</summary>
    [CustomEditor(typeof(HarvestableNode))]
    public sealed class HarvestableNodeGizmoEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var node = (HarvestableNode)target;
            bool ready = node.ReadyForHarvest;
            Handles.color = ready
                ? new Color(0.4f, 1f, 0.5f, 0.9f)
                : new Color(1f, 0.75f, 0.3f, 0.6f);
            Vector3 p = node.transform.position;
            float size = HandleUtility.GetHandleSize(p) * 0.25f;
            Handles.SphereHandleCap(0, p, Quaternion.identity, size, EventType.Repaint);
            Handles.Label(p + Vector3.up * 0.8f,
                ready ? "READY" : "growing…");
        }
    }

    /// <summary>Air meter bar above water-breathing creatures.</summary>
    [CustomEditor(typeof(WaterBreather))]
    public sealed class WaterBreatherGizmoEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var breather = (WaterBreather)target;
            Vector3 p = breather.transform.position + Vector3.up * 1.4f;
            float ratio = breather.AirRatio;

            Handles.color = new Color(0.2f, 0.2f, 0.25f, 0.8f);
            Handles.DrawLine(p - Vector3.right * 0.6f, p + Vector3.right * 0.6f);

            Handles.color = ratio > 0.35f
                ? new Color(0.3f, 0.85f, 1f)
                : new Color(1f, 0.35f, 0.25f);
            Handles.DrawLine(p - Vector3.right * 0.6f,
                p + Vector3.right * (ratio * 1.2f - 0.6f));

            if (breather.IsDrowning)
                Handles.Label(p + Vector3.up * 0.3f, "DROWNING");
        }
    }

    /// <summary>Highlights chunks flagged by the last collapse scan.</summary>
    [CustomEditor(typeof(CollapseWatcher))]
    public sealed class CollapseWatcherGizmoEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var watcher = (CollapseWatcher)target;
            if (watcher.LastDetachedCount == 0) return;

            Handles.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            Handles.Label(watcher.transform.position + Vector3.up * 2f,
                $"CollapseWatcher: {watcher.LastDetachedCount} detached voxel(s)");
        }
    }
}
