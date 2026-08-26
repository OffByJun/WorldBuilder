using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Streaming
{
    /// <summary>
    /// One-click streaming budgets: view-distance radii and refresh cadence tuned for
    /// handheld / desktop / dedicated-server profiles. Pair with
    /// <see cref="StreamingBudgetDriver"/> which feeds <see cref="ChunkStreamingService"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Streaming/Streaming Budget Preset",
        fileName = "StreamingBudgetPreset")]
    public sealed class StreamingBudgetPreset : ScriptableObject
    {
        public enum Profile
        {
            Handheld,
            Desktop,
            Server
        }

        [Header("Profile")]
        public Profile profile = Profile.Desktop;

        [Header("Focus")]
        [Tooltip("Regions kept loaded around the focus point (Chebyshev radius).")]
        [Min(0)] public int regionRadius = 2;
        [Tooltip("Seconds between focus re-evaluations.")]
        [Min(0.1f)] public float focusIntervalSeconds = 1f;

        public static StreamingBudgetPreset Defaults(Profile profileKind)
        {
            var preset = CreateInstance<StreamingBudgetPreset>();
            preset.profile = profileKind;
            switch (profileKind)
            {
                case Profile.Handheld:
                    preset.regionRadius = 1;
                    preset.focusIntervalSeconds = 2f;
                    break;
                case Profile.Server:
                    preset.regionRadius = 4;
                    preset.focusIntervalSeconds = 0.5f;
                    break;
                default:
                    preset.regionRadius = 2;
                    preset.focusIntervalSeconds = 1f;
                    break;
            }
            return preset;
        }
    }

    /// <summary>
    /// Applies a <see cref="StreamingBudgetPreset"/> to a chunk streaming service on an
    /// interval, re-focusing as the tracked transform moves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreamingBudgetDriver : MonoBehaviour
    {
        [SerializeField] private StreamingBudgetPreset preset;
        [SerializeField] private ChunkStreamingService service;
        [SerializeField] private Transform focusOverride;

        private float timer;

        public StreamingBudgetPreset Preset
        {
            get => preset;
            set => preset = value;
        }

        public ChunkStreamingService Service
        {
            get => service;
            set => service = value;
        }

        public Transform FocusTarget
        {
            get => focusOverride != null ? focusOverride : transform;
            set => focusOverride = value;
        }

        private void Update()
        {
            if (preset == null || service == null || FocusTarget == null) return;

            timer += Time.unscaledDeltaTime;
            if (timer < preset.focusIntervalSeconds) return;
            timer = 0f;

            // Fire-and-forget, but surface faults instead of silently swallowing them.
            System.Threading.Tasks.Task focusTask = service.SetFocusAsync(
                FocusTarget.position, preset.regionRadius, destroyCancellationToken);
            focusTask.ContinueWith(t => Debug.LogException(
                    t.Exception?.GetBaseException() ?? new Exception("region focus failed")),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
