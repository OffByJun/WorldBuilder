using System;
using UnityEngine;

namespace WorldBuilder.Runtime.WorldTime
{
    public enum DayPhase
    {
        Dawn,
        Day,
        Dusk,
        Night
    }

    /// <summary>
    /// World clock: drives time of day, optional sun rotation/intensity and exposes
    /// <see cref="Nightness"/> for zones (e.g., bioluminescence) and gameplay systems.
    /// Attach once per scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldClock : MonoBehaviour
    {
        [SerializeField] private float dayLengthSeconds = 1200f;
        [SerializeField, Range(0f, 24f)] private float startHour = 8f;
        [SerializeField] private bool advanceInEditMode;
        [SerializeField] private Light sunLight;
        [SerializeField] private float dayLightIntensity = 1.15f;
        [SerializeField] private float nightLightIntensity = 0.05f;
        [SerializeField] private float dawnDuskElevationDegrees = 12f;

        private float hour;

        public static WorldClock Instance { get; private set; }

        public event Action<int> HourChanged;
        public event Action<DayPhase> PhaseChanged;

        public float Hour => hour;
        public DayPhase Phase { get; private set; } = DayPhase.Day;

        /// <summary>0 = full day, 1 = full night, smooth through dawn/dusk.</summary>
        public float Nightness { get; private set; }

        public bool Paused { get; set; }

        public void SetHour(float value)
        {
            int before = Mathf.FloorToInt(hour);
            hour = Mathf.Repeat(value, 24f);
            RefreshPhase();
            int after = Mathf.FloorToInt(hour);
            if (after != before) HourChanged?.Invoke(after);
        }

        private void Awake()
        {
            Instance = this;
            hour = startHour;
            RefreshPhase();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Paused)
            {
                int before = Mathf.FloorToInt(hour);
                hour = Mathf.Repeat(hour + Time.deltaTime / Mathf.Max(1f, dayLengthSeconds) * 24f, 24f);
                int after = Mathf.FloorToInt(hour);
                if (after != before) HourChanged?.Invoke(after);
                RefreshPhase();
            }
            ApplySun();
        }

        private void RefreshPhase()
        {
            float elevation = SunElevationDegrees();
            DayPhase phase =
                elevation >= dawnDuskElevationDegrees ? DayPhase.Day :
                elevation <= -dawnDuskElevationDegrees ? DayPhase.Night :
                hour >= 6f && hour < 12f ? DayPhase.Dawn : DayPhase.Dusk;

            if (phase != Phase)
            {
                Phase = phase;
                PhaseChanged?.Invoke(phase);
            }

            Nightness = 1f - Mathf.InverseLerp(-dawnDuskElevationDegrees, dawnDuskElevationDegrees, elevation);
        }

        private float SunElevationDegrees()
        {
            // Simple circular path: noon at the top, midnight at the bottom.
            return Mathf.Sin((hour / 24f) * Mathf.PI * 2f - Mathf.PI / 2f) * 90f;
        }

        private void ApplySun()
        {
            if (sunLight == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying && !advanceInEditMode && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Still reflect the configured start hour when not simulating.
            }
#endif
            float elevation01 = Mathf.InverseLerp(-90f, 90f, SunElevationDegrees());
            sunLight.transform.rotation = Quaternion.Euler(SunElevationDegrees(), 170f, 0f);
            sunLight.intensity = Mathf.Lerp(nightLightIntensity, dayLightIntensity, elevation01);
        }
    }
}
