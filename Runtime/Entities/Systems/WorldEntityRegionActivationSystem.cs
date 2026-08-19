using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(WorldEntitySpawnSystem))]
    public partial struct WorldEntityRegionActivationSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntityLoadedRegion>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<WorldEntityLoadedRegion> regions =
                state.EntityManager.GetBuffer<WorldEntityLoadedRegion>(runtime, true);
            NativeParallelHashSet<int2> loaded =
                new NativeParallelHashSet<int2>(math.max(1, regions.Length), Allocator.TempJob);
            for (int i = 0; i < regions.Length; i++) loaded.Add(regions[i].Coordinate);

            state.Dependency = new ApplyRegionActivationJob { Loaded = loaded }
                .ScheduleParallel(state.Dependency);
            state.Dependency = loaded.Dispose(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct ApplyRegionActivationJob : IJobEntity
        {
            [ReadOnly] public NativeParallelHashSet<int2> Loaded;

            private void Execute(in WorldEntityChunk ownership, in WorldEntityDescriptor descriptor,
                EnabledRefRW<WorldEntityActive> active)
            {
                bool streamed = (descriptor.Flags & WorldEntityFlags.RegionStreamed) != 0;
                active.ValueRW = !streamed || Loaded.Contains(ownership.Region);
            }
        }
    }
}
