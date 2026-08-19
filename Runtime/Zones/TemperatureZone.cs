using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class TemperatureZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float temperature = 20f;

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public float Temperature
        {
            get => temperature;
            set => temperature = value;
        }
    }
}
