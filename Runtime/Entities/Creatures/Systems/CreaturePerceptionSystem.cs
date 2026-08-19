using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
            CreaturePlayerFocus focus = SystemAPI.GetSingleton<CreaturePlayerFocus>();
            state.Dependency = new PerceiveJob
            {
                PlayerPosition = focus.Position,
                HasPlayer = focus.IsValid != 0,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithDisabled(typeof(CreatureCaptured))]
        private partial struct PerceiveJob : IJobEntity
        {
            public float3 PlayerPosition;
            public bool HasPlayer;
            public double ElapsedTime;
            public WorldEntityRuntimeConfig Config;

            private void Execute(in LocalTransform transform, in CreaturePerception perception,
                in CreatureAlarm alarm, in CreatureTaming taming, in CreatureWorkState work,
                in CreatureRandom seed, in CreatureSwim swim, ref CreatureMoveOrder order,
                EnabledRefRW<CreatureMoveOrder> hasOrder, in WorldEntityActive active)
            {
                if (work.Phase != CreatureWorkPhase.Idle) return;
                if (CreatureTamingRules.IsAlarmed(alarm, ElapsedTime)) return;

                if (!HasPlayer)
                {
                    if (hasOrder.ValueRO) hasOrder.ValueRW = false;
                    return;
                }

                float distance = math.distance(transform.Position, PlayerPosition);
                CreatureReaction reaction =
                    CreaturePerceptionRules.Resolve(perception, taming.IsTamed != 0, distance);
                if (reaction == CreatureReaction.Ignore)
                {
                    if (hasOrder.ValueRO) hasOrder.ValueRW = false;
                    return;
                }

                float3 target = CreaturePerceptionRules.ReactionTarget(transform.Position, PlayerPosition,
                    reaction, perception, seed.State);
                order.Target = CreatureSwimMath.ConstrainToHomeRegion(target, swim, Config);
                order.SpeedMultiplier = CreaturePerceptionRules.SpeedMultiplier(perception, reaction);
                order.ArriveRadius = math.max(0.5f, perception.ApproachDistance * 0.5f);
                hasOrder.ValueRW = true;
            }
        }
    }
}
