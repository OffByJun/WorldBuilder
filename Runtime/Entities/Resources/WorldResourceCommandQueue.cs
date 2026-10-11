using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace WorldBuilder.Entities.Resources
{
    public static class WorldResourceCommandQueue
    {
        public static void DestroyHierarchy(EntityManager manager, EntityCommandBuffer commands, Entity entity)
        {
            // Instantiated prefabs own their children through LinkedEntityGroup. Scene-baked roots do not.
            if (!manager.HasBuffer<LinkedEntityGroup>(entity) && manager.HasBuffer<Unity.Transforms.Child>(entity))
            {
                foreach (var child in manager.GetBuffer<Unity.Transforms.Child>(entity, true))
                    if (manager.Exists(child.Value)) DestroyHierarchy(manager, commands, child.Value);
            }
            commands.DestroyEntity(entity);
        }

        private static uint nextRequestId;
        public static Func<InventoryGrantRequest, int?> RemoteInventoryGrant;

        public static bool TryGetInteractionInfo(Entity target, out ResourceInteractionInfo info)
        {
            info = default;
            if (!TryGetRuntime(out EntityManager entityManager, out _) || target == Entity.Null ||
                !entityManager.Exists(target) || entityManager.HasComponent<Disabled>(target)) return false;
            if (entityManager.HasComponent<ResourceNode>(target))
            {
                info = new ResourceInteractionInfo(entityManager.GetComponentData<ResourceNode>(target));
                return true;
            }
            if (entityManager.HasComponent<DroppedItem>(target))
            {
                info = new ResourceInteractionInfo(entityManager.GetComponentData<DroppedItem>(target));
                return true;
            }
            return false;
        }

        public static bool TryRaycast(Vector3 origin, Vector3 direction, float distance,
            out Entity target, out float fraction)
        {
            target = Entity.Null;
            fraction = 1f;
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;
            if (!WorldRuntimeLookup.TryGetPhysics(out PhysicsWorldSingleton physics)) return false;
            float3 start = origin;
            float3 end = start + math.normalizesafe((float3)direction, new float3(0f, 0f, 1f)) * math.max(0f, distance);
            RaycastInput input = new RaycastInput
            {
                Start = start,
                End = end,
                Filter = CollisionFilter.Default
            };
            if (!physics.CollisionWorld.CastRay(input, out Unity.Physics.RaycastHit hit)) return false;
            if (!TryGetInteractionInfo(hit.Entity, out _)) return false;
            target = hit.Entity;
            fraction = hit.Fraction;
            return true;
        }

        public static bool TryHarvest(Entity target, HarvestMethod method, int toolItemId,
            byte toolTier, float toolPower, float damage, out uint requestId, int pieceIndex = -1, bool grantToInventory = false)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<ResourceHarvestRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<ResourceHarvestRequest>(runtime).Add(new ResourceHarvestRequest
            {
                RequestId = requestId,
                Target = target,
                Method = method,
                ToolItemId = toolItemId,
                ToolTier = toolTier,
                ToolPower = math.max(0f, toolPower),
                Damage = math.max(0f, damage),
                PieceIndex = pieceIndex, HasTargetPiece = (byte)(pieceIndex >= 0 ? 1 : 0),
                GrantToInventory = (byte)(grantToInventory ? 1 : 0)
            });
            return true;
        }

        public static bool TryPickup(Entity target, out uint requestId)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<DroppedItemPickupRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<DroppedItemPickupRequest>(runtime).Add(new DroppedItemPickupRequest
            {
                RequestId = requestId,
                Target = target
            });
            return true;
        }

        public static int ProcessInventoryTransfers(Func<int, int, int> acceptItems)
        {
            if (acceptItems == null) throw new ArgumentNullException(nameof(acceptItems));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<InventoryGrantRequest>(runtime) ||
                !entityManager.HasBuffer<InventoryGrantResult>(runtime)) return 0;
            DynamicBuffer<InventoryGrantRequest> requests = entityManager.GetBuffer<InventoryGrantRequest>(runtime);
            if (requests.IsEmpty) return 0;
            // Inventory notifications may change ECS entities and invalidate every live buffer reference.
            using var pending = requests.ToNativeArray(Allocator.Temp);
            requests.Clear();
            int processed = pending.Length;
            for (int i = 0; i < pending.Length; i++)
            {
                InventoryGrantRequest request = pending[i];
                int accepted = math.clamp(RemoteInventoryGrant?.Invoke(request) ?? acceptItems(request.ItemId, request.RequestedCount), 0,
                    request.RequestedCount);
                if (request.IsHarvest != 0)
                {
                    if (accepted < request.RequestedCount)
                    {
                        var drop = request.HarvestDrop;
                        drop.Count = request.RequestedCount - accepted;
                        entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime).Add(drop);
                    }
                    continue;
                }
                entityManager.GetBuffer<InventoryGrantResult>(runtime).Add(new InventoryGrantResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    AcceptedCount = accepted
                });
            }
            return processed;
        }

        public static int DrainHarvestResults(Action<ResourceHarvestResult> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<ResourceHarvestResult>(runtime)) return 0;
            DynamicBuffer<ResourceHarvestResult> results = entityManager.GetBuffer<ResourceHarvestResult>(runtime);
            if (results.IsEmpty) return 0;
            using var pending = results.ToNativeArray(Allocator.Temp);
            results.Clear();
            for (int i = 0; i < pending.Length; i++) visitor(pending[i]);
            return pending.Length;
        }

        private static uint NextRequestId()
        {
            unchecked
            {
                nextRequestId++;
                if (nextRequestId == 0) nextRequestId = 1;
                return nextRequestId;
            }
        }

        private static bool TryGetRuntime(out EntityManager entityManager, out Entity runtime)
            => WorldRuntimeLookup.TryGetRuntime(out entityManager, out runtime);
    }
}
