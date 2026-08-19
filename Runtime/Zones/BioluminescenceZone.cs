using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class BioluminescenceZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float intensity = 0.5f;
        [SerializeField] private Color color = Color.cyan;

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public float Intensity
        {
            get => intensity;
            set => intensity = value;
        }

        public Color Color
        {
            get => color;
            set => color = value;
        }
    }
}
