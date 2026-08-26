using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.WorldTime
{
    /// <summary>
    /// Tints whatever atmosphere other systems wrote (weather, environment FX) by time of
    /// day and season — instead of owning absolute colors, so every layer keeps working:
    /// fog/ambient get multiplied toward cool night tones, the sun picks its color from an
    /// elevation gradient, and a season palette optionally warms/cools everything.
    /// Attach alongside <see cref="WorldClock"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightAtmosphere : MonoBehaviour
    {
        [SerializeField] private WorldClock clock;
        [SerializeField] private Light sunLight;

        [Header("Sun Color (key: 0 midnight · 0.5 noon)")]
        [SerializeField] private Gradient sunColor = BuildDefaultSunGradient();

        [Header("Night Tint (multiplied onto fog & ambient)")]
        [SerializeField] private Color nightTint = new Color(0.45f, 0.52f, 0.78f);
        [Tooltip("Ambient light multiplier at deep night.")]
        [Range(0f, 1f)] [SerializeField] private float nightAmbientScale = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float blendSpeed = 3f;

        [Header("Season")]
        [Tooltip("Optional palette; shifts ambient warmth with the season value.")]
        [SerializeField] private SeasonPalette seasonPalette;
        [SerializeField] private BiomeType seasonReferenceBiome = BiomeType.Forest;
        [Range(-1f, 5f)] [SerializeField] private float seasonValue = 1f; // summer default

        private float blendedDaylight = 1f;

        public SeasonPalette Season
        {
            get => seasonPalette;
            set => seasonPalette = value;
        }

        public void SetSeason(float value) => seasonValue = Mathf.Repeat(value, 4f);

        private void LateUpdate()
        {
            WorldClock activeClock = clock != null ? clock : WorldClock.Instance;
            if (activeClock == null) return;

            // Smooth daylight factor from the clock's nightness.
            float targetDaylight = 1f - activeClock.Nightness;
            blendedDaylight = Mathf.MoveTowards(blendedDaylight, targetDaylight,
                Mathf.Max(0.1f, blendSpeed) * Time.deltaTime);

            Color tint = Color.Lerp(nightTint, Color.white, blendedDaylight);

            // Season shift: winter cools, autumn warms — sampled from the reference biome.
            if (seasonPalette != null)
            {
                Color seasonal = seasonPalette.Sample(seasonReferenceBiome, seasonValue);
                Color neutral = seasonPalette.Sample(seasonReferenceBiome, 1f); // summer baseline
                Vector3 delta = new Vector3(seasonal.r - neutral.r,
                    seasonal.g - neutral.g, seasonal.b - neutral.b);
                Vector3 tinted = new Vector3(tint.r + delta.x * 0.25f,
                    tint.g + delta.y * 0.25f, tint.b + delta.z * 0.25f);
                tint = new Color(tinted.x, tinted.y, tinted.z, 1f);
            }

            RenderSettings.fogColor *= tint;
            RenderSettings.ambientLight *= tint;
            RenderSettings.ambientIntensity *=
                Mathf.Lerp(nightAmbientScale, 1f, blendedDaylight);

            if (sunLight == null && activeClock != null) sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                float elevation01 = Mathf.InverseLerp(-90f, 90f, SunElevation(activeClock));
                sunLight.color = sunColor.Evaluate(elevation01);
            }
        }

        private static float SunElevation(WorldClock clock)
        {
            // Mirror of WorldClock's circular path so gradients key on the same curve.
            return Mathf.Sin((clock.Hour / 24f) * Mathf.PI * 2f - Mathf.PI / 2f) * 90f;
        }

        private static Gradient BuildDefaultSunGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.25f, 0.32f, 0.55f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.30f), 0.40f),
                    new GradientColorKey(new Color(1f, 0.96f, 0.88f), 0.50f),
                    new GradientColorKey(new Color(1f, 0.52f, 0.28f), 0.60f),
                    new GradientColorKey(new Color(0.22f, 0.28f, 0.5f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
