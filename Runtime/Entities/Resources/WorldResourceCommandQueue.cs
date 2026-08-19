using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace WorldBuilder.Entities.Resources
{
    public static class WorldResourceCommandQueue
    {
        private static uint nextRequestId;

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
            EntityQuery physicsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            if (physicsQuery.CalculateEntityCount() != 1)
            {
                physicsQuery.Dispose();
                return false;
            }

            entityManager.CompleteDependencyBeforeRO<PhysicsWorldSingleton>();
            PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
            physicsQuery.Dispose();
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
            byte toolTier, float toolPower, float damage, out uint requestId)
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
                Damage = math.max(0f, damage)
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
            DynamicBuffer<InventoryGrantResult> results = entityManager.GetBuffer<InventoryGrantResult>(runtime);
            int processed = requests.Length;
            for (int i = 0; i < requests.Length; i++)
            {
                InventoryGrantRequest request = requests[i];
                int accepted = math.clamp(acceptItems(request.ItemId, request.RequestedCount), 0,
                    request.RequestedCount);
                results.Add(new InventoryGrantResult
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    AcceptedCount = accepted
                });
            }
            requests.Clear();
            return processed;
        }

        public static int DrainHarvestResults(Action<ResourceHarvestResult> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<ResourceHarvestResult>(runtime)) return 0;
            DynamicBuffer<ResourceHarvestResult> results = entityManager.GetBuffer<ResourceHarvestResult>(runtime);
            int count = results.Length;
            for (int i = 0; i < results.Length; i++) visitor(results[i]);
            results.Clear();
            return count;
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
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                entityManager = default;
                runtime = Entity.Null;
                return false;
            }
            entityManager = world.EntityManager;
            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldEntityRuntimeConfig>());
            bool found = query.CalculateEntityCount() == 1;
            runtime = found ? query.GetSingletonEntity() : Entity.Null;
            query.Dispose();
            return found;
        }
    }
}
