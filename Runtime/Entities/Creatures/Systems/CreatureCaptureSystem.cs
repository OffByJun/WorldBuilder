using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using WorldBuilder.Entities.Resources;
using WorldBuilder.Entities.Resources.Systems;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct CreatureCaptureRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureCaptureRequest>();
            state.RequireForUpdate<CreatureGradeDefinition>();
            state.RequireForUpdate<InventoryGrantRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureCaptureRequest>();
            DynamicBuffer<CreatureCaptureRequest> requests =
                state.EntityManager.GetBuffer<CreatureCaptureRequest>(runtime);
            if (requests.IsEmpty) return;

            DynamicBuffer<CreatureGradeDefinition> definitions =
                state.EntityManager.GetBuffer<CreatureGradeDefinition>(runtime, true);
            DynamicBuffer<InventoryGrantRequest> grants =
                state.EntityManager.GetBuffer<InventoryGrantRequest>(runtime);
            DynamicBuffer<CreatureCaptureResult> results =
                state.EntityManager.GetBuffer<CreatureCaptureResult>(runtime);

            for (int i = 0; i < requests.Length; i++)
            {
                CreatureCaptureRequest request = requests[i];
                if (!IsCapturable(state.EntityManager, request.Target))
                {
                    results.Add(new CreatureCaptureResult
                    {
                        RequestId = request.RequestId,
                        Target = request.Target,
                        Failure = CreatureCaptureFailure.InvalidTarget
                    });
                    continue;
                }

                Creature creature = state.EntityManager.GetComponentData<Creature>(request.Target);
                CreatureCapture capture = state.EntityManager.GetComponentData<CreatureCapture>(request.Target);
                bool alreadyCaptured = state.EntityManager.IsComponentEnabled<CreatureCaptured>(request.Target);
                bool isTamed = state.EntityManager.HasComponent<CreatureTaming>(request.Target) &&
                               state.EntityManager.GetComponentData<CreatureTaming>(request.Target).IsTamed != 0;
                CreatureCaptureFailure failure = CreatureInteractionRules.EvaluateCapture(creature, capture,
                    alreadyCaptured, isTamed, request.ToolItemId, request.ToolTier);
                if (failure != CreatureCaptureFailure.None)
                {
                    results.Add(new CreatureCaptureResult
                    {
                        RequestId = request.RequestId,
                        Target = request.Target,
                        Failure = failure
                    });
                    continue;
                }

                int count = CreatureGradeRules.RewardCount(capture.BaseCount,
                    CreatureGradeRules.Resolve(definitions, creature.Grade).ValueMultiplier);
                state.EntityManager.SetComponentEnabled<CreatureCaptured>(request.Target, true);
                grants.Add(new InventoryGrantRequest
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    ItemId = capture.ItemId,
                    RequestedCount = count
                });
                results.Add(new CreatureCaptureResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    Failure = CreatureCaptureFailure.None,
                    ItemId = capture.ItemId,
                    Count = count
                });
            }

            requests.Clear();
        }

        private static bool IsCapturable(EntityManager entityManager, Entity target)
            => target != Entity.Null && entityManager.Exists(target) &&
               entityManager.HasComponent<Creature>(target) &&
               entityManager.HasComponent<CreatureCapture>(target) &&
               entityManager.HasComponent<CreatureCaptured>(target);
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(InventoryGrantResultSystem))]
    public partial struct CreatureCaptureGrantSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InventoryGrantResult>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<InventoryGrantResult>();
            DynamicBuffer<InventoryGrantResult> results =
                state.EntityManager.GetBuffer<InventoryGrantResult>(runtime);
            if (results.IsEmpty) return;

            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);
            for (int i = results.Length - 1; i >= 0; i--)
            {
                InventoryGrantResult result = results[i];
                if (result.Target == Entity.Null || !state.EntityManager.Exists(result.Target) ||
                    !state.EntityManager.HasComponent<CreatureCaptured>(result.Target)) continue;

                results.RemoveAtSwapBack(i);
                if (math.max(0, result.AcceptedCount) > 0) commands.DestroyEntity(result.Target);
                else state.EntityManager.SetComponentEnabled<CreatureCaptured>(result.Target, false);
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
