using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Resources.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ResourceRespawnSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency = new RespawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Commands = commands.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities)]
        private partial struct RespawnJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter Commands;

            private void Execute(Entity entity, [EntityIndexInQuery] int sortKey, ref ResourceNode node,
                ref ResourceRespawnState respawn, in Disabled disabled)
            {
                respawn.RemainingSeconds -= DeltaTime;
                if (respawn.RemainingSeconds > 0f) return;
                respawn.RemainingSeconds = 0f;
                node.Health = node.MaxHealth;
                node.NextHitTime = 0d;
                Commands.RemoveComponent<Disabled>(sortKey, entity);
            }
        }
    }
}
