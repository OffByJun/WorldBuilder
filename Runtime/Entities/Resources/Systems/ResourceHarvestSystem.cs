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

                float previousHealth = node.Health;
                bool hasPieces = state.EntityManager.HasComponent<ResourceNodePieces>(request.Target);
                bool individual = state.EntityManager.HasComponent<ResourcePieceState>(request.Target);
                int brokenPiece = -1;
                bool inventory = request.GrantToInventory != 0 && request.Method == HarvestMethod.Pickaxe && hasPieces &&
                    state.EntityManager.GetComponentData<ResourceNodePieces>(request.Target).Kind != ResourceNodePieces.SeaweedKind;
                float damage = math.max(0f, request.Damage);
                if (individual && node.MaxHealth > 0f)
                {
                    var pieces = state.EntityManager.GetComponentData<ResourcePieceState>(request.Target);
                    byte kind = state.EntityManager.GetComponentData<ResourceNodePieces>(request.Target).Kind;
                    float3 position = state.EntityManager.GetComponentData<LocalTransform>(request.Target).Position;
                    int seed = ResourceNodePieces.PositionSeed(position, kind);
                    if (pieces.Health.Length == 0)
                        pieces.Initialize(ResourceNodePieces.RemainingCount(kind, seed, 1f), node.Health / node.MaxHealth);
                    int selected = request.HasTargetPiece != 0 ? request.PieceIndex : pieces.Health.Length - 1;
                    if (request.HasTargetPiece == 0)
                        while (selected >= 0 && pieces.Health[selected] <= .0001f) selected--;
                    if (selected < 0 || selected >= pieces.Health.Length || pieces.Health[selected] <= .0001f)
                    {
                        results.Add(CreateResult(request, HarvestFailureReason.InvalidTarget, node.Health, false));
                        continue;
                    }
                    pieces.Health[selected] = math.max(0f, pieces.Health[selected] - damage / node.MaxHealth);
                    if (pieces.Health[selected] <= .0001f)
                    {
                        pieces.Health[selected] = 0f;
                        brokenPiece = selected;
                    }
                    float totalHealth = 0f;
                    for (int piece = 0; piece < pieces.Health.Length; piece++) totalHealth += pieces.Health[piece];
                    node.Health = node.MaxHealth * totalHealth / pieces.Health.Length;
                    state.EntityManager.SetComponentData(request.Target, pieces);
                }
                else if (hasPieces)
                {
                    byte kind = state.EntityManager.GetComponentData<ResourceNodePieces>(request.Target).Kind;
                    if (kind != ResourceNodePieces.SeaweedKind)
                    {
                        float3 position = state.EntityManager.GetComponentData<LocalTransform>(request.Target).Position;
                        int seed = ResourceNodePieces.PositionSeed(position, kind);
                        // Mineral health is the durability of each piece, rather than divided between all pieces.
                        damage /= ResourceNodePieces.RemainingCount(kind, seed, 1f);
                    }
                }
                if (!individual) node.Health = math.max(0f, node.Health - damage);
                if (node.Health < .0001f) node.Health = 0f;
                node.NextHitTime = elapsedTime + node.HitCooldownSeconds;
                bool depleted = node.Health <= 0f;
                if (brokenPiece >= 0)
                    CreateDrops(state.EntityManager, request.Target, node, drops, (uint)(brokenPiece + 1), inventory, request.RequestId, runtime);
                if (!individual && hasPieces && node.MaxHealth > 0f)
                {
                    byte kind = state.EntityManager.GetComponentData<ResourceNodePieces>(request.Target).Kind;
                    float3 position = state.EntityManager.GetComponentData<LocalTransform>(request.Target).Position;
                    int seed = ResourceNodePieces.PositionSeed(position, kind);
                    int before = ResourceNodePieces.RemainingCount(kind, seed, previousHealth / node.MaxHealth);
                    int after = ResourceNodePieces.RemainingCount(kind, seed, node.Health / node.MaxHealth);
                    int total = ResourceNodePieces.RemainingCount(kind, seed, 1f);
                    for (int piece = after; piece < before; piece++)
                        CreateDrops(state.EntityManager, request.Target, node, drops, (uint)(total - piece), inventory, request.RequestId, runtime);
                }
                if (depleted)
                {
                    node.DepletionCount++;
                    if (!hasPieces) CreateDrops(state.EntityManager, request.Target, node, drops);
                    if (node.RespawnSeconds > 0f)
                    {
                        var respawn = new ResourceRespawnState { RemainingSeconds = node.RespawnSeconds };
                        if (state.EntityManager.HasComponent<ResourceRespawnState>(request.Target))
                            state.EntityManager.SetComponentData(request.Target, respawn);
                        else commands.AddComponent(request.Target, respawn);
                        commands.AddComponent<Disabled>(request.Target);
                    }
                    else
                    {
                        WorldResourceCommandQueue.DestroyHierarchy(state.EntityManager, commands, request.Target);
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
            DynamicBuffer<ResourceDropSpawnRequest> output, uint pieceIndex = 0, bool inventory = false,
            uint requestId = 0, Entity runtime = default)
        {
            if (!entityManager.HasBuffer<ResourceDrop>(target) ||
                !entityManager.HasComponent<LocalTransform>(target)) return;
            DynamicBuffer<ResourceDrop> table = entityManager.GetBuffer<ResourceDrop>(target, true);
            float3 origin = entityManager.GetComponentData<LocalTransform>(target).Position;
            uint seed = math.hash(new uint2(node.RandomSeed == 0 ? 1u : node.RandomSeed,
                node.DepletionCount == 0 ? 1u : node.DepletionCount));
            if (pieceIndex > 0)
                seed = math.hash(new uint3(node.RandomSeed == 0 ? 1u : node.RandomSeed,
                    node.DepletionCount + 1u, pieceIndex));
            Random random = Random.CreateFromIndex(seed);
            int multiplier = 1;
            if (pieceIndex > 0)
            {
                var pieces = entityManager.GetComponentData<ResourceNodePieces>(target);
                int minimum = math.max(1, pieces.MinimumDropMultiplier);
                multiplier = random.NextInt(minimum, math.max(minimum, pieces.MaximumDropMultiplier) + 1);
            }
            for (int i = 0; i < table.Length; i++)
            {
                ResourceDrop entry = table[i];
                if (random.NextFloat() > math.saturate(entry.Probability)) continue;
                int count = random.NextInt(entry.MinimumCount, entry.MaximumCount + 1) * multiplier;
                if (count <= 0) continue;
                float2 offset = random.NextFloat2Direction() * random.NextFloat(0.15f, 0.65f);
                var drop = new ResourceDropSpawnRequest
                {
                    PrefabId = node.DroppedItemPrefabId,
                    ItemId = entry.ItemId,
                    Count = count,
                    Position = origin + new float3(offset.x, 0.2f, offset.y),
                    Seed = random.NextUInt()
                };
                if (inventory)
                    entityManager.GetBuffer<InventoryGrantRequest>(runtime).Add(new InventoryGrantRequest
                    {
                        RequestId = requestId, Target = target, ItemId = entry.ItemId,
                        RequestedCount = count, IsHarvest = 1, HarvestDrop = drop
                    });
                else output.Add(drop);
            }
        }
    }
}
