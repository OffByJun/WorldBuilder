using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class ToxicZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float intensity = 0.5f;
        [SerializeField] private float damagePerSecond = 5f;

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

        public float DamagePerSecond
        {
            get => damagePerSecond;
            set => damagePerSecond = value;
        }
    }
}
