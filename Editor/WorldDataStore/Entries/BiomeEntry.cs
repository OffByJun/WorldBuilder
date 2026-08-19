using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.ZoneEntries
{
    [Serializable]
    public sealed class BiomeEntry : IWorldDataEntry, IBiomeAware
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 position;
        [SerializeField] private bool enabled = true;
        [SerializeField] private BiomeType biome;
        [SerializeField] private Vector3Int chunkCoord;
        [SerializeField] private float chunkSize;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector3 Position => position;
        public bool Enabled { get => enabled; set => enabled = value; }
        public BiomeType Biome => biome;
        public Vector3Int ChunkCoord => chunkCoord;
        public float ChunkSize => chunkSize;

        public BiomeEntry(Vector3 worldCenter, BiomeType biome, Vector3Int chunkCoord, float chunkSize)
        {
            id = Guid.NewGuid().ToString();
            displayName = "Biome: " + biome;
            position = worldCenter;
            this.biome = biome;
            this.chunkCoord = chunkCoord;
            this.chunkSize = chunkSize;
        }
    }
}
