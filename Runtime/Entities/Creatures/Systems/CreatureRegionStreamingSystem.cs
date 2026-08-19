using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using WorldBuilder.Entities.Systems;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldEntityChunkOwnershipSystem))]
    public partial struct CreatureRegionStreamingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntityLoadedRegion>();
            state.RequireForUpdate<CreatureStreaming>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<WorldEntityLoadedRegion> regions =
                state.EntityManager.GetBuffer<WorldEntityLoadedRegion>(runtime, true);
            if (regions.IsEmpty) return;

            NativeParallelHashSet<int2> loaded = new NativeParallelHashSet<int2>(regions.Length, Allocator.TempJob);
            for (int i = 0; i < regions.Length; i++) loaded.Add(regions[i].Coordinate);

            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency = new StreamCreaturesJob
            {
                Loaded = loaded,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Commands = commands.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();

            commands.Playback(state.EntityManager);
            commands.Dispose();
            loaded.Dispose();
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct StreamCreaturesJob : IJobEntity
        {
            [ReadOnly] public NativeParallelHashSet<int2> Loaded;
            public double ElapsedTime;
            public EntityCommandBuffer.ParallelWriter Commands;

            private void Execute(Entity entity, [EntityIndexInQuery] int sortKey,
                ref CreatureStreaming streaming, in WorldEntityChunk ownership,
                in WorldEntityDescriptor descriptor, in Creature creature)
            {
                if ((descriptor.Flags & WorldEntityFlags.Persistent) != 0) return;
                if (Loaded.Contains(ownership.Region))
                {
                    streaming.UnloadedSince = 0d;
                    return;
                }

                if (streaming.UnloadedSince <= 0d)
                {
                    streaming.UnloadedSince = ElapsedTime;
                    return;
                }

                if (ElapsedTime - streaming.UnloadedSince >= streaming.DespawnGraceSeconds)
                    Commands.DestroyEntity(sortKey, entity);
            }
        }
    }
}
