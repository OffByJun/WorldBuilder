using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldBuilder.Authoring.Water;

namespace WorldBuilder.Editor.WaterAuthoring
{
    internal static class WaterAuthoringColors
    {
        public static readonly Color Water = new Color(0.1f, 0.55f, 1f, 0.75f);
        public static readonly Color Air = new Color(1f, 0.5f, 0.1f, 0.9f);
        public static readonly Color Flow = new Color(0.2f, 1f, 0.9f, 1f);
    }

    [CustomEditor(typeof(RiverWaterBody))]
    public sealed class RiverWaterBodyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => DrawDefaultInspector();

        private void OnSceneGUI()
        {
            RiverWaterBody river = (RiverWaterBody)target;
            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = WaterAuthoringColors.Water;
            const int samples = 64;
            Vector3 previous = river.EvaluateWorldPosition(0f);
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 current = river.EvaluateWorldPosition(t);
                Handles.DrawAAPolyLine(3f, previous, current);
                if (i % 8 == 0)
                {
                    RiverKnot knot = river.EvaluateKnot(t);
                    Vector3 tangent = current - previous;
                    Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized * knot.width * 0.5f;
                    Handles.DrawLine(current - right, current + right);
                }
                previous = current;
            }

            for (int i = 0; i < river.Knots.Count; i++)
            {
                RiverKnot knot = river.Knots[i];
                Vector3 world = river.transform.TransformPoint(knot.position);
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(world, river.transform.rotation);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(river, "Move River Knot");
                knot.position = river.transform.InverseTransformPoint(moved);
                river.Knots[i] = knot;
                EditorUtility.SetDirty(river);
            }
        }
    }

    [CustomEditor(typeof(LakeWaterBody))]
    public sealed class LakeWaterBodyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => DrawDefaultInspector();

        private void OnSceneGUI()
        {
            LakeWaterBody lake = (LakeWaterBody)target;
            Handles.color = WaterAuthoringColors.Water;
            for (int i = 0; i < lake.Polygon.Count; i++)
            {
                int next = (i + 1) % lake.Polygon.Count;
                Vector3 world = lake.transform.TransformPoint(lake.Polygon[i]);
                Vector3 nextWorld = lake.transform.TransformPoint(lake.Polygon[next]);
                Handles.DrawAAPolyLine(3f, world, nextWorld);
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(world, lake.transform.rotation);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(lake, "Move Lake Vertex");
                lake.Polygon[i] = lake.transform.InverseTransformPoint(moved);
                EditorUtility.SetDirty(lake);
            }
        }
    }

    [CustomEditor(typeof(BoxWaterBodyAuthoring), true)]
    public sealed class BoxWaterBodyAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => DrawDefaultInspector();

        private void OnSceneGUI()
        {
            BoxWaterBodyAuthoring box = (BoxWaterBodyAuthoring)target;
            Matrix4x4 previous = Handles.matrix;
            Color previousColor = Handles.color;
            Handles.matrix = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.Center);
            Handles.color = box is AirOverrideVolume ? WaterAuthoringColors.Air : WaterAuthoringColors.Water;
            Handles.DrawWireCube(Vector3.zero, box.Size);
            Handles.matrix = previous;
            Handles.color = previousColor;
        }
    }

    public static class OceanWaterBodyGizmo
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void Draw(OceanWaterBody ocean, GizmoType gizmoType)
        {
            Color previous = Gizmos.color;
            Gizmos.color = new Color(0.1f, 0.55f, 1f, 0.25f);
            Vector3 center = ocean.transform.position;
            center.y = ocean.SeaLevel;
            Gizmos.DrawCube(center, new Vector3(100f, 0.02f, 100f));
            Gizmos.color = previous;
        }
    }
}
