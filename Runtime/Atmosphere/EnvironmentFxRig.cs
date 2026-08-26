using System;
using UnityEngine;
using UnityEngine.Audio;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Atmosphere
{
    /// <summary>
    /// Drives RenderSettings atmosphere from the environment domain under a probe point:
    /// underwater tint, cave darkness, flooded-cave murk and open air each get their own
    /// look. Values ease toward targets so domain switches feel natural. RP-agnostic (no
    /// Volume/URP coupling) — wire a URP Volume in game code if you need post processing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentFxRig : MonoBehaviour
    {
        [Serializable]
        public sealed class DomainLook
        {
            public EnvironmentDomain domain = EnvironmentDomain.OpenAir;
            public Color fogColor = new Color(0.65f, 0.75f, 0.85f);
            [Min(0f)] public float fogDensity;
            public float ambientIntensity = 1f;
            public Color ambientColor = new Color(0.6f, 0.7f, 0.9f);
            public bool overrideAmbientColor;

            [Tooltip("Optional: mixer snapshot switched to while this domain is active (underwater muffle, cave reverb…).")]
            public AudioMixerSnapshot mixerSnapshot;

            [Tooltip("Optional looping ambience clip crossfaded in for this domain.")]
            public AudioClip ambientClip;
            [Range(0f, 1f)] public float ambientVolume = 0.6f;

#if WB_CORE_RP
            [Tooltip("Optional: URP Volume profile swapped in while this domain is active.")]
            public UnityEngine.Rendering.VolumeProfile volumeProfile;
#endif
        }

        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private VoxelStoreAsset voxelStore;
        [SerializeField] private float chunkSize = 128f;
        [SerializeField] private Transform probe;
        [SerializeField] private float responseSpeed = 1.5f;
        [Tooltip("AudioMixerSnapshot crossfade duration when the domain changes.")]
        [Min(0f)] [SerializeField] private float snapshotTransitionSeconds = 0.4f;

#if WB_CORE_RP
        [Tooltip("Optional URP Volume whose profile is swapped per domain.")]
        [SerializeField] private UnityEngine.Rendering.Volume targetVolume;
#endif

        [SerializeField] private DomainLook[] looks =
        {
            new DomainLook { domain = EnvironmentDomain.OpenAir },
            new DomainLook { domain = EnvironmentDomain.Underwater,
                fogColor = new Color(0.05f, 0.22f, 0.35f), fogDensity = 0.06f, ambientIntensity = 0.55f },
            new DomainLook { domain = EnvironmentDomain.Underground,
                fogColor = new Color(0.06f, 0.055f, 0.08f), fogDensity = 0.04f, ambientIntensity = 0.25f,
                ambientColor = new Color(0.45f, 0.42f, 0.55f), overrideAmbientColor = true },
            new DomainLook { domain = EnvironmentDomain.FloodedCave,
                fogColor = new Color(0.03f, 0.12f, 0.14f), fogDensity = 0.09f, ambientIntensity = 0.2f,
                ambientColor = new Color(0.3f, 0.4f, 0.45f), overrideAmbientColor = true }
        };

        private WaterQueryService waterService;
        private VoxelWorldSampler sampler;
        private EnvironmentDomain current = EnvironmentDomain.OpenAir;
        private AudioSource ambientA;
        private AudioSource ambientB;
        private bool ambientToggle;

        public IWaterQueryService QueryServiceOverride { get; set; }
        public VoxelWorldSampler SamplerOverride { get; set; }
        public event Action<EnvironmentDomain> DomainChanged;

        public EnvironmentDomain CurrentDomain => current;

        private void OnEnable()
        {
            if (waterData != null) waterService = new WaterQueryService(waterData);
            if (voxelStore != null) sampler = new VoxelWorldSampler(voxelStore, Mathf.Max(1f, chunkSize));
            ambientA = CreateAmbientSource("A");
            ambientB = CreateAmbientSource("B");
        }

        private void OnDisable()
        {
            if (ambientA != null) Destroy(ambientA.gameObject);
            if (ambientB != null) Destroy(ambientB.gameObject);
            ambientA = ambientB = null;
        }

        private AudioSource CreateAmbientSource(string suffix)
        {
            var go = new GameObject($"WB_Ambience_{suffix}");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        private void Update()
        {
            Vector3 position = (probe != null ? probe : transform).position;
            IWaterQueryService water = QueryServiceOverride ?? waterService;
            VoxelWorldSampler voxels = SamplerOverride ?? sampler;
            if (water == null && voxels == null) return;

            EnvironmentDomain domain = EnvironmentClassifier.Classify(water, voxels, position);
            if (domain != current)
            {
                current = domain;
                DomainChanged?.Invoke(domain);
                TransitionAudio(FindLook(current));
#if WB_CORE_RP
                SwapVolumeProfile(FindLook(current));
#endif
            }

            CrossfadeAmbient(Time.deltaTime);
            ApplyAtmosphere(FindLook(current));
        }

        private void ApplyAtmosphere(DomainLook target)
        {
            if (target == null) return;
            float step = Mathf.Max(0.1f, responseSpeed) * Time.deltaTime;

            UnityEngine.RenderSettings.fogColor = Color.Lerp(UnityEngine.RenderSettings.fogColor,
                target.fogColor, step);
            if (UnityEngine.RenderSettings.fog)
                UnityEngine.RenderSettings.fogDensity = Mathf.Lerp(
                    UnityEngine.RenderSettings.fogDensity, target.fogDensity, step);
            UnityEngine.RenderSettings.ambientLight = Color.Lerp(UnityEngine.RenderSettings.ambientLight,
                target.overrideAmbientColor ? target.ambientColor : target.fogColor * 1.2f, step);

            Light sun = UnityEngine.RenderSettings.sun;
            if (sun != null && Application.isPlaying)
                sun.intensity = Mathf.Lerp(sun.intensity, target.ambientIntensity, step);
        }

        private void CrossfadeAmbient(float deltaTime)
        {
            DomainLook look = FindLook(current);
            AudioClip targetClip = look?.ambientClip;
            float targetVolume = look != null ? look.ambientVolume : 0f;

            AudioSource active = ambientToggle ? ambientA : ambientB;
            AudioSource idle = ambientToggle ? ambientB : ambientA;

            if (targetClip != null && (active.clip == null || active.clip != targetClip))
            {
                // Swap: fade out the old, start the new on the other source.
                ambientToggle = !ambientToggle;
                AudioSource previous = active;
                active = idle;
                idle = previous;
                active.clip = targetClip;
                active.volume = 0f;
                active.Play();
            }
            else if (targetClip == null && active != null && active.isPlaying)
            {
                active.Stop();
            }

            float step = Mathf.Max(0.05f, deltaTime);
            if (active != null)
                active.volume = Mathf.MoveTowards(active.volume, targetVolume, step * 0.5f);
            if (idle != null && idle.isPlaying)
                idle.volume = Mathf.MoveTowards(idle.volume, 0f, step * 0.5f);
        }

        private DomainLook FindLook(EnvironmentDomain domain)
        {
            foreach (DomainLook look in looks)
                if (look != null && look.domain == domain) return look;
            return null;
        }

        private void TransitionAudio(DomainLook target)
        {
            if (target?.mixerSnapshot == null) return;
            AudioMixerSnapshot snapshot = target.mixerSnapshot;
            if (snapshot.audioMixer == null) return;
            snapshot.TransitionTo(Mathf.Max(0f, snapshotTransitionSeconds));
        }

#if WB_CORE_RP
        private void SwapVolumeProfile(DomainLook target)
        {
            if (targetVolume == null) return;
            UnityEngine.Rendering.VolumeProfile profile = target?.volumeProfile;
            targetVolume.sharedProfile = profile;
            targetVolume.enabled = profile != null;
        }
#endif
    }
}
