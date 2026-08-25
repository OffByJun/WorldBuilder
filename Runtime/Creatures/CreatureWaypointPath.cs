using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Creatures
{
    /// <summary>
    /// Authored creature patrol route: waypoints with dwell times, evaluated through a
    /// closed Catmull-Rom spline. Creatures (or any mover) call
    /// <see cref="Evaluate"/>/<see cref="Advance"/>; the editor draws the loop in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureWaypointPath : MonoBehaviour
    {
        [Serializable]
        public sealed class Waypoint
        {
            public Vector3 localPosition = Vector3.zero;
            [Min(0f)] public float dwellSeconds = 2f;
            [Tooltip("Speed multiplier while travelling toward this waypoint.")]
            public float speedScale = 1f;
        }

        [SerializeField] private List<Waypoint> waypoints = new List<Waypoint>
        {
            new Waypoint { localPosition = new Vector3(0f, 0f, 8f) },
            new Waypoint { localPosition = new Vector3(7f, 0f, 0f) },
            new Waypoint { localPosition = new Vector3(0f, 0f, -8f) },
            new Waypoint { localPosition = new Vector3(-7f, 0f, 0f) }
        };
        [SerializeField] private bool closedLoop = true;
        [Min(1)] [SerializeField] private int samplesPerSegment = 12;

        private float[] cumulativeLengths;

        public IReadOnlyList<Waypoint> Points => waypoints;
        public bool ClosedLoop => closedLoop;

        public Vector3 GetWorldPosition(int index) =>
            transform.TransformPoint(waypoints[Validate(index)].localPosition);

        public float GetDwell(int index) => waypoints[Validate(index)].dwellSeconds;

        public float GetSpeedScale(int index) => waypoints[Validate(index)].speedScale;

        /// <summary>Total world-space length of the route.</summary>
        public float TotalLength()
        {
            BuildLengthTable();
            return cumulativeLengths[cumulativeLengths.Length - 1];
        }

        /// <summary>World position at normalized distance [0..TotalLength()].</summary>
        public Vector3 EvaluateAtDistance(float distance)
        {
            BuildLengthTable();
            int count = waypoints.Count;
            if (count == 0) return transform.position;
            if (count == 1) return transform.position;

            float total = cumulativeLengths[cumulativeLengths.Length - 1];
            if (!closedLoop)
            {
                distance = Mathf.Clamp(distance, 0f, total);
                if (distance >= total) return GetWorldPosition(count - 1);
            }
            else
            {
                distance = Mathf.Repeat(distance, total);
            }

            // Binary search the sample row containing this distance, then map the flat
            // index back to (segment, sub-t) spline coordinates.
            int low = 0, high = cumulativeLengths.Length - 1;
            while (low < high - 1)
            {
                int mid = (low + high) / 2;
                if (cumulativeLengths[mid] <= distance) low = mid; else high = mid;
            }

            int segments = closedLoop ? count : count - 1;
            int maxRow = segments * samplesPerSegment; // rows before the terminal point
            int row = Mathf.Min(low, maxRow - 1);
            float rowLength = cumulativeLengths[row + 1] - cumulativeLengths[row];
            float frac = rowLength > 1e-5f ? (distance - cumulativeLengths[row]) / rowLength : 0f;

            int segmentIndex = row / samplesPerSegment;
            float u = (row % samplesPerSegment + Mathf.Clamp01(frac)) / samplesPerSegment;
            return CatmullRom(Mathf.Min(segmentIndex, segments - 1), Mathf.Clamp01(u));
        }

        /// <summary>Evaluates the spline at continuous parameter u in [0..segmentCount].</summary>
        public Vector3 Evaluate(float u)
        {
            int count = waypoints.Count;
            if (count == 0) return transform.position;
            int segments = closedLoop ? count : count - 1;
            if (segments <= 0) return GetWorldPosition(0);

            if (closedLoop) u = Mathf.Repeat(u, segments);
            else u = Mathf.Clamp(u, 0f, segments);

            int index = Mathf.Min(Mathf.FloorToInt(u), segments - 1);
            return CatmullRom(index, u - index);
        }

        private Vector3 CatmullRom(int segment, float t)
        {
            int count = waypoints.Count;
            int p1 = Validate(segment);
            int p2 = Validate((segment + 1) % count);
            int p0 = Validate((segment - 1 + count) % count);
            int p3 = Validate((segment + 2) % count);
            if (!closedLoop)
            {
                p0 = Mathf.Max(0, segment - 1);
                p3 = Mathf.Min(count - 1, segment + 2);
            }

            Vector3 a = 2f * GetWorldPosition(p0);
            Vector3 b = GetWorldPosition(p1);
            Vector3 c = GetWorldPosition(p2);
            Vector3 d = GetWorldPosition(p3);

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((b * 2f) + (-a + c) * t +
                           (2f * a - 5f * b + 4f * c - d) * t2 +
                           (-a + 3f * b - 3f * c + d) * t3);
        }

        private void BuildLengthTable()
        {
            if (cumulativeLengths != null && cumulativeLengths.Length == waypoints.Count + 1 &&
                cachedVersion == waypoints.Count * 100000 + samplesPerSegment * (closedLoop ? 1 : 2))
                return;

            int segments = closedLoop ? waypoints.Count : waypoints.Count - 1;
            var lengths = new List<float> { 0f };
            Vector3 previous = GetWorldPosition(0);
            for (int s = 0; s < segments; s++)
            {
                for (int i = 1; i <= samplesPerSegment; i++)
                {
                    Vector3 point = CatmullRom(s, (float)i / samplesPerSegment);
                    lengths.Add(lengths[lengths.Count - 1] + Vector3.Distance(previous, point));
                    previous = point;
                }
            }
            cumulativeLengths = lengths.ToArray();
            cachedVersion = waypoints.Count * 100000 + samplesPerSegment * (closedLoop ? 1 : 2);
        }

        private int cachedVersion = -1;

        private int Validate(int index)
        {
            int count = waypoints.Count;
            if (count == 0) throw new InvalidOperationException("Waypoint path has no points.");
            return ((index % count) + count) % count;
        }
    }
}
