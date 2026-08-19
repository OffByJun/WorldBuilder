using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorldEntityVelocitySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new IntegrateVelocityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct IntegrateVelocityJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(ref LocalTransform transform, in WorldEntityVelocity velocity,
                in WorldEntityActive active)
            {
                transform.Position += velocity.Value * DeltaTime;
            }
        }
    }
}
