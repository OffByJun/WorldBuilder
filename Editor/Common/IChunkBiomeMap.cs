using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public interface IChunkBiomeMap
    {
        void Set(Vector3Int coord, BiomeType biome);
        void Remove(Vector3Int coord);
        bool TryGet(Vector3Int coord, out BiomeType biome);
        IReadOnlyDictionary<Vector3Int, BiomeType> Entries { get; }
    }
}
