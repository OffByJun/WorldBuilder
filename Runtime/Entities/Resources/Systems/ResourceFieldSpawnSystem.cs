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
            NativeList<Unity.Physics.RaycastHit> hits = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);

            foreach (var (zone, transform, spawned, points, zoneEntity) in
                     SystemAPI.Query<RefRW<ResourceFieldSpawnZone>, RefRO<LocalTransform>,
                         DynamicBuffer<ResourceFieldSpawnedEntity>, DynamicBuffer<ResourceFieldSpawnPoint>>()
                         .WithEntityAccess())
            {
                PruneDestroyed(state.EntityManager, spawned);
                if (zone.ValueRO.MaximumAlive <= spawned.Length || elapsed < zone.ValueRO.NextSpawnTime ||
                    !prefabMap.TryGetValue(zone.ValueRO.PrefabId, out Entity prefab)) continue;

                ResourceFieldSpawnZone value = zone.ValueRO;
                Random random = Random.CreateFromIndex(value.RandomState == 0 ? 1u : value.RandomState);
                int count = math.min(value.SpawnPerTick, value.MaximumAlive - spawned.Length);
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetPointInZone(ref random, points, out float2 normalizedPoint)) continue;

                    float3 localOffset = new float3(normalizedPoint.x * value.HalfExtents.x, 0f,
                        normalizedPoint.y * value.HalfExtents.z);
                    float3 offset = math.rotate(transform.ValueRO.Rotation, localOffset);
                    float3 center = transform.ValueRO.Position;
                    RaycastInput input = new RaycastInput
                    {
                        Start = center + offset + new float3(0f, value.RaycastHeight, 0f),
                        End = center + offset - new float3(0f, value.RaycastHeight, 0f),
                        Filter = CollisionFilter.Default
                    };
                    hits.Clear();
                    if (!physics.CollisionWorld.CastRay(input, ref hits) ||
                        !TryGetTerrainHit(state.EntityManager, hits, value.MinimumGroundNormalY,
                            out Unity.Physics.RaycastHit hit)) continue;

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
            hits.Dispose();
            prefabMap.Dispose();
        }

        private static bool TryGetPointInZone(ref Random random, DynamicBuffer<ResourceFieldSpawnPoint> points,
            out float2 point)
        {
            const int MaximumAttempts = 12;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                point = random.NextFloat2(-1f, 1f);
                if (points.Length < 3 || IsInsidePolygon(point, points)) return true;
            }

            point = default;
            return false;
        }

        private static bool IsInsidePolygon(float2 point, DynamicBuffer<ResourceFieldSpawnPoint> points)
        {
            bool inside = false;
            int previous = points.Length - 1;
            for (int current = 0; current < points.Length; current++)
            {
                float2 a = points[current].Value;
                float2 b = points[previous].Value;
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                               point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (crosses) inside = !inside;
                previous = current;
            }

            return inside;
        }

        private static bool TryGetTerrainHit(EntityManager entityManager,
            NativeList<Unity.Physics.RaycastHit> hits, float minimumGroundNormalY,
            out Unity.Physics.RaycastHit terrainHit)
        {
            terrainHit = default;
            float nearestFraction = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Unity.Physics.RaycastHit hit = hits[i];
                if (hit.Fraction >= nearestFraction || hit.SurfaceNormal.y < minimumGroundNormalY ||
                    !entityManager.HasComponent<ResourceSpawnTerrain>(hit.Entity)) continue;

                terrainHit = hit;
                nearestFraction = hit.Fraction;
                found = true;
            }

            return found;
        }

        private static void PruneDestroyed(EntityManager entityManager,
            DynamicBuffer<ResourceFieldSpawnedEntity> spawned)
        {
            for (int i = spawned.Length - 1; i >= 0; i--)
                if (!entityManager.Exists(spawned[i].Value)) spawned.RemoveAtSwapBack(i);
        }
    }
}
