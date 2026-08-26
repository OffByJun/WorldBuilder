using System;
using UnityEngine;

namespace WorldBuilder.Runtime.WorldTime
{
    public enum WeatherState
    {
        Clear,
        Overcast,
        Fog,
        Rain
    }

    /// <summary>
    /// Minimal weather controller: transitions RenderSettings fog/ambient between states.
    /// Visual polish (particles, clouds) stays in game code; this only owns the state
    /// machine and blending so zones like Visibility keep working predictably.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimpleWeatherController : MonoBehaviour
    {
        [Serializable]
        public sealed class WeatherProfile
        {
            public WeatherState state = WeatherState.Clear;
            public Color fogColor = new Color(0.65f, 0.75f, 0.85f);
            public Color ambientColor = new Color(0.6f, 0.7f, 0.9f);
            public float ambientIntensity = 1f;
            public float fogDensity;
            public float transitionSeconds = 4f;
        }

        [SerializeField] private WeatherProfile clear =
            new WeatherProfile { state = WeatherState.Clear, transitionSeconds = 3f };
        [SerializeField] private WeatherProfile overcast =
            new WeatherProfile { state = WeatherState.Overcast, fogDensity = 0.008f, transitionSeconds = 5f };
        [SerializeField] private WeatherProfile fog =
            new WeatherProfile { state = WeatherState.Fog, fogDensity = 0.035f, transitionSeconds = 8f };
        [SerializeField] private WeatherProfile rain =
            new WeatherProfile { state = WeatherState.Rain,
                fogColor = new Color(0.45f, 0.52f, 0.58f), ambientColor = new Color(0.45f, 0.5f, 0.6f),
                ambientIntensity = 0.75f, fogDensity = 0.012f, transitionSeconds = 6f };

        [SerializeField] private bool randomizePeriodically = true;
        [SerializeField] private float minStateSeconds = 90f;
        [SerializeField] private float maxStateSeconds = 300f;

        private WeatherProfile current;
        private float timer;
        private Color fromFogColor;
        private Color fromAmbient;
        private float fromAmbientIntensity;
        private float fromFogDensity;
        private float blend;

        public event Action<WeatherState> WeatherChanged;

        public WeatherState Current => current != null ? current.state : WeatherState.Clear;

        public void SetState(WeatherState state)
        {
            WeatherProfile target = Find(state);
            if (target == null)
            {
                Debug.LogWarning($"[WorldBuilder] No weather profile for {state}.");
                return;
            }
            SwitchTo(target);
        }

        private void OnEnable()
        {
            if (clear != null) ApplyImmediate(clear);
            timer = NextDuration();
        }

        private void Update()
        {
            if (current == null) return;

            blend += Time.deltaTime / Mathf.Max(0.01f, current.transitionSeconds);
            RenderSettings.fogColor = Color.Lerp(fromFogColor, current.fogColor, blend);
            RenderSettings.ambientLight = Color.Lerp(fromAmbient, current.ambientColor, blend);
            RenderSettings.ambientIntensity = Mathf.Lerp(fromAmbientIntensity, current.ambientIntensity, blend);
            if (RenderSettings.fog || current.fogDensity > 0f)
                RenderSettings.fogDensity = Mathf.Lerp(fromFogDensity, current.fogDensity, blend);

            if (!randomizePeriodically) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;

            WeatherProfile next = PickDifferent();
            SwitchTo(next);
            timer = NextDuration();
        }

        private WeatherProfile Find(WeatherState state)
        {
            if (clear != null && clear.state == state) return clear;
            if (overcast != null && overcast.state == state) return overcast;
            if (fog != null && fog.state == state) return fog;
            if (rain != null && rain.state == state) return rain;
            return null;
        }

        private WeatherProfile PickDifferent()
        {
            WeatherProfile[] candidates = { clear, overcast, fog, rain };
            WeatherProfile pick = current;
            for (int attempt = 0; attempt < 4 && pick == current; attempt++)
                pick = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            return pick ?? current;
        }

        private float NextDuration()
        {
            return UnityEngine.Random.Range(Mathf.Max(5f, minStateSeconds), Mathf.Max(minStateSeconds + 1f, maxStateSeconds));
        }

        private void SwitchTo(WeatherProfile target)
        {
            CaptureFromRenderSettings();
            current = target;
            blend = 0f;
            WeatherChanged?.Invoke(current.state);
        }

        private void ApplyImmediate(WeatherProfile profile)
        {
            current = profile;
            blend = 1f;
            fromFogColor = profile.fogColor;
            fromAmbient = profile.ambientColor;
            fromAmbientIntensity = profile.ambientIntensity;
            fromFogDensity = profile.fogDensity;
            RenderSettings.fogColor = profile.fogColor;
            RenderSettings.ambientLight = profile.ambientColor;
            RenderSettings.ambientIntensity = profile.ambientIntensity;
            RenderSettings.fogDensity = profile.fogDensity;
        }

        private void CaptureFromRenderSettings()
        {
            fromFogColor = RenderSettings.fogColor;
            fromAmbient = RenderSettings.ambientLight;
            fromAmbientIntensity = RenderSettings.ambientIntensity;
            fromFogDensity = RenderSettings.fogDensity;
        }
    }
}
