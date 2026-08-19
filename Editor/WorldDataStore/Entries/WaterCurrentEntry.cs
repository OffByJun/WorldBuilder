using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class WaterCurrentEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private Vector3 direction;
        [SerializeField] private float strength;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public Vector3 Direction => direction;
        public float Strength => strength;

        public WaterCurrentEntry(Vector3 position, Vector3 direction, float strength, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Water Current";
            this.position = position;
            this.direction = direction;
            this.strength = strength;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
