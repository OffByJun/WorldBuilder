using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class ToxicZoneEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private float radius;
        [SerializeField] private float intensity;
        [SerializeField] private float damagePerSecond;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public float Radius => radius;
        public float Intensity => intensity;
        public float DamagePerSecond => damagePerSecond;

        public ToxicZoneEntry(Vector3 position, float radius, float intensity, float damagePerSecond, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Toxic Zone";
            this.position = position;
            this.radius = radius;
            this.intensity = intensity;
            this.damagePerSecond = damagePerSecond;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
