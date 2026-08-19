using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public sealed class ChunkBiomeMapAdapter : IBiomeMap
    {
        private readonly IChunkBiomeMap inner;
        private readonly BiomeType fallback;

        public ChunkBiomeMapAdapter(IChunkBiomeMap inner, BiomeType fallback = BiomeType.Ocean)
        {
            this.inner = inner;
            this.fallback = fallback;
        }

        public BiomeType GetBiome(Vector3Int chunkCoord)
        {
            return inner != null && inner.TryGet(chunkCoord, out BiomeType biome) ? biome : fallback;
        }

        public void SetBiome(Vector3Int chunkCoord, BiomeType biome)
        {
            inner?.Set(chunkCoord, biome);
        }
    }
}
