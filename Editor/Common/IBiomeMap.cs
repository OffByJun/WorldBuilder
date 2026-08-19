using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public interface IBiomeMap
    {
        BiomeType GetBiome(Vector3Int chunkCoord);
        void SetBiome(Vector3Int chunkCoord, BiomeType biome);
    }
}
