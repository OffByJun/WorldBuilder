using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using WorldBuilder.Entities.Creatures;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorldEntityVelocitySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            state.Dependency = new IntegrateVelocityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Pauses = SystemAPI.GetComponentLookup<CreatureSwimPause>(true)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct IntegrateVelocityJob : IJobEntity
        {
            public float DeltaTime;
            public double ElapsedTime;
            [ReadOnly] public ComponentLookup<CreatureSwimPause> Pauses;

            private void Execute(Entity entity, ref LocalTransform transform, in WorldEntityVelocity velocity,
                in WorldEntityActive active)
            {
                if (Pauses.TryGetComponent(entity, out var pause) && pause.Until > ElapsedTime) return;
                transform.Position += velocity.Value * DeltaTime;
            }
        }
    }
}
