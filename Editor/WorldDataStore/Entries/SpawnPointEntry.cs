using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class SpawnPointEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private int prefabId;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public int PrefabId => prefabId;

        public SpawnPointEntry(Vector3 position, int prefabId, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Spawn Point";
            this.position = position;
            this.prefabId = prefabId;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
