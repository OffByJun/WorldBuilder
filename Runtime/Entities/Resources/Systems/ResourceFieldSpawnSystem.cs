using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Resources.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ResourceFieldSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntityPrefabElement>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<ResourceFieldSpawnZone>();
        }

        public void OnUpdate(ref SystemState state)
        {
            double elapsed = SystemAPI.Time.ElapsedTime;
            bool hasDueZone = false;
            foreach (RefRO<ResourceFieldSpawnZone> zone in SystemAPI.Query<RefRO<ResourceFieldSpawnZone>>())
            {
                if (elapsed < zone.ValueRO.NextSpawnTime) continue;
                hasDueZone = true;
                break;
            }
            if (!hasDueZone) return;

            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<WorldEntityPrefabElement> prefabs =
                state.EntityManager.GetBuffer<WorldEntityPrefabElement>(runtime, true);
            NativeParallelHashMap<int, Entity> prefabMap =
                new NativeParallelHashMap<int, Entity>(math.max(1, prefabs.Length), Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++) prefabMap.TryAdd(prefabs[i].PrefabId, prefabs[i].Prefab);
            PhysicsWorldSingleton physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            RefRW<WorldEntityRuntimeConfig> config = SystemAPI.GetSingletonRW<WorldEntityRuntimeConfig>();
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (zone, transform, spawned, zoneEntity) in
                     SystemAPI.Query<RefRW<ResourceFieldSpawnZone>, RefRO<LocalTransform>,
                         DynamicBuffer<ResourceFieldSpawnedEntity>>().WithEntityAccess())
            {
                PruneDestroyed(state.EntityManager, spawned);
                if (zone.ValueRO.MaximumAlive <= spawned.Length || elapsed < zone.ValueRO.NextSpawnTime ||
                    !prefabMap.TryGetValue(zone.ValueRO.PrefabId, out Entity prefab)) continue;

                ResourceFieldSpawnZone value = zone.ValueRO;
                Random random = Random.CreateFromIndex(value.RandomState == 0 ? 1u : value.RandomState);
                int count = math.min(value.SpawnPerTick, value.MaximumAlive - spawned.Length);
                for (int i = 0; i < count; i++)
                {
                    float2 offset = random.NextFloat2(new float2(-value.HalfExtents.x, -value.HalfExtents.z),
                        new float2(value.HalfExtents.x, value.HalfExtents.z));
                    float3 center = transform.ValueRO.Position;
                    RaycastInput input = new RaycastInput
                    {
                        Start = center + new float3(offset.x, value.RaycastHeight, offset.y),
                        End = center + new float3(offset.x, -value.RaycastHeight, offset.y),
                        Filter = CollisionFilter.Default
                    };
                    if (!physics.CollisionWorld.CastRay(input, out Unity.Physics.RaycastHit hit)) continue;

                    Entity instance = commands.Instantiate(prefab);
                    config.ValueRW.NextRuntimeId++;
                    commands.SetComponent(instance, new WorldEntityIdentity
                    {
                        High = value.RandomState,
                        Low = config.ValueRO.NextRuntimeId
                    });
                    commands.SetComponent(instance,
                        LocalTransform.FromPositionRotationScale(hit.Position, quaternion.identity, 1f));
                    int2 chunk = WorldEntityGridUtility.WorldToChunk(hit.Position, config.ValueRO);
                    commands.SetComponent(instance, new WorldEntityChunk
                    {
                        Chunk = chunk,
                        Region = WorldEntityGridUtility.ChunkToRegion(chunk, config.ValueRO.ChunksPerRegion)
                    });
                    commands.SetComponentEnabled<WorldEntityActive>(instance, true);
                    if (value.Kind == ResourceFieldSpawnKind.DroppedItem)
                    {
                        int itemCount = random.NextInt(value.MinimumItemCount, value.MaximumItemCount + 1);
                        DroppedItem droppedItem = state.EntityManager.HasComponent<DroppedItem>(prefab)
                            ? state.EntityManager.GetComponentData<DroppedItem>(prefab)
                            : default;
                        droppedItem.ItemId = value.ItemId;
                        droppedItem.Count = itemCount;
                        commands.SetComponent(instance, droppedItem);
                        commands.SetComponentEnabled<DroppedItemPendingPickup>(instance, false);
                    }
                    commands.AppendToBuffer(zoneEntity, new ResourceFieldSpawnedEntity { Value = instance });
                }

                value.RandomState = random.NextUInt();
                value.NextSpawnTime = elapsed + value.SpawnInterval;
                zone.ValueRW = value;
            }

            config.ValueRW.RegionRevision++;
            commands.Playback(state.EntityManager);
            commands.Dispose();
            prefabMap.Dispose();
        }

        private static void PruneDestroyed(EntityManager entityManager,
            DynamicBuffer<ResourceFieldSpawnedEntity> spawned)
        {
            for (int i = spawned.Length - 1; i >= 0; i--)
                if (!entityManager.Exists(spawned[i].Value)) spawned.RemoveAtSwapBack(i);
        }
    }
}
