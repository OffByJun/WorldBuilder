using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [Serializable]
    public struct ChunkData
    {
        public Vector3 position;
        public BiomeType biome;
        public VoxelData voxels;
        public SpawnData[] spawns;
    }
}
