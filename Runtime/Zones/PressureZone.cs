using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class PressureZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float pressure = 50f;
        [SerializeField] private float damagePerSecond = 5f;

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public float Pressure
        {
            get => pressure;
            set => pressure = value;
        }

        public float DamagePerSecond
        {
            get => damagePerSecond;
            set => damagePerSecond = value;
        }
    }
}
