using UnityEngine;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Weather → water coupling: maps a 0..1 intensity (0 = drought, 0.5 = normal,
    /// 1 = flood) to a sea level offset and a river/ocean flow speed multiplier, applied
    /// as runtime-only adjustments on <see cref="WaterWorldRuntimeData"/>. Feed intensity
    /// from your weather system; values ease toward the target for natural transitions.
    /// Note: rebuild <see cref="NativeWaterQuery"/> after offsets change so the Burst path
    /// sees the new sea level.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterLevelDriver : MonoBehaviour
    {
        [SerializeField] private WaterWorldRuntimeData target;
        [SerializeField, Range(0f, 1f)] private float intensity = 0.5f;
        [Tooltip("Sea level rise at full flood (meters).")]
        [SerializeField] private float maxRise = 1.5f;
        [Tooltip("Sea level drop at full drought (meters).")]
        [SerializeField] private float maxDrop = 0.8f;
        [Tooltip("Flow speed multiplier at drought / flood extremes.")]
        [SerializeField] private Vector2 speedMultiplierRange = new Vector2(0.55f, 2.2f);
        [SerializeField] private float responseSpeed = 0.4f;

        private float smoothed = 0.5f;

        public WaterWorldRuntimeData Target
        {
            get => target;
            set => target = value;
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            if (!Application.isPlaying) Apply(immediate: true);
        }

        public float CurrentSmoothed => smoothed;

        private void OnEnable() => Apply(immediate: true);

        private void Update()
        {
            smoothed = Mathf.MoveTowards(smoothed, intensity,
                Mathf.Max(0.01f, responseSpeed) * Time.deltaTime);
            Apply();
        }

        private void Apply(bool immediate = false)
        {
            if (target == null) return;
            float t = immediate ? intensity : smoothed;
            float offset = Mathf.Lerp(-maxDrop, maxRise, t);
            float multiplier = Mathf.Lerp(speedMultiplierRange.x, speedMultiplierRange.y, t);
            target.SetRuntimeSeaLevelOffset(offset);
            target.SetRuntimeFlowSpeedMultiplier(multiplier);
        }

        private void OnDisable()
        {
            if (target == null) return;
            target.SetRuntimeSeaLevelOffset(0f);
            target.SetRuntimeFlowSpeedMultiplier(1f);
        }
    }
}
