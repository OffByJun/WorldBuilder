using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class VisibilityZoneEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private float radius;
        [SerializeField] private float visibility;
        [SerializeField] private Color fogColor;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public float Radius => radius;
        public float Visibility => visibility;
        public Color FogColor => fogColor;

        public VisibilityZoneEntry(Vector3 position, float radius, float visibility, Color fogColor, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Visibility Zone";
            this.position = position;
            this.radius = radius;
            this.visibility = visibility;
            this.fogColor = fogColor;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
