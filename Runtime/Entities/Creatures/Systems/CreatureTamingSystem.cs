using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct CreatureTamingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureFeedRequest>();
            state.RequireForUpdate<CreatureGradeDefinition>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureFeedRequest>();
            DynamicBuffer<CreatureFeedRequest> requests = state.EntityManager.GetBuffer<CreatureFeedRequest>(runtime);
            if (requests.IsEmpty) return;

            DynamicBuffer<CreatureFeedResult> results = state.EntityManager.GetBuffer<CreatureFeedResult>(runtime);
            DynamicBuffer<CreatureGradeDefinition> definitions =
                state.EntityManager.GetBuffer<CreatureGradeDefinition>(runtime, true);
            double elapsed = SystemAPI.Time.ElapsedTime;

            for (int i = 0; i < requests.Length; i++)
            {
                CreatureFeedRequest request = requests[i];
                if (!IsFeedable(state.EntityManager, request.Target))
                {
                    results.Add(Failure(request, CreatureFeedFailure.InvalidTarget));
                    continue;
                }

                Creature creature = state.EntityManager.GetComponentData<Creature>(request.Target);
                CreatureAffinity affinity = state.EntityManager.GetComponentData<CreatureAffinity>(request.Target);
                CreatureTaming taming = state.EntityManager.GetComponentData<CreatureTaming>(request.Target);

                CreatureFeedFailure failure = Validate(state.EntityManager, request, creature, affinity, taming,
                    elapsed);
                if (failure != CreatureFeedFailure.None)
                {
                    results.Add(Failure(request, failure, affinity, taming));
                    continue;
                }

                bool preferred = affinity.PreferredItemId == CreatureInteractionRules.AnyItemId ||
                                 affinity.PreferredItemId == request.ItemId;
                float gradeMultiplier = CreatureGradeRules.Resolve(definitions, creature.Grade).TameChanceMultiplier;

                CreatureRandom seed = state.EntityManager.GetComponentData<CreatureRandom>(request.Target);
                Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(seed.State));
                CreatureTamingOutcome outcome =
                    CreatureTamingRules.Attempt(taming, preferred, gradeMultiplier, ref random);
                seed.State = CreatureGradeRules.SanitizeSeed(random.NextUInt());
                state.EntityManager.SetComponentData(request.Target, seed);

                taming = CreatureTamingRules.ApplyOutcome(taming, outcome);
                state.EntityManager.SetComponentData(request.Target, taming);

                affinity.Value = math.min(affinity.MaximumValue,
                    affinity.Value + math.max(0f, affinity.GainPerFeed) * (preferred ? 1.5f : 1f));
                affinity.NextFeedTime = elapsed + math.max(0f, affinity.FeedCooldownSeconds);
                state.EntityManager.SetComponentData(request.Target, affinity);

                if (outcome.Success)
                {
                    if (state.EntityManager.HasComponent<CreatureTamed>(request.Target))
                        state.EntityManager.SetComponentEnabled<CreatureTamed>(request.Target, true);
                }
                else if (state.EntityManager.HasComponent<CreatureAlarm>(request.Target))
                {
                    CreatureAlarm alarm = state.EntityManager.GetComponentData<CreatureAlarm>(request.Target);
                    state.EntityManager.SetComponentData(request.Target,
                        CreatureTamingRules.Alarm(alarm, request.SourcePosition, elapsed));
                }

                results.Add(new CreatureFeedResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    Failure = CreatureFeedFailure.None,
                    Affinity = affinity.Value,
                    MaximumAffinity = affinity.MaximumValue,
                    SuccessChance = outcome.Chance,
                    AttemptCount = taming.AttemptCount,
                    TamedNow = (byte)(outcome.Success ? 1 : 0),
                    IsTamed = taming.IsTamed
                });
            }

            requests.Clear();
        }

        private static bool IsFeedable(EntityManager entityManager, Entity target)
            => target != Entity.Null && entityManager.Exists(target) &&
               entityManager.HasComponent<Creature>(target) &&
               entityManager.HasComponent<CreatureAffinity>(target) &&
               entityManager.HasComponent<CreatureTaming>(target) &&
               entityManager.HasComponent<CreatureRandom>(target);

        private static CreatureFeedFailure Validate(EntityManager entityManager, in CreatureFeedRequest request,
            in Creature creature, in CreatureAffinity affinity, in CreatureTaming taming, double elapsed)
        {
            if ((creature.Interactions & CreatureInteractionMask.Feed) == 0)
                return CreatureFeedFailure.NotFeedable;
            if (creature.SizeClass == CreatureSizeClass.Large) return CreatureFeedFailure.NotFeedable;
            if (taming.IsTamed != 0 && affinity.Value >= affinity.MaximumValue)
                return CreatureFeedFailure.AlreadyTamed;
            if (elapsed < affinity.NextFeedTime) return CreatureFeedFailure.Cooldown;
            if (entityManager.HasComponent<CreatureAlarm>(request.Target) &&
                CreatureTamingRules.IsAlarmed(entityManager.GetComponentData<CreatureAlarm>(request.Target), elapsed))
                return CreatureFeedFailure.Alarmed;
            return CreatureFeedFailure.None;
        }

        private static CreatureFeedResult Failure(in CreatureFeedRequest request, CreatureFeedFailure failure)
            => new CreatureFeedResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Failure = failure
            };

        private static CreatureFeedResult Failure(in CreatureFeedRequest request, CreatureFeedFailure failure,
            in CreatureAffinity affinity, in CreatureTaming taming)
            => new CreatureFeedResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Failure = failure,
                Affinity = affinity.Value,
                MaximumAffinity = affinity.MaximumValue,
                AttemptCount = taming.AttemptCount,
                IsTamed = taming.IsTamed
            };
    }
}
