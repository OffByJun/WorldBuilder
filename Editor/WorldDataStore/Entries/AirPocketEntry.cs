using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class AirPocketEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private Vector3 size;
        [SerializeField] private string label;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public Vector3 Size => size;
        public string Label => label;

        public AirPocketEntry(Vector3 position, Vector3 size, string label, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = string.IsNullOrEmpty(label) ? "Air Pocket" : label;
            this.position = position;
            this.size = size;
            this.label = label;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
