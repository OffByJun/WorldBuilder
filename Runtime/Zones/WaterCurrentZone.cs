using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class WaterCurrentZone : MonoBehaviour
    {
        [SerializeField] private Vector3 direction = Vector3.forward;
        [SerializeField] private float strength = 1f;

        public Vector3 Direction
        {
            get => direction;
            set => direction = value;
        }

        public float Strength
        {
            get => strength;
            set => strength = value;
        }
    }
}
