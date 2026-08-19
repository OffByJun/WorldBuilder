using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.TransformBatchTool
{
    public enum AlignAxis
    {
        X,
        Y,
        Z
    }

    public enum AlignMode
    {
        Min,
        Center,
        Max
    }

    public static class TransformBatchService
    {
        public static void Align(IList<GameObject> targets, AlignAxis axis, AlignMode mode)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            int index = (int)axis;
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < targets.Count; i++)
            {
                float value = targets[i].transform.position[index];
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            float reference = mode == AlignMode.Min ? min : mode == AlignMode.Max ? max : (min + max) * 0.5f;

            for (int i = 0; i < targets.Count; i++)
            {
                Transform t = targets[i].transform;
                Undo.RecordObject(t, "Align");
                Vector3 position = t.position;
                position[index] = reference;
                t.position = position;
            }
        }

        public static void Distribute(IList<GameObject> targets, AlignAxis axis, float spacing)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            int index = (int)axis;
            List<GameObject> sorted = new List<GameObject>(targets);
            sorted.Sort((a, b) => a.transform.position[index].CompareTo(b.transform.position[index]));

            float start = sorted[0].transform.position[index];
            for (int i = 0; i < sorted.Count; i++)
            {
                Transform t = sorted[i].transform;
                Undo.RecordObject(t, "Distribute");
                Vector3 position = t.position;
                position[index] = start + spacing * i;
                t.position = position;
            }
        }

        public static void Reset(IList<GameObject> targets, bool position, bool rotation, bool scale)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Transform t = targets[i].transform;
                Undo.RecordObject(t, "Reset Transform");

                if (position)
                {
                    t.localPosition = Vector3.zero;
                }

                if (rotation)
                {
                    t.localRotation = Quaternion.identity;
                }

                if (scale)
                {
                    t.localScale = Vector3.one;
                }
            }
        }
    }
}
