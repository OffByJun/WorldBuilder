using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(CreatureTamingSystem))]
    public partial struct CreatureRecolorSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureRecolorRequest>();
            state.RequireForUpdate<CreaturePaletteEntry>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureRecolorRequest>();
            DynamicBuffer<CreatureRecolorRequest> colorRequests =
                state.EntityManager.GetBuffer<CreatureRecolorRequest>(runtime);
            DynamicBuffer<CreaturePatternRequest> patternRequests =
                state.EntityManager.GetBuffer<CreaturePatternRequest>(runtime);
            if (colorRequests.IsEmpty && patternRequests.IsEmpty) return;

            DynamicBuffer<CreatureRecolorResult> results =
                state.EntityManager.GetBuffer<CreatureRecolorResult>(runtime);
            DynamicBuffer<CreaturePaletteEntry> palette =
                state.EntityManager.GetBuffer<CreaturePaletteEntry>(runtime, true);

            for (int i = 0; i < colorRequests.Length; i++)
            {
                CreatureRecolorRequest request = colorRequests[i];
                CreatureRecolorFailure failure = Validate(state.EntityManager, request.Target);
                if (failure != CreatureRecolorFailure.None)
                {
                    results.Add(Failure(request.RequestId, request.Target, failure));
                    continue;
                }

                if (!CreatureAppearanceRules.TryResolvePalette(palette, request.PaletteId, out float4 color))
                {
                    results.Add(Failure(request.RequestId, request.Target,
                        CreatureRecolorFailure.UnknownPalette));
                    continue;
                }

                CreatureAppearance appearance =
                    state.EntityManager.GetComponentData<CreatureAppearance>(request.Target);
                appearance = CreatureAppearanceRules.WithSlot(appearance, request.Slot, color);
                Apply(ref state, request.Target, appearance);
                results.Add(new CreatureRecolorResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    Failure = CreatureRecolorFailure.None,
                    Appearance = appearance
                });
            }

            for (int i = 0; i < patternRequests.Length; i++)
            {
                CreaturePatternRequest request = patternRequests[i];
                CreatureRecolorFailure failure = Validate(state.EntityManager, request.Target);
                if (failure != CreatureRecolorFailure.None)
                {
                    results.Add(Failure(request.RequestId, request.Target, failure));
                    continue;
                }

                CreaturePatternMask supported = state.EntityManager
                    .HasComponent<CreatureSupportedPatterns>(request.Target)
                    ? state.EntityManager.GetComponentData<CreatureSupportedPatterns>(request.Target).Value
                    : CreaturePatternMask.None;
                if (!CreatureAppearanceRules.Supports(supported, request.Pattern))
                {
                    results.Add(Failure(request.RequestId, request.Target,
                        CreatureRecolorFailure.UnsupportedPattern));
                    continue;
                }

                CreatureAppearance appearance =
                    state.EntityManager.GetComponentData<CreatureAppearance>(request.Target);
                if (request.PaletteId != CreatureAppearanceRules.UnknownPaletteId)
                {
                    if (!CreatureAppearanceRules.TryResolvePalette(palette, request.PaletteId, out float4 color))
                    {
                        results.Add(Failure(request.RequestId, request.Target,
                            CreatureRecolorFailure.UnknownPalette));
                        continue;
                    }
                    appearance.PatternColor = color;
                }

                appearance = CreatureAppearanceRules.WithPattern(appearance, request.Pattern, request.Strength);
                Apply(ref state, request.Target, appearance);
                results.Add(new CreatureRecolorResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    Failure = CreatureRecolorFailure.None,
                    Appearance = appearance
                });
            }

            colorRequests.Clear();
            patternRequests.Clear();
        }

        private static void Apply(ref SystemState state, Entity target, in CreatureAppearance appearance)
        {
            state.EntityManager.SetComponentData(target, appearance);
            if (state.EntityManager.HasComponent<CreatureAppearanceDirty>(target))
                state.EntityManager.SetComponentEnabled<CreatureAppearanceDirty>(target, true);
            if (state.EntityManager.HasComponent<CreatureCustomized>(target))
                state.EntityManager.SetComponentEnabled<CreatureCustomized>(target, true);
        }

        private static CreatureRecolorFailure Validate(EntityManager entityManager, Entity target)
        {
            if (target == Entity.Null || !entityManager.Exists(target) ||
                !entityManager.HasComponent<Creature>(target) ||
                !entityManager.HasComponent<CreatureAppearance>(target))
                return CreatureRecolorFailure.InvalidTarget;

            Creature creature = entityManager.GetComponentData<Creature>(target);
            if ((creature.Interactions & CreatureInteractionMask.Recolor) == 0)
                return CreatureRecolorFailure.NotRecolorable;
            if (entityManager.HasComponent<CreatureTaming>(target) &&
                entityManager.GetComponentData<CreatureTaming>(target).IsTamed == 0)
                return CreatureRecolorFailure.NotTamed;
            return CreatureRecolorFailure.None;
        }

        private static CreatureRecolorResult Failure(uint requestId, Entity target, CreatureRecolorFailure failure)
            => new CreatureRecolorResult
            {
                RequestId = requestId,
                Target = target,
                Failure = failure
            };
    }
}
