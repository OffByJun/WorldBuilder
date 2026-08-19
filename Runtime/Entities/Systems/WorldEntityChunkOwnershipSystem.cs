using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldEntityVelocitySystem))]
    public partial struct WorldEntityChunkOwnershipSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldEntityRuntimeConfig config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>();
            state.Dependency = new UpdateOwnershipJob { Config = config }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct UpdateOwnershipJob : IJobEntity
        {
            public WorldEntityRuntimeConfig Config;

            private void Execute(in LocalTransform transform, ref WorldEntityChunk ownership,
                in WorldEntityTrackChunk tracking)
            {
                ownership.Chunk = WorldEntityGridUtility.WorldToChunk(transform.Position, Config);
                ownership.Region = WorldEntityGridUtility.ChunkToRegion(ownership.Chunk, Config.ChunksPerRegion);
            }
        }
    }
}
