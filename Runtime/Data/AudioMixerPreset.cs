using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace WorldBuilder.Runtime.Data
{
    [Serializable]
    public struct AudioMixerParamEntry
    {
        public string paramName;
        public float value;
    }

    [CreateAssetMenu(fileName = "AudioMixerPreset", menuName = "WorldBuilder/AudioMixerPreset")]
    public sealed class AudioMixerPreset : ScriptableObject
    {
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private List<AudioMixerParamEntry> parameters = new List<AudioMixerParamEntry>();

        public AudioMixer Mixer
        {
            get => mixer;
            set => mixer = value;
        }

        public List<AudioMixerParamEntry> Parameters => parameters;
    }
}
