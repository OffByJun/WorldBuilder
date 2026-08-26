using UnityEngine;
using WorldBuilder.Runtime.WorldTime;

namespace WorldBuilder.Runtime.Fx
{
    /// <summary>
    /// Rain/snow particle driver keyed off <see cref="SimpleWeatherController"/> states.
    /// While raining it also: (1) raises the global terrain wetness channel
    /// (<c>_WB_Wetness</c>, consumed by TerrainSplat) and (2) optionally feeds a
    /// <see cref="Water.WaterLevelDriver"/> so rivers swell during storms — closing the
    /// weather → visuals → water loop with one component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrecipitationFx : MonoBehaviour
    {
        [SerializeField] private SimpleWeatherController weather;
        [SerializeField] private ParticleSystem rainPrefab;
        [SerializeField] private Transform emitAnchor;
        [Tooltip("Radius of the procedural fallback rain column when no prefab is assigned.")]
        [Min(2f)] [SerializeField] private float fallbackRadius = 18f;

        [Header("Wetness")]
        [Range(0f, 1f)] [SerializeField] private float maxWetness = 0.85f;
        [SerializeField] private float wetLerpSpeed = 0.4f;
        private static readonly int WetnessId = Shader.PropertyToID("_WB_Wetness");

        [Header("Water link")]
        [SerializeField] private Water.WaterLevelDriver waterDriver;
        [SerializeField] private float stormIntensity = 0.85f;

        private ParticleSystem activeRain;
        private float wetness;

        public float Wetness => wetness;

        public void Configure(SimpleWeatherController controller) => weather = controller;

        private void OnEnable()
        {
            if (weather != null) weather.WeatherChanged += OnWeatherChanged;
            ApplyState(weather != null ? weather.Current : WeatherState.Clear);
        }

        private void OnDisable()
        {
            if (weather != null) weather.WeatherChanged -= OnWeatherChanged;
            SetRainActive(false);
            Shader.SetGlobalFloat(WetnessId, 0f);
        }

        private void OnWeatherChanged(WeatherState state) => ApplyState(state);

        private void ApplyState(WeatherState state)
        {
            bool raining = state == WeatherState.Rain || state == WeatherState.Overcast;
            SetRainActive(state == WeatherState.Rain);
            if (!raining && waterDriver != null) waterDriver.SetIntensity(0.35f); // dry-out target
        }

        private void SetRainActive(bool active)
        {
            if (active)
            {
                if (activeRain == null)
                    activeRain = rainPrefab != null
                        ? Instantiate(rainPrefab, emitAnchor != null ? emitAnchor : transform)
                        : BuildFallbackRain();
                if (!activeRain.isPlaying) activeRain.Play();
                if (waterDriver != null) waterDriver.SetIntensity(stormIntensity);
            }
            else if (activeRain != null && activeRain.isPlaying)
            {
                activeRain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Update()
        {
            bool raining = activeRain != null && activeRain.isEmitting;
            float target = raining ? maxWetness : 0f;
            wetness = Mathf.MoveTowards(wetness, target,
                Mathf.Max(0.05f, wetLerpSpeed) * Time.deltaTime * (raining ? 1f : 0.6f));
            Shader.SetGlobalFloat(WetnessId, wetness);

            // Keep the fallback column over the listener/camera area.
            if (activeRain != null && emitAnchor == null &&
                Camera.main != null && !Camera.main.transform.IsChildOf(transform))
                activeRain.transform.position = Camera.main.transform.position;
        }

        private ParticleSystem BuildFallbackRain()
        {
            var go = new GameObject("WB_FallbackRain");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 18f);
            main.startSize3D = true;
            main.startSizeX = 0.02f;
            main.startSizeY = 0.45f;
            main.startSizeZ = 0.02f;
            main.gravityModifier = 0.35f;
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = fallbackRadius;
            shape.angle = 2f;
            shape.rotation = Vector3.right * -90f; // fall downward from a dome

            var emission = ps.emission;
            emission.rateOverTime = 900f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                    ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(particleShader);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return ps;
        }
    }
}
