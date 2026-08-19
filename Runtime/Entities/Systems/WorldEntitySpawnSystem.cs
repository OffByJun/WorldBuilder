using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using WorldBuilder.Entities.Resources;

namespace WorldBuilder.Entities.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct WorldEntitySpawnSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntitySpawnRequest>();
            state.RequireForUpdate<WorldEntityPrefabElement>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<WorldEntitySpawnRequest> requests =
                state.EntityManager.GetBuffer<WorldEntitySpawnRequest>(runtime);
            if (requests.IsEmpty) return;

            DynamicBuffer<WorldEntityPrefabElement> prefabs =
                state.EntityManager.GetBuffer<WorldEntityPrefabElement>(runtime, true);
            NativeParallelHashMap<int, Entity> prefabMap =
                new NativeParallelHashMap<int, Entity>(prefabs.Length, Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
                prefabMap.TryAdd(prefabs[i].PrefabId, prefabs[i].Prefab);

            RefRW<WorldEntityRuntimeConfig> config = SystemAPI.GetSingletonRW<WorldEntityRuntimeConfig>();
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                WorldEntitySpawnRequest request = requests[i];
                if (!prefabMap.TryGetValue(request.PrefabId, out Entity prefab)) continue;

                Entity instance = commands.Instantiate(prefab);
                WorldEntityIdentity identity = request.Identity;
                if (!identity.IsValid)
                {
                    config.ValueRW.NextRuntimeId++;
                    identity.Low = config.ValueRO.NextRuntimeId;
                }

                commands.SetComponent(instance, identity);
                commands.SetComponent(instance,
                    LocalTransform.FromPositionRotationScale(request.Position, request.Rotation,
                        request.UniformScale));
                Unity.Mathematics.int2 chunk = WorldEntityGridUtility.WorldToChunk(request.Position, config.ValueRO);
                commands.SetComponent(instance, new WorldEntityChunk
                {
                    Chunk = chunk,
                    Region = WorldEntityGridUtility.ChunkToRegion(chunk, config.ValueRO.ChunksPerRegion)
                });
                if ((request.StateFlags & WorldEntitySpawnRequestFlags.RestoreVelocity) != 0)
                    commands.SetComponent(instance, new WorldEntityVelocity { Value = request.Velocity });
                if ((request.StateFlags & WorldEntitySpawnRequestFlags.RestoreLifetime) != 0)
                    commands.SetComponent(instance,
                        new WorldEntityLifetime { RemainingSeconds = request.RemainingLifetime });
                if ((request.StateFlags & WorldEntitySpawnRequestFlags.RestoreResourceHealth) != 0)
                {
                    ResourceNode resource = state.EntityManager.GetComponentData<ResourceNode>(prefab);
                    resource.Health = request.ResourceHealth;
                    commands.SetComponent(instance, resource);
                }
                if ((request.StateFlags & WorldEntitySpawnRequestFlags.RestoreResourceRespawn) != 0)
                {
                    commands.SetComponent(instance, new ResourceRespawnState
                    {
                        RemainingSeconds = request.ResourceRespawnRemaining
                    });
                    if (request.ResourceRespawnRemaining > 0f) commands.AddComponent<Disabled>(instance);
                }
                if ((request.StateFlags & WorldEntitySpawnRequestFlags.RestoreDroppedItem) != 0)
                {
                    DroppedItem dropped = state.EntityManager.GetComponentData<DroppedItem>(prefab);
                    dropped.ItemId = request.DroppedItemId;
                    dropped.Count = request.DroppedItemCount;
                    commands.SetComponent(instance, dropped);
                    commands.SetComponentEnabled<DroppedItemPendingPickup>(instance, false);
                }
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
