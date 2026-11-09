using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

namespace WorldBuilder.Entities.Creatures.Systems
{
    /// <summary>
    /// Reacts to the player. Working creatures keep working; only idle ones respond,
    /// so a settled village does not stop every time the player swims past.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureWorkExecutionSystem))]
    [UpdateBefore(typeof(CreatureSwimSystem))]
    public partial struct CreaturePerceptionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreaturePlayerFocus>();
            state.RequireForUpdate<CreaturePerception>();
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            CreaturePlayerFocus focus = SystemAPI.GetSingleton<CreaturePlayerFocus>();
            float detection = SystemAPI.TryGetSingleton<CreaturePlayerSupport>(out var support) && support.DetectionMultiplier > 0f
                ? support.DetectionMultiplier : 1f;
            state.Dependency = new PerceiveJob
            {
                PlayerPosition = focus.Position,
                HasPlayer = focus.IsValid != 0,
                DetectionMultiplier = detection,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Pauses = SystemAPI.GetComponentLookup<CreatureSwimPause>(true),
                Config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct PerceiveJob : IJobEntity
        {
            public float3 PlayerPosition;
            public bool HasPlayer;
            public float DetectionMultiplier;
            public double ElapsedTime;
            public WorldEntityRuntimeConfig Config;
            [ReadOnly] public ComponentLookup<CreatureSwimPause> Pauses;

            private void Execute(Entity entity, in LocalTransform transform, ref CreaturePerception perception,
                in CreatureAlarm alarm, in CreatureTaming taming, in CreatureWorkState work,
                in CreatureRandom seed, in CreatureSwim swim, ref CreatureMoveOrder order,
                EnabledRefRW<CreatureMoveOrder> hasOrder, EnabledRefRO<CreatureCaptured> captured,
                EnabledRefRO<WorldEntityActive> active)
            {
                if (captured.ValueRO || !active.ValueRO)
                { perception.PlayerDetected = 0; perception.AlertUntil = 0; return; }
                if (taming.IsTamed == 0 && taming.BondUntil > ElapsedTime)
                {
                    perception.CurrentReaction = CreatureReaction.Ignore;
                    return;
                }
                if (Pauses.TryGetComponent(entity, out var pause) && pause.Until > ElapsedTime) return;
                bool alarmed = CreatureTamingRules.IsAlarmed(alarm, ElapsedTime);
                if (work.Phase != CreatureWorkPhase.Idle && !alarmed)
                {
                    perception.CurrentReaction = CreatureReaction.Ignore;
                    return;
                }

                if (!HasPlayer)
                {
                    perception.PlayerDetected = 0;
                    perception.AlertUntil = 0;
                    if (perception.CurrentReaction != CreatureReaction.Ignore) hasOrder.ValueRW = false;
                    perception.CurrentReaction = CreatureReaction.Ignore;
                    return;
                }

                float distance = math.distance(transform.Position, PlayerPosition);
                var settings = perception;
                if (taming.IsTamed == 0 && settings.WildReaction == CreatureReaction.Flee)
                { settings.DetectionRadius *= DetectionMultiplier; settings.PersonalSpace *= DetectionMultiplier; }
                float clearance = math.max(.5f, swim.BodyRadius * math.abs(transform.Scale)) + 1.5f;
                settings.ApproachDistance = math.max(settings.ApproachDistance, clearance);
                settings.PersonalSpace = math.max(settings.PersonalSpace, clearance);
                bool detected = settings.DetectionRadius > 0f && distance <= settings.DetectionRadius + (perception.PlayerDetected != 0 ? 2f : 0f);
                bool discovered = detected && perception.PlayerDetected == 0;
                if (discovered) { perception.AlertVersion++; perception.AlertUntil = ElapsedTime + 2.2f; }
                perception.PlayerDetected = detected ? (byte)1 : (byte)0;
                if (!detected) perception.AlertUntil = 0;
                if (alarmed)
                {
                    if (perception.CurrentReaction != CreatureReaction.Flee && !discovered && detected)
                    { perception.AlertVersion++; perception.AlertUntil = ElapsedTime + 2.2f; }
                    perception.CurrentReaction = CreatureReaction.Flee;
                    return;
                }
                CreatureReaction reaction =
                    CreaturePerceptionRules.Resolve(settings, taming.IsTamed != 0, distance);
                if (reaction == CreatureReaction.Flee && perception.CurrentReaction != CreatureReaction.Flee && !discovered)
                { perception.AlertVersion++; perception.AlertUntil = ElapsedTime + 2.2f; }
                if (reaction == CreatureReaction.Ignore)
                {
                    if (perception.CurrentReaction != CreatureReaction.Ignore) hasOrder.ValueRW = false;
                    perception.CurrentReaction = CreatureReaction.Ignore;
                    return;
                }

                float3 target = CreaturePerceptionRules.ReactionTarget(transform.Position, PlayerPosition,
                    reaction, settings, seed.State);
                order.Target = CreatureSwimMath.ConstrainToHomeRegion(target, swim, Config);
                order.SpeedMultiplier = CreaturePerceptionRules.SpeedMultiplier(perception, reaction);
                order.ArriveRadius = .6f;
                perception.CurrentReaction = reaction;
                hasOrder.ValueRW = true;
            }
        }
    }
}
