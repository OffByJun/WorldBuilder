using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class EventTriggerZoneEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private string eventId;
        [SerializeField] private float radius;
        [SerializeField] private bool oneShot;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public string EventId => eventId;
        public float Radius => radius;
        public bool OneShot => oneShot;

        public EventTriggerZoneEntry(Vector3 position, string eventId, float radius, bool oneShot, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = string.IsNullOrEmpty(eventId) ? "Event Trigger" : eventId;
            this.position = position;
            this.eventId = eventId;
            this.radius = radius;
            this.oneShot = oneShot;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
