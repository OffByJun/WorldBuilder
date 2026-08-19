using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Resources.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct ResourceHarvestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ResourceHarvestRequest>();
            state.RequireForUpdate<ResourceHarvestResult>();
            state.RequireForUpdate<ResourceDropSpawnRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<ResourceHarvestRequest>();
            DynamicBuffer<ResourceHarvestRequest> requests =
                state.EntityManager.GetBuffer<ResourceHarvestRequest>(runtime);
            if (requests.IsEmpty) return;

            DynamicBuffer<ResourceHarvestResult> results =
                state.EntityManager.GetBuffer<ResourceHarvestResult>(runtime);
            DynamicBuffer<ResourceDropSpawnRequest> drops =
                state.EntityManager.GetBuffer<ResourceDropSpawnRequest>(runtime);
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);
            double elapsedTime = SystemAPI.Time.ElapsedTime;

            for (int i = 0; i < requests.Length; i++)
            {
                ResourceHarvestRequest request = requests[i];
                HarvestFailureReason failure = Validate(state.EntityManager, request, elapsedTime,
                    out ResourceNode node);
                if (failure != HarvestFailureReason.None)
                {
                    results.Add(CreateResult(request, failure, node.Health, false));
                    continue;
                }

                node.Health = math.max(0f, node.Health - math.max(0f, request.Damage));
                node.NextHitTime = elapsedTime + node.HitCooldownSeconds;
                bool depleted = node.Health <= 0f;
                if (depleted)
                {
                    node.DepletionCount++;
                    CreateDrops(state.EntityManager, request.Target, node, drops);
                    if (node.RespawnSeconds > 0f)
                    {
                        state.EntityManager.SetComponentData(request.Target,
                            new ResourceRespawnState { RemainingSeconds = node.RespawnSeconds });
                        commands.AddComponent<Disabled>(request.Target);
                    }
                    else
                    {
                        commands.DestroyEntity(request.Target);
                    }
                }

                state.EntityManager.SetComponentData(request.Target, node);
                results.Add(CreateResult(request, HarvestFailureReason.None, node.Health, depleted));
            }

            requests.Clear();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        private static HarvestFailureReason Validate(EntityManager entityManager,
            ResourceHarvestRequest request, double elapsedTime, out ResourceNode node)
        {
            node = default;
            if (request.Target == Entity.Null || !entityManager.Exists(request.Target) ||
                !entityManager.HasComponent<ResourceNode>(request.Target))
                return HarvestFailureReason.InvalidTarget;
            node = entityManager.GetComponentData<ResourceNode>(request.Target);
            if (entityManager.HasComponent<Disabled>(request.Target)) return HarvestFailureReason.Respawning;
            if (elapsedTime < node.NextHitTime) return HarvestFailureReason.Cooldown;
            if ((node.AllowedMethods & request.Method) == 0) return HarvestFailureReason.WrongMethod;
            if (node.RequiredToolItemId >= 0 && request.ToolItemId != node.RequiredToolItemId)
                return HarvestFailureReason.RequiredToolMissing;
            if (request.ToolTier < node.MinimumToolTier) return HarvestFailureReason.ToolTierTooLow;
            if (request.ToolPower < node.MinimumToolPower) return HarvestFailureReason.ToolPowerTooLow;
            return HarvestFailureReason.None;
        }

        private static ResourceHarvestResult CreateResult(ResourceHarvestRequest request,
            HarvestFailureReason failure, float health, bool depleted)
        {
            return new ResourceHarvestResult
            {
                RequestId = request.RequestId,
                Target = request.Target,
                Failure = failure,
                RemainingHealth = health,
                Depleted = depleted ? (byte)1 : (byte)0
            };
        }

        private static void CreateDrops(EntityManager entityManager, Entity target, ResourceNode node,
            DynamicBuffer<ResourceDropSpawnRequest> output)
        {
            if (!entityManager.HasBuffer<ResourceDrop>(target) ||
                !entityManager.HasComponent<LocalTransform>(target)) return;
            DynamicBuffer<ResourceDrop> table = entityManager.GetBuffer<ResourceDrop>(target, true);
            float3 origin = entityManager.GetComponentData<LocalTransform>(target).Position;
            uint seed = math.hash(new uint2(node.RandomSeed == 0 ? 1u : node.RandomSeed,
                node.DepletionCount == 0 ? 1u : node.DepletionCount));
            Random random = Random.CreateFromIndex(seed);
            for (int i = 0; i < table.Length; i++)
            {
                ResourceDrop entry = table[i];
                if (random.NextFloat() > math.saturate(entry.Probability)) continue;
                int count = random.NextInt(entry.MinimumCount, entry.MaximumCount + 1);
                if (count <= 0) continue;
                float2 offset = random.NextFloat2Direction() * random.NextFloat(0.15f, 0.65f);
                output.Add(new ResourceDropSpawnRequest
                {
                    PrefabId = node.DroppedItemPrefabId,
                    ItemId = entry.ItemId,
                    Count = count,
                    Position = origin + new float3(offset.x, 0.2f, offset.y),
                    Seed = random.NextUInt()
                });
            }
        }
    }
}
