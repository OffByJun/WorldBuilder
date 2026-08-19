using System;
using UnityEngine;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class WreckageEntry : IWorldDataEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private string prefabGuid;
        [SerializeField] private int logNumber;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public string PrefabGuid => prefabGuid;
        public int LogNumber => logNumber;

        public WreckageEntry(Vector3 position, string prefabGuid, int logNumber, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = logNumber > 0 ? "LOG_" + logNumber.ToString("D3") : "Wreckage";
            this.position = position;
            this.prefabGuid = prefabGuid;
            this.logNumber = logNumber;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
