using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureAppearancePropagationSystem : ISystem
    {
        private struct AppearanceAssignment
        {
            public Entity Target;
            public CreatureAppearance Appearance;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureAppearanceDirty>();
            state.RequireForUpdate<CreatureAppearanceTarget>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;
            NativeList<AppearanceAssignment> pending = new NativeList<AppearanceAssignment>(Allocator.Temp);

            foreach (var (appearance, targets, dirty, source) in
                     SystemAPI.Query<RefRO<CreatureAppearance>, DynamicBuffer<CreatureAppearanceTarget>,
                         EnabledRefRW<CreatureAppearanceDirty>>().WithEntityAccess())
            {
                CreatureAppearance value = appearance.ValueRO;
                for (int i = 0; i < targets.Length; i++)
                {
                    Entity target = targets[i].Value;
                    if (target == Entity.Null || target == source || !entityManager.Exists(target) ||
                        !entityManager.HasComponent<CreatureAppearance>(target)) continue;
                    pending.Add(new AppearanceAssignment { Target = target, Appearance = value });
                }

                if (!entityManager.HasComponent<CreaturePrimaryColor>(source)) dirty.ValueRW = false;
            }

            for (int i = 0; i < pending.Length; i++)
            {
                entityManager.SetComponentData(pending[i].Target, pending[i].Appearance);
                entityManager.SetComponentEnabled<CreatureAppearanceDirty>(pending[i].Target, true);
            }

            pending.Dispose();
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureAppearancePropagationSystem))]
    public partial struct CreatureAppearanceSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureAppearanceDirty>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ApplyAppearanceJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct ApplyAppearanceJob : IJobEntity
        {
            private void Execute(in CreatureAppearance appearance, ref CreaturePrimaryColor primary,
                ref CreatureSecondaryColor secondary, ref CreatureAccentColor accent,
                ref CreaturePatternColor patternColor, ref CreaturePatternParams patternParams,
                EnabledRefRW<CreatureAppearanceDirty> dirty)
            {
                primary.Value = appearance.Primary;
                secondary.Value = appearance.Secondary;
                accent.Value = appearance.Accent;
                patternColor.Value = appearance.PatternColor;
                patternParams.Value = CreatureAppearanceRules.PatternParameters(appearance);
                dirty.ValueRW = false;
            }
        }
    }
}
