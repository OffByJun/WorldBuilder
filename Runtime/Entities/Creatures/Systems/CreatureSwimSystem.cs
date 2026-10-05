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
            float surface = SystemAPI.TryGetSingleton(out CreatureWaterSurface water)
                ? water.Height
                : float.PositiveInfinity;
            state.Dependency = new SwimJob
            {
                WaterSurface = surface,
                DeltaTime = SystemAPI.Time.DeltaTime,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>(),
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                Creatures = SystemAPI.GetComponentLookup<CreatureAlarm>(true)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct SwimJob : IJobEntity
        {
            public float DeltaTime;
            public double ElapsedTime;
            public WorldEntityRuntimeConfig Config;
            public float WaterSurface;
            [ReadOnly] public CollisionWorld CollisionWorld;
            // Every swimmer carries an alarm, so this tells a creature's body apart from the seabed.
            [ReadOnly] public ComponentLookup<CreatureAlarm> Creatures;

            private void Execute(Entity entity, ref LocalTransform transform, ref CreatureSwim swim,
                ref CreatureRandom seed,
                in CreatureAlarm alarm, in CreatureMoveOrder order, EnabledRefRO<CreatureMoveOrder> hasOrder,
                EnabledRefRO<CreatureCaptured> captured, EnabledRefRO<WorldEntityActive> active)
            {
                if (captured.ValueRO || !active.ValueRO) return;

                // Swimmers live under the surface: nothing, not a flee, a follow order or a shove, takes one out of the water.
                float radius = CreatureTerrainCollision.Radius(transform.Scale, swim.BodyRadius);
                float ceiling = WaterSurface - radius - .05f;
                if (!CreatureTerrainCollision.TryPlace(CollisionWorld, Creatures, entity, transform.Position,
                        radius, WaterSurface, out float3 safeStart)) return;
                transform.Position = safeStart;

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
                    // Retry inaccessible/shallow destinations instead of keeping a point inside land.
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        float3 target = CreatureSwimMath.NextWanderTarget(swim.Home, swim.WanderRadius,
                            swim.VerticalRadius, ref random);
                        target = CreatureSwimMath.ConstrainToHomeRegion(target, swim, Config);
                        if (!CreatureTerrainCollision.TryPlace(CollisionWorld, Creatures, entity, target,
                                radius, WaterSurface, out float3 safeTarget)) continue;
                        swim.TargetPoint = safeTarget;
                        break;
                    }
                    swim.NextRepathTime = ElapsedTime + swim.RepathIntervalSeconds;
                    seed.State = CreatureGradeRules.SanitizeSeed(random.NextUInt());
                }

                // Aim at the water too, so a target above the surface does not keep a creature pressed against it.
                if (CreatureTerrainCollision.TryPlace(CollisionWorld, Creatures, entity, swim.TargetPoint,
                        radius, WaterSurface, out float3 destination))
                    swim.TargetPoint = destination;
                else
                    swim.TargetPoint = transform.Position;

                CreatureSwimStep step = CreatureSwimMath.Advance(transform.Position, transform.Rotation,
                    swim.TargetPoint, speed, swim.TurnSpeedRadians, DeltaTime);
                float3 next = step.Position;
                if (!alarmed && !hasOrder.ValueRO)
                    next = CreatureSwimMath.ConstrainToHomeRegion(next, swim, Config);
                next.y = math.min(next.y, ceiling);
                next = CreatureTerrainCollision.Move(CollisionWorld, Creatures, entity, transform.Position,
                    next, radius, WaterSurface);
                transform.Position = next;
                transform.Rotation = step.Rotation;
            }

        }
    }
}
