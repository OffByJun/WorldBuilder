using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    [Serializable]
    public struct RiverKnot
    {
        public Vector3 position;
        [Min(0.01f)] public float width;
        [Min(0.01f)] public float depth;
        [Min(0f)] public float flowSpeed;

        public RiverKnot(Vector3 position, float width, float depth, float flowSpeed)
        {
            this.position = position;
            this.width = width;
            this.depth = depth;
            this.flowSpeed = flowSpeed;
        }
    }

    public sealed class RiverWaterBody : WaterBodyAuthoring
    {
        [SerializeField] private List<RiverKnot> knots = new List<RiverKnot>
        {
            new RiverKnot(new Vector3(0f, 0f, -5f), 4f, 2f, 1f),
            new RiverKnot(new Vector3(0f, 0f, 5f), 4f, 2f, 1f)
        };
        [SerializeField, Min(0.25f)] private float bakeSpacing = 2f;

        public IList<RiverKnot> Knots => knots;
        public float BakeSpacing { get => bakeSpacing; set => bakeSpacing = Mathf.Max(0.25f, value); }

        public Vector3 EvaluateWorldPosition(float normalizedT)
        {
            if (knots.Count == 0) return transform.position;
            if (knots.Count == 1) return transform.TransformPoint(knots[0].position);
            float scaled = Mathf.Clamp01(normalizedT) * (knots.Count - 1);
            int segment = Mathf.Min(Mathf.FloorToInt(scaled), knots.Count - 2);
            float t = scaled - segment;
            Vector3 p0 = knots[Mathf.Max(0, segment - 1)].position;
            Vector3 p1 = knots[segment].position;
            Vector3 p2 = knots[segment + 1].position;
            Vector3 p3 = knots[Mathf.Min(knots.Count - 1, segment + 2)].position;
            return transform.TransformPoint(CatmullRom(p0, p1, p2, p3, t));
        }

        public RiverKnot EvaluateKnot(float normalizedT)
        {
            if (knots.Count == 0) return new RiverKnot(Vector3.zero, 1f, 1f, 0f);
            if (knots.Count == 1) return knots[0];
            float scaled = Mathf.Clamp01(normalizedT) * (knots.Count - 1);
            int segment = Mathf.Min(Mathf.FloorToInt(scaled), knots.Count - 2);
            float t = scaled - segment;
            RiverKnot a = knots[segment];
            RiverKnot b = knots[segment + 1];
            return new RiverKnot(Vector3.zero, Mathf.Lerp(a.width, b.width, t),
                Mathf.Lerp(a.depth, b.depth, t), Mathf.Lerp(a.flowSpeed, b.flowSpeed, t));
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            bakeSpacing = Mathf.Max(0.25f, bakeSpacing);
            for (int i = 0; i < knots.Count; i++)
            {
                RiverKnot knot = knots[i];
                knot.width = Mathf.Max(0.01f, knot.width);
                knot.depth = Mathf.Max(0.01f, knot.depth);
                knot.flowSpeed = Mathf.Max(0f, knot.flowSpeed);
                knots[i] = knot;
            }
        }
    }
}
