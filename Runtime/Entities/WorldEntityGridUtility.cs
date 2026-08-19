using Unity.Mathematics;

namespace WorldBuilder.Entities
{
    public static class WorldEntityGridUtility
    {
        public static int2 WorldToChunk(float3 position, in WorldEntityRuntimeConfig config)
        {
            float2 local = new float2(position.x - config.WorldOrigin.x, position.z - config.WorldOrigin.z);
            return (int2)math.floor(local / config.ChunkSize);
        }

        public static int2 ChunkToRegion(int2 chunk, int chunksPerRegion)
        {
            return new int2(FloorDiv(chunk.x, chunksPerRegion), FloorDiv(chunk.y, chunksPerRegion));
        }

        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int2 WorldToRegion(float3 position, in WorldEntityRuntimeConfig config)
            => ChunkToRegion(WorldToChunk(position, config), config.ChunksPerRegion);

        public static float RegionSize(in WorldEntityRuntimeConfig config)
            => config.ChunkSize * config.ChunksPerRegion;

        public static float2 RegionMinimum(int2 region, in WorldEntityRuntimeConfig config)
        {
            float size = RegionSize(config);
            return new float2(config.WorldOrigin.x + region.x * size, config.WorldOrigin.z + region.y * size);
        }

        public static float3 ClampToRegion(float3 position, int2 region, in WorldEntityRuntimeConfig config,
            float margin)
        {
            float size = RegionSize(config);
            float inset = math.min(math.max(0f, margin), size * 0.5f);
            float2 minimum = RegionMinimum(region, config) + inset;
            float2 maximum = RegionMinimum(region, config) + (size - inset);
            float2 clamped = math.clamp(position.xz, minimum, math.max(minimum, maximum));
            return new float3(clamped.x, position.y, clamped.y);
        }
    }
}
