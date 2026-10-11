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
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
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
                Creatures = SystemAPI.GetComponentLookup<CreatureAlarm>(true),
                Immobilized = SystemAPI.GetComponentLookup<CreatureImmobilized>(true)
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
            [ReadOnly] public ComponentLookup<CreatureImmobilized> Immobilized;

            private void Execute(Entity entity, ref LocalTransform transform, ref CreatureSwim swim,
                ref CreatureRandom seed,
                in CreatureAlarm alarm, in CreatureMoveOrder order, EnabledRefRO<CreatureMoveOrder> hasOrder,
                EnabledRefRO<CreatureCaptured> captured, EnabledRefRO<WorldEntityActive> active)
            {
                if (captured.ValueRO || !active.ValueRO || Immobilized.HasComponent(entity))
                {
                    swim.StuckSeconds = 0f;
                    return;
                }

                // Swimmers live under the surface: nothing, not a flee, a follow order or a shove, takes one out of the water.
                float radius = CreatureTerrainCollision.Radius(transform.Scale, swim.BodyRadius);
                float ceiling = WaterSurface - radius - .05f;
                bool alarmed = CreatureTamingRules.IsAlarmed(alarm, ElapsedTime);
                float3 originalTarget = alarmed
                    ? CreatureSwimMath.ConstrainToHomeRegion(CreatureTamingRules.FleeTarget(transform.Position, alarm), swim, Config)
                    : hasOrder.ValueRO ? order.Target : swim.TargetPoint;
                float arriveRadius = hasOrder.ValueRO && !alarmed ? math.max(0f, order.ArriveRadius) : math.max(.25f, swim.ArriveRadius);
                RecoverIfStuck(entity, ref transform, ref swim, originalTarget, arriveRadius, radius,
                    !alarmed && !hasOrder.ValueRO);

                if (!CreatureTerrainCollision.TryClear(CollisionWorld, Creatures, entity, transform.Position,
                        radius, WaterSurface, out float3 safeStart)) return;
                transform.Position = safeStart;

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
                if (CreatureTerrainCollision.TryClear(CollisionWorld, Creatures, entity, swim.TargetPoint,
                        radius, WaterSurface, out float3 destination))
                    swim.TargetPoint = destination;
                else
                    swim.TargetPoint = transform.Position;

                float3 navigationTarget = CreatureTerrainCollision.Navigate(CollisionWorld, Creatures, entity,
                    transform.Position, swim.TargetPoint, radius, speed, WaterSurface, ElapsedTime, ref swim);
                CreatureSwimStep step = CreatureSwimMath.Advance(transform.Position, transform.Rotation,
                    navigationTarget, speed, swim.TurnSpeedRadians, DeltaTime);
                float3 next = step.Position;
                if (!alarmed && !hasOrder.ValueRO)
                    next = CreatureSwimMath.ConstrainToHomeRegion(next, swim, Config);
                next.y = math.min(next.y, ceiling);
                next = CreatureTerrainCollision.Move(CollisionWorld, Creatures, entity, transform.Position,
                    next, radius, WaterSurface);
                float expected = math.distance(transform.Position, step.Position);
                if (expected > .001f && math.distance(transform.Position, next) < expected * .15f)
                    swim.BlockedSeconds += DeltaTime;
                else swim.BlockedSeconds = 0f;
                if (swim.BlockedSeconds > .75f)
                {
                    if (swim.BlockedSeconds - DeltaTime <= .75f) swim.NextAvoidanceTime = 0d;
                    swim.AvoidanceUntil = 0d;
                    if (!alarmed && !hasOrder.ValueRO && swim.BlockedSeconds > 2f)
                    {
                        swim.NextRepathTime = 0d;
                        swim.BlockedSeconds = 0f;
                    }
                }
                transform.Position = next;
                transform.Rotation = step.Rotation;
            }

            private void RecoverIfStuck(Entity entity, ref LocalTransform transform, ref CreatureSwim swim,
                float3 target, float arriveRadius, float radius, bool constrainToHome)
            {
                float progressDistance = math.max(.4f, radius * .5f);
                if (swim.CruiseSpeed <= 0f || CreatureSwimMath.HasArrived(transform.Position, target, arriveRadius) ||
                    math.distancesq(transform.Position, swim.ProgressPosition) >= progressDistance * progressDistance ||
                    !constrainToHome && math.distancesq(target, swim.ProgressTarget) >= math.max(1f, radius * radius))
                {
                    swim.StuckSeconds = 0f;
                    swim.ProgressPosition = transform.Position;
                    swim.ProgressTarget = target;
                    return;
                }
                swim.StuckSeconds += DeltaTime;
                if (swim.StuckSeconds < 10f) return;
                if (constrainToHome) target = CreatureSwimMath.ConstrainToHomeRegion(target, swim, Config);
                if (!CreatureTerrainCollision.TryRecover(CollisionWorld, Creatures, entity, target, radius,
                        WaterSurface, out float3 destination) ||
                    constrainToHome && math.distancesq(destination,
                        CreatureSwimMath.ConstrainToHomeRegion(destination, swim, Config)) > .001f)
                {
                    swim.StuckSeconds = 8f;
                    return;
                }
                transform.Position = destination;
                swim.ProgressPosition = destination;
                swim.ProgressTarget = target;
                swim.StuckSeconds = 0f;
                swim.BlockedSeconds = 0f;
                swim.AvoidanceUntil = 0d;
                swim.NextAvoidanceTime = 0d;
            }

        }
    }
}
