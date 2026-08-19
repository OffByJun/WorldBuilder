using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class TemperatureZoneEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private float radius;
        [SerializeField] private float temperature;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public float Radius => radius;
        public float Temperature => temperature;

        public TemperatureZoneEntry(Vector3 position, float radius, float temperature, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Temperature Zone";
            this.position = position;
            this.radius = radius;
            this.temperature = temperature;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
