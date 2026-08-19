using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [CreateAssetMenu(fileName = "LightingPreset", menuName = "WorldBuilder/LightingPreset")]
    public sealed class LightingPreset : ScriptableObject
    {
        [SerializeField] private string label;
        [SerializeField] private Color ambientColor = Color.gray;
        [SerializeField] private float ambientIntensity = 1f;
        [SerializeField] private Color fogColor = Color.gray;
        [SerializeField] private bool fogEnabled;
        [SerializeField] private float fogDensity = 0.01f;
        [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;

        public string Label
        {
            get => label;
            set => label = value;
        }

        public Color AmbientColor
        {
            get => ambientColor;
            set => ambientColor = value;
        }

        public float AmbientIntensity
        {
            get => ambientIntensity;
            set => ambientIntensity = value;
        }

        public Color FogColor
        {
            get => fogColor;
            set => fogColor = value;
        }

        public bool FogEnabled
        {
            get => fogEnabled;
            set => fogEnabled = value;
        }

        public float FogDensity
        {
            get => fogDensity;
            set => fogDensity = value;
        }

        public FogMode FogMode
        {
            get => fogMode;
            set => fogMode = value;
        }
    }
}
