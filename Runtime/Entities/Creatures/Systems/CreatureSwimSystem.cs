using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using WorldBuilder.Entities.Systems;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(WorldEntityChunkOwnershipSystem))]
    public partial struct CreatureSwimSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new SwimJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithDisabled(typeof(CreatureCaptured))]
        private partial struct SwimJob : IJobEntity
        {
            public float DeltaTime;
            public double ElapsedTime;
            public WorldEntityRuntimeConfig Config;

            private void Execute(ref LocalTransform transform, ref CreatureSwim swim, ref CreatureRandom seed,
                in CreatureAlarm alarm, in CreatureMoveOrder order, EnabledRefRO<CreatureMoveOrder> hasOrder,
                in WorldEntityActive active)
            {
                bool alarmed = CreatureTamingRules.IsAlarmed(alarm, ElapsedTime);
                float speed = swim.CruiseSpeed;

                if (alarmed)
                {
                    swim.TargetPoint = CreatureSwimMath.ConstrainToHomeRegion(
                        CreatureTamingRules.FleeTarget(transform.Position, alarm), swim, Config);
                    swim.NextRepathTime = ElapsedTime;
                    speed *= math.max(1f, alarm.FleeSpeedMultiplier);
                }
                else if (hasOrder.ValueRO)
                {
                    swim.TargetPoint = order.Target;
                    swim.NextRepathTime = ElapsedTime;
                    speed *= math.max(0.1f, order.SpeedMultiplier);
                }
                else if (CreatureSwimMath.ShouldRepath(swim, transform.Position, ElapsedTime))
                {
                    Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(seed.State));
                    float3 target = CreatureSwimMath.NextWanderTarget(swim.Home, swim.WanderRadius,
                        swim.VerticalRadius, ref random);
                    swim.TargetPoint = CreatureSwimMath.ConstrainToHomeRegion(target, swim, Config);
                    swim.NextRepathTime = ElapsedTime + swim.RepathIntervalSeconds;
                    seed.State = CreatureGradeRules.SanitizeSeed(random.NextUInt());
                }

                CreatureSwimStep step = CreatureSwimMath.Advance(transform.Position, transform.Rotation,
                    swim.TargetPoint, speed, swim.TurnSpeedRadians, DeltaTime);
                float3 next = step.Position;
                if (!alarmed && !hasOrder.ValueRO)
                    next = CreatureSwimMath.ConstrainToHomeRegion(next, swim, Config);
                transform.Position = next;
                transform.Rotation = step.Rotation;
            }
        }
    }
}
