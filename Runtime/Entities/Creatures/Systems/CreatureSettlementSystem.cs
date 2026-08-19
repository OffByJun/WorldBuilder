using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(CreatureRecolorSystem))]
    public partial struct CreatureSettlementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureSettleRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureSettleRequest>();
            DynamicBuffer<CreatureSettleRequest> settleRequests =
                state.EntityManager.GetBuffer<CreatureSettleRequest>(runtime);
            DynamicBuffer<CreatureUnsettleRequest> unsettleRequests =
                state.EntityManager.GetBuffer<CreatureUnsettleRequest>(runtime);
            if (settleRequests.IsEmpty && unsettleRequests.IsEmpty) return;

            DynamicBuffer<CreatureSettleResult> results =
                state.EntityManager.GetBuffer<CreatureSettleResult>(runtime);

            for (int i = 0; i < settleRequests.Length; i++) Settle(ref state, settleRequests[i], results);
            for (int i = 0; i < unsettleRequests.Length; i++) Unsettle(ref state, unsettleRequests[i], results);

            settleRequests.Clear();
            unsettleRequests.Clear();
        }

        private static void Settle(ref SystemState state, in CreatureSettleRequest request,
            DynamicBuffer<CreatureSettleResult> results)
        {
            EntityManager entityManager = state.EntityManager;
            if (!IsSettleable(entityManager, request.Target) ||
                !IsHabitat(entityManager, request.Habitat))
            {
                results.Add(Failure(request, CreatureSettleFailure.InvalidTarget));
                return;
            }

            Creature creature = entityManager.GetComponentData<Creature>(request.Target);
            CreatureHabitat habitat = entityManager.GetComponentData<CreatureHabitat>(request.Habitat);
            CreatureEnvironmentNeeds needs = entityManager.GetComponentData<CreatureEnvironmentNeeds>(request.Target);
            DynamicBuffer<CreatureHabitatMember> members =
                entityManager.GetBuffer<CreatureHabitatMember>(request.Habitat);
            Prune(entityManager, members);

            bool isTamed = entityManager.HasComponent<CreatureTaming>(request.Target) &&
                           entityManager.GetComponentData<CreatureTaming>(request.Target).IsTamed != 0;
            bool isCustomized = entityManager.HasComponent<CreatureCustomized>(request.Target) &&
                                entityManager.IsComponentEnabled<CreatureCustomized>(request.Target);
            bool alreadySettled = entityManager.IsComponentEnabled<CreatureSettled>(request.Target);

            CreatureSettleFailure failure = CreatureSettlementRules.Evaluate(creature, isTamed, isCustomized,
                alreadySettled, needs, habitat, members.Length);
            if (failure != CreatureSettleFailure.None)
            {
                results.Add(Failure(request, failure));
                return;
            }

            CreatureRoleAptitude aptitude = entityManager.GetComponentData<CreatureRoleAptitude>(request.Target);
            CreatureAppearance appearance = entityManager.GetComponentData<CreatureAppearance>(request.Target);
            CreatureWorkTraits traits = CreatureSettlementRules.Traits(aptitude, appearance, needs, habitat);

            float3 center = entityManager.GetComponentData<LocalTransform>(request.Habitat).Position;
            CreatureRandom seed = entityManager.GetComponentData<CreatureRandom>(request.Target);
            Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(seed.State));
            float3 living = CreatureSettlementRules.LivingPosition(center, habitat, members.Length, ref random);
            seed.State = CreatureGradeRules.SanitizeSeed(random.NextUInt());
            entityManager.SetComponentData(request.Target, seed);

            entityManager.SetComponentData(request.Target, new CreatureSettlement
            {
                Habitat = request.Habitat,
                LivingPosition = living,
                ActivityRadius = math.cmax(habitat.HalfExtents),
                WorkSpeed = traits.WorkSpeed,
                CarryCapacity = traits.CarryCapacity
            });
            entityManager.SetComponentEnabled<CreatureSettled>(request.Target, true);
            entityManager.SetComponentData(request.Target, new CreatureWorkState
            {
                Phase = CreatureWorkPhase.Idle
            });

            CreatureSwim swim = entityManager.GetComponentData<CreatureSwim>(request.Target);
            swim.Home = living;
            swim.WanderRadius = math.max(1f, math.cmin(habitat.HalfExtents) * 0.6f);
            swim.NextRepathTime = 0d;
            entityManager.SetComponentData(request.Target, swim);

            members.Add(new CreatureHabitatMember { Value = request.Target });
            results.Add(new CreatureSettleResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Habitat = request.Habitat,
                Failure = CreatureSettleFailure.None,
                Roles = aptitude.Roles,
                WorkSpeed = traits.WorkSpeed,
                CarryCapacity = traits.CarryCapacity
            });
        }

        private static void Unsettle(ref SystemState state, in CreatureUnsettleRequest request,
            DynamicBuffer<CreatureSettleResult> results)
        {
            EntityManager entityManager = state.EntityManager;
            if (request.Target == Entity.Null || !entityManager.Exists(request.Target) ||
                !entityManager.HasComponent<CreatureSettlement>(request.Target))
            {
                results.Add(new CreatureSettleResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    Failure = CreatureSettleFailure.InvalidTarget
                });
                return;
            }

            CreatureSettlement settlement = entityManager.GetComponentData<CreatureSettlement>(request.Target);
            if (settlement.Habitat != Entity.Null && entityManager.Exists(settlement.Habitat) &&
                entityManager.HasBuffer<CreatureHabitatMember>(settlement.Habitat))
            {
                DynamicBuffer<CreatureHabitatMember> members =
                    entityManager.GetBuffer<CreatureHabitatMember>(settlement.Habitat);
                for (int i = members.Length - 1; i >= 0; i--)
                    if (members[i].Value == request.Target) members.RemoveAtSwapBack(i);
            }

            ReleaseSite(entityManager, request.Target);
            entityManager.SetComponentData(request.Target, new CreatureSettlement());
            entityManager.SetComponentEnabled<CreatureSettled>(request.Target, false);
            entityManager.SetComponentData(request.Target, new CreatureWorkState());
            if (entityManager.HasComponent<CreatureMoveOrder>(request.Target))
                entityManager.SetComponentEnabled<CreatureMoveOrder>(request.Target, false);

            results.Add(new CreatureSettleResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Failure = CreatureSettleFailure.None
            });
        }

        private static void ReleaseSite(EntityManager entityManager, Entity worker)
        {
            if (!entityManager.HasComponent<CreatureWorkState>(worker)) return;
            CreatureWorkState work = entityManager.GetComponentData<CreatureWorkState>(worker);
            if (work.Site == Entity.Null || !entityManager.Exists(work.Site) ||
                !entityManager.HasComponent<CreatureWorkSite>(work.Site)) return;

            CreatureWorkSite site = entityManager.GetComponentData<CreatureWorkSite>(work.Site);
            if (site.Claimant != worker) return;
            site.Claimant = Entity.Null;
            site.State = CreatureWorkSiteState.Ready;
            entityManager.SetComponentData(work.Site, site);
            if (entityManager.HasComponent<CreatureWorkSiteReady>(work.Site))
                entityManager.SetComponentEnabled<CreatureWorkSiteReady>(work.Site, true);
        }

        private static void Prune(EntityManager entityManager, DynamicBuffer<CreatureHabitatMember> members)
        {
            for (int i = members.Length - 1; i >= 0; i--)
                if (!entityManager.Exists(members[i].Value)) members.RemoveAtSwapBack(i);
        }

        private static bool IsSettleable(EntityManager entityManager, Entity target)
            => target != Entity.Null && entityManager.Exists(target) &&
               entityManager.HasComponent<Creature>(target) &&
               entityManager.HasComponent<CreatureSettlement>(target) &&
               entityManager.HasComponent<CreatureSettled>(target) &&
               entityManager.HasComponent<CreatureWorkState>(target) &&
               entityManager.HasComponent<CreatureRoleAptitude>(target) &&
               entityManager.HasComponent<CreatureEnvironmentNeeds>(target) &&
               entityManager.HasComponent<CreatureAppearance>(target) &&
               entityManager.HasComponent<CreatureRandom>(target) &&
               entityManager.HasComponent<CreatureSwim>(target);

        private static bool IsHabitat(EntityManager entityManager, Entity habitat)
            => habitat != Entity.Null && entityManager.Exists(habitat) &&
               entityManager.HasComponent<CreatureHabitat>(habitat) &&
               entityManager.HasBuffer<CreatureHabitatMember>(habitat) &&
               entityManager.HasComponent<LocalTransform>(habitat);

        private static CreatureSettleResult Failure(in CreatureSettleRequest request, CreatureSettleFailure failure)
            => new CreatureSettleResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Habitat = request.Habitat,
                Failure = failure
            };
    }
}
