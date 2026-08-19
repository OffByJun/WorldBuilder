using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public sealed class ChunkBiomeMap : IChunkBiomeMap
    {
        private readonly Dictionary<Vector3Int, BiomeType> map = new Dictionary<Vector3Int, BiomeType>();

        public IReadOnlyDictionary<Vector3Int, BiomeType> Entries => map;

        public void Set(Vector3Int coord, BiomeType biome)
        {
            map[coord] = biome;
        }

        public void Remove(Vector3Int coord)
        {
            map.Remove(coord);
        }

        public bool TryGet(Vector3Int coord, out BiomeType biome)
        {
            return map.TryGetValue(coord, out biome);
        }
    }
}
