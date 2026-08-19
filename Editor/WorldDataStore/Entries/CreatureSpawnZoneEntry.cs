using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class CreatureSpawnZoneEntry : IWorldDataEntry, IBiomeAware
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string sceneObjectGlobalId;
        [SerializeField] private BiomeType biome;
        [SerializeField] private int prefabId;
        [SerializeField] private float density;
        [SerializeField] private float radius;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public string SceneObjectGlobalId => sceneObjectGlobalId;
        public BiomeType Biome => biome;
        public int PrefabId => prefabId;
        public float Density => density;
        public float Radius => radius;

        public CreatureSpawnZoneEntry(Vector3 position, BiomeType biome, int prefabId, float density, float radius, string sceneObjectGlobalId = "")
        {
            id = Guid.NewGuid().ToString();
            displayName = "Creature Spawn [" + biome + "]";
            this.position = position;
            this.biome = biome;
            this.prefabId = prefabId;
            this.density = density;
            this.radius = radius;
            this.sceneObjectGlobalId = sceneObjectGlobalId;
        }
    }
}
