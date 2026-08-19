using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Resources.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(ResourceHarvestSystem))]
    public partial struct ResourceDropSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ResourceDropSpawnRequest>();
            state.RequireForUpdate<WorldEntityPrefabElement>();
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<ResourceDropSpawnRequest> requests =
                state.EntityManager.GetBuffer<ResourceDropSpawnRequest>(runtime);
            if (requests.IsEmpty) return;
            DynamicBuffer<WorldEntityPrefabElement> prefabs =
                state.EntityManager.GetBuffer<WorldEntityPrefabElement>(runtime, true);
            NativeParallelHashMap<int, Entity> prefabMap =
                new NativeParallelHashMap<int, Entity>(math.max(1, prefabs.Length), Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++) prefabMap.TryAdd(prefabs[i].PrefabId, prefabs[i].Prefab);

            RefRW<WorldEntityRuntimeConfig> config = SystemAPI.GetSingletonRW<WorldEntityRuntimeConfig>();
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                ResourceDropSpawnRequest request = requests[i];
                if (!prefabMap.TryGetValue(request.PrefabId, out Entity prefab)) continue;
                Entity instance = commands.Instantiate(prefab);
                config.ValueRW.NextRuntimeId++;
                commands.SetComponent(instance,
                    new WorldEntityIdentity { High = request.Seed, Low = config.ValueRO.NextRuntimeId });
                commands.SetComponent(instance,
                    LocalTransform.FromPositionRotationScale(request.Position, quaternion.identity, 1f));
                DroppedItem droppedItem = state.EntityManager.HasComponent<DroppedItem>(prefab)
                    ? state.EntityManager.GetComponentData<DroppedItem>(prefab)
                    : default;
                droppedItem.ItemId = request.ItemId;
                droppedItem.Count = request.Count;
                commands.SetComponent(instance, droppedItem);
                int2 chunk = WorldEntityGridUtility.WorldToChunk(request.Position, config.ValueRO);
                commands.SetComponent(instance, new WorldEntityChunk
                {
                    Chunk = chunk,
                    Region = WorldEntityGridUtility.ChunkToRegion(chunk, config.ValueRO.ChunksPerRegion)
                });
                commands.SetComponentEnabled<DroppedItemPendingPickup>(instance, false);
                commands.SetComponentEnabled<WorldEntityActive>(instance, true);
            }

            config.ValueRW.RegionRevision++;
            requests.Clear();
            commands.Playback(state.EntityManager);
            commands.Dispose();
            prefabMap.Dispose();
        }
    }
}
