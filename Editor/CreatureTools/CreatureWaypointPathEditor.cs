using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Creatures;

namespace WorldBuilder.Editor.CreatureTools
{
    /// <summary>Scene-view loop rendering + waypoint editing helpers for patrol paths.</summary>
    [CustomEditor(typeof(CreatureWaypointPath))]
    public sealed class CreatureWaypointPathEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var path = (CreatureWaypointPath)target;
            var points = path.Points;
            if (points == null || points.Count == 0) return;

            Handles.color = new Color(0.4f, 1f, 0.75f, 0.9f);
            Vector3 previous = path.Evaluate(0f);
            int segments = path.ClosedLoop ? points.Count : points.Count - 1;
            for (int s = 0; s < segments; s++)
            {
                const int steps = 10;
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 next = path.Evaluate(s + i / (float)steps);
                    Handles.DrawLine(previous, next);
                    previous = next;
                }
            }

            Handles.color = Color.white;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 world = path.GetWorldPosition(i);
                float size = HandleUtility.GetHandleSize(world) * 0.12f;
                Handles.SphereHandleCap(i, world, Quaternion.identity, size, EventType.Repaint);
                Handles.Label(world + Vector3.up * 0.6f,
                    $"#{i}  dwell {path.GetDwell(i):0.#}s");
            }
        }
    }
}
