using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorldEntityLifetimeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency = new TickLifetimeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Commands = commands.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        [BurstCompile]
        private partial struct TickLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter Commands;

            private void Execute(Entity entity, [EntityIndexInQuery] int sortKey,
                ref WorldEntityLifetime lifetime, in WorldEntityActive active)
            {
                lifetime.RemainingSeconds -= DeltaTime;
                if (lifetime.RemainingSeconds <= 0f) Commands.DestroyEntity(sortKey, entity);
            }
        }
    }
}
