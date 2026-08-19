using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Resources.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct DroppedItemPickupRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DroppedItemPickupRequest>();
            state.RequireForUpdate<InventoryGrantRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<DroppedItemPickupRequest>();
            DynamicBuffer<DroppedItemPickupRequest> requests =
                state.EntityManager.GetBuffer<DroppedItemPickupRequest>(runtime);
            if (requests.IsEmpty) return;
            DynamicBuffer<InventoryGrantRequest> grants =
                state.EntityManager.GetBuffer<InventoryGrantRequest>(runtime);
            for (int i = 0; i < requests.Length; i++)
            {
                DroppedItemPickupRequest request = requests[i];
                if (request.Target == Entity.Null || !state.EntityManager.Exists(request.Target) ||
                    !state.EntityManager.HasComponent<DroppedItem>(request.Target) ||
                    !state.EntityManager.HasComponent<DroppedItemPendingPickup>(request.Target) ||
                    state.EntityManager.IsComponentEnabled<DroppedItemPendingPickup>(request.Target)) continue;
                DroppedItem item = state.EntityManager.GetComponentData<DroppedItem>(request.Target);
                if (item.Count <= 0) continue;
                state.EntityManager.SetComponentEnabled<DroppedItemPendingPickup>(request.Target, true);
                grants.Add(new InventoryGrantRequest
                {
                    RequestId = request.RequestId,
                    Target = request.Target,
                    ItemId = item.ItemId,
                    RequestedCount = item.Count
                });
            }
            requests.Clear();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct InventoryGrantResultSystem : ISystem
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
            for (int i = 0; i < results.Length; i++)
            {
                InventoryGrantResult result = results[i];
                if (result.Target == Entity.Null || !state.EntityManager.Exists(result.Target) ||
                    !state.EntityManager.HasComponent<DroppedItem>(result.Target)) continue;
                DroppedItem item = state.EntityManager.GetComponentData<DroppedItem>(result.Target);
                item.Count -= math.clamp(result.AcceptedCount, 0, item.Count);
                if (item.Count <= 0)
                    commands.DestroyEntity(result.Target);
                else
                {
                    state.EntityManager.SetComponentData(result.Target, item);
                    if (state.EntityManager.HasComponent<DroppedItemPendingPickup>(result.Target))
                        state.EntityManager.SetComponentEnabled<DroppedItemPendingPickup>(result.Target, false);
                }
            }
            results.Clear();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
