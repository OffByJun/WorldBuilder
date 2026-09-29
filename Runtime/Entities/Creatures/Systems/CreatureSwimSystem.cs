using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
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
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new SwimJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>(),
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct SwimJob : IJobEntity
        {
            public float DeltaTime;
            public double ElapsedTime;
            public WorldEntityRuntimeConfig Config;
            [ReadOnly] public CollisionWorld CollisionWorld;

            private void Execute(Entity entity, ref LocalTransform transform, ref CreatureSwim swim,
                ref CreatureRandom seed,
                in CreatureAlarm alarm, in CreatureMoveOrder order, EnabledRefRO<CreatureMoveOrder> hasOrder,
                EnabledRefRO<CreatureCaptured> captured, EnabledRefRO<WorldEntityActive> active)
            {
                if (captured.ValueRO || !active.ValueRO) return;

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
                    if (CreatureSwimMath.HasArrived(transform.Position, order.Target,
                            math.max(0f, order.ArriveRadius)))
                        return;
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
                next = KeepAboveGround(entity, next, transform.Scale);
                transform.Position = next;
                transform.Rotation = step.Rotation;
            }

            private float3 KeepAboveGround(Entity entity, float3 position, float scale)
            {
                const float probeHeight = 2f;
                const float probeDepth = 6f;
                RaycastInput input = new RaycastInput
                {
                    Start = position + math.up() * probeHeight,
                    End = position - math.up() * probeDepth,
                    Filter = CollisionFilter.Default
                };
                GroundHitCollector collector = new GroundHitCollector(entity);
                if (!CollisionWorld.CastRay(input, ref collector)) return position;

                float clearance = math.max(.35f, math.abs(scale) * .5f);
                position.y = math.max(position.y, collector.Hit.Position.y + clearance);
                return position;
            }
        }

        private struct GroundHitCollector : ICollector<Unity.Physics.RaycastHit>
        {
            private readonly Entity ignoredEntity;

            public GroundHitCollector(Entity ignoredEntity)
            {
                this.ignoredEntity = ignoredEntity;
                Hit = default;
                MaxFraction = 1f;
                NumHits = 0;
            }

            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction { get; private set; }
            public int NumHits { get; private set; }
            public Unity.Physics.RaycastHit Hit { get; private set; }

            public bool AddHit(Unity.Physics.RaycastHit hit)
            {
                if (hit.Entity == ignoredEntity || hit.SurfaceNormal.y < .35f) return false;

                Hit = hit;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
        }
    }
}
