using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class POIEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }

        public POIEntry(Vector3 position, string displayName = "Point of Interest")
        {
            id = Guid.NewGuid().ToString();
            this.displayName = displayName;
            this.position = position;
        }
    }
}
