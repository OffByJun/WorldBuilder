using UnityEngine;

namespace WorldBuilder.Runtime.Fx
{
    /// <summary>
    /// Drives the global terrain snow channel (<c>_WB_Snow</c>, consumed by TerrainSplat).
    /// Manual coverage or automatic winter blending via <see cref="WorldBuilder.Runtime.Zones.SeasonState"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowCoverageDriver : MonoBehaviour
    {
        [Tooltip("Manual coverage when autoWinter is off.")]
        [Range(0f, 1f)] [SerializeField] private float coverage;
        [Tooltip("Blend toward full snow while SeasonState.CurrentSeason == Winter.")]
        [SerializeField] private bool autoWinter = true;
        [Range(0f, 1f)] [SerializeField] private float winterCoverage = 0.8f;
        [Min(0.1f)] [SerializeField] private float lerpSpeed = 0.5f;

        private static readonly int SnowId = Shader.PropertyToID("_WB_Snow");
        private float smoothed;

        public float Coverage
        {
            get => coverage;
            set => coverage = Mathf.Clamp01(value);
        }

        private void Update()
        {
            float target = coverage;
            if (autoWinter)
            {
                bool winter = Zones.SeasonState.CurrentSeason == Zones.SeasonState.Winter;
                target = winter ? Mathf.Max(coverage, winterCoverage) : coverage * 0.25f;
            }
            smoothed = Mathf.MoveTowards(smoothed, target,
                Mathf.Max(0.05f, lerpSpeed) * Time.deltaTime);
            Shader.SetGlobalFloat(SnowId, smoothed);
        }

        private void OnDisable() => Shader.SetGlobalFloat(SnowId, 0f);
    }
}
