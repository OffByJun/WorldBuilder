using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class PressureZoneEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private float radius;
        [SerializeField] private float pressure;
        [SerializeField] private float damagePerSecond;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public float Radius => radius;
        public float Pressure => pressure;
        public float DamagePerSecond => damagePerSecond;

        public PressureZoneEntry(Vector3 position, float radius, float pressure, float damagePerSecond, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Pressure Zone";
            this.position = position;
            this.radius = radius;
            this.pressure = pressure;
            this.damagePerSecond = damagePerSecond;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
