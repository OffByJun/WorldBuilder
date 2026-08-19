using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class EventTriggerZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private string eventId = string.Empty;
        [SerializeField] private bool oneShot = true;

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public string EventId
        {
            get => eventId;
            set => eventId = value;
        }

        public bool OneShot
        {
            get => oneShot;
            set => oneShot = value;
        }
    }
}
