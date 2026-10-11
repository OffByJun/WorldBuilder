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

            EntityQuery exclusionsQuery = SystemAPI.QueryBuilder().WithAll<ResourceSpawnExclusion>().Build();
            using NativeArray<ResourceSpawnExclusion> exclusions = exclusionsQuery.ToComponentDataArray<ResourceSpawnExclusion>(Allocator.Temp);

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
            NativeList<float3> pendingPositions = new NativeList<float3>(Allocator.Temp);
            int remainingAttempts = 4;

            foreach (var (zone, transform, spawned, points, zoneEntity) in
                     SystemAPI.Query<RefRW<ResourceFieldSpawnZone>, RefRO<LocalTransform>,
                         DynamicBuffer<ResourceFieldSpawnedEntity>, DynamicBuffer<ResourceFieldSpawnPoint>>()
                         .WithEntityAccess())
            {
                if (remainingAttempts == 0) break;
                if (elapsed < zone.ValueRO.NextSpawnTime) continue;
                PruneDestroyed(state.EntityManager, spawned);
                if (zone.ValueRO.MaximumAlive <= spawned.Length)
                {
                    zone.ValueRW.PendingSpawnAttempts = 0;
                    zone.ValueRW.NextSpawnTime = elapsed + math.max(1f, zone.ValueRO.SpawnInterval);
                    continue;
                }

                ResourceFieldSpawnZone value = zone.ValueRO;
                pendingPositions.Clear();
                bool hasEntries = state.EntityManager.HasBuffer<ResourceFieldSpawnEntry>(zoneEntity);
                DynamicBuffer<ResourceFieldSpawnEntry> entries = hasEntries
                    ? state.EntityManager.GetBuffer<ResourceFieldSpawnEntry>(zoneEntity, true) : default;
                Random random = Random.CreateFromIndex(value.RandomState == 0 ? 1u : value.RandomState);
                int requested = math.min(value.PendingSpawnAttempts > 0 ? value.PendingSpawnAttempts : value.SpawnPerTick,
                    value.MaximumAlive - spawned.Length);
                int count = math.min(requested, math.min(1, remainingAttempts));
                remainingAttempts -= count;
                for (int i = 0; i < count; i++)
                {
                    if (!TrySelectEntry(ref random, value, hasEntries, entries, prefabMap,
                            state.EntityManager, out ResourceFieldSpawnEntry selected, out Entity prefab)) continue;
                    float individualRate = state.EntityManager.HasComponent<WorldEntitySpawnRate>(prefab)
                        ? state.EntityManager.GetComponentData<WorldEntitySpawnRate>(prefab).Value : 1f;
                    float ceiling = state.EntityManager.HasComponent<WorldEntitySpawnRate>(zoneEntity)
                        ? state.EntityManager.GetComponentData<WorldEntitySpawnRate>(zoneEntity).Value : 1f;
                    if (individualRate <= 0f || (individualRate < ceiling && random.NextFloat() >= individualRate / ceiling)) continue;
                    if (!TryGetPointInZone(ref random, points, out float2 normalizedPoint)) continue;

                    float3 localOffset = new float3(normalizedPoint.x * value.HalfExtents.x, 0f,
                        normalizedPoint.y * value.HalfExtents.z);
                    float3 offset = math.rotate(transform.ValueRO.Rotation, localOffset);
                    float3 center = transform.ValueRO.Position;
                    float verticalReach = value.RaycastHeight + value.HalfExtents.y;
                    RaycastInput input = new RaycastInput
                    {
                        Start = center + offset + new float3(0f, verticalReach, 0f),
                        End = center + offset - new float3(0f, verticalReach, 0f),
                        Filter = CollisionFilter.Default
                    };
                    hits.Clear();
                    if (!physics.CollisionWorld.CastRay(input, ref hits) ||
                        !TryGetTerrainHit(state.EntityManager, hits, value.MinimumGroundNormalY,
                            out Unity.Physics.RaycastHit hit)) continue;
                    float3 groundPosition = hit.Position;
                    float3 groundNormal = hit.SurfaceNormal;
                    if (IsExcluded(groundPosition, exclusions)) continue;
                    if (value.MinimumSpacing > 0f && TooClose(state.EntityManager, spawned,
                            pendingPositions, groundPosition, value.MinimumSpacing)) continue;
                    pendingPositions.Add(groundPosition);

                    Entity instance = commands.Instantiate(prefab);
                    config.ValueRW.NextRuntimeId++;
                    commands.SetComponent(instance, new WorldEntityIdentity
                    {
                        High = value.RandomState,
                        Low = config.ValueRO.NextRuntimeId
                    });
                    commands.SetComponent(instance,
                        LocalTransform.FromPositionRotationScale(groundPosition + groundNormal * 0.03f,
                            value.Kind == ResourceFieldSpawnKind.DroppedItem
                                || state.EntityManager.HasComponent<ResourceNodePieces>(prefab)
                                ? SurfaceRotation(groundNormal)
                                : quaternion.identity,
                            state.EntityManager.HasComponent<LocalTransform>(prefab)
                                ? state.EntityManager.GetComponentData<LocalTransform>(prefab).Scale : 1f));
                    int2 chunk = WorldEntityGridUtility.WorldToChunk(groundPosition, config.ValueRO);
                    commands.SetComponent(instance, new WorldEntityChunk
                    {
                        Chunk = chunk,
                        Region = WorldEntityGridUtility.ChunkToRegion(chunk, config.ValueRO.ChunksPerRegion)
                    });
                    commands.SetComponentEnabled<WorldEntityActive>(instance, true);
                    if (value.Kind == ResourceFieldSpawnKind.DroppedItem)
                    {
                        int itemCount = random.NextInt(selected.MinimumCount, selected.MaximumCount + 1);
                        DroppedItem droppedItem = state.EntityManager.HasComponent<DroppedItem>(prefab)
                            ? state.EntityManager.GetComponentData<DroppedItem>(prefab)
                            : default;
                        droppedItem.ItemId = selected.ItemId;
                        droppedItem.Count = itemCount;
                        commands.SetComponent(instance, droppedItem);
                        commands.SetComponentEnabled<DroppedItemPendingPickup>(instance, false);
                    }
                    commands.AppendToBuffer(zoneEntity, new ResourceFieldSpawnedEntity { Value = instance });
                }

                value.RandomState = random.NextUInt();
                value.PendingSpawnAttempts = requested - count;
                value.NextSpawnTime = elapsed + (value.PendingSpawnAttempts > 0 ? .1f : value.SpawnInterval);
                zone.ValueRW = value;
            }

            config.ValueRW.RegionRevision++;
            commands.Playback(state.EntityManager);
            commands.Dispose();
            hits.Dispose();
            pendingPositions.Dispose();
            prefabMap.Dispose();
        }

        private static bool TrySelectEntry(ref Random random, ResourceFieldSpawnZone zone, bool hasEntries,
            DynamicBuffer<ResourceFieldSpawnEntry> entries, NativeParallelHashMap<int, Entity> prefabs,
            EntityManager manager, out ResourceFieldSpawnEntry selected, out Entity prefab)
        {
            selected = default;
            prefab = Entity.Null;
            if (!hasEntries || entries.Length == 0)
            {
                selected = new ResourceFieldSpawnEntry { PrefabId = zone.PrefabId, ItemId = zone.ItemId,
                    MinimumCount = math.max(1, zone.MinimumItemCount),
                    MaximumCount = math.max(1, math.max(zone.MinimumItemCount, zone.MaximumItemCount)), Weight = 1f };
                return IsValidEntry(selected, zone.Kind, prefabs, manager, out prefab);
            }

            float total = 0f;
            for (int i = 0; i < entries.Length; i++)
                if (IsValidEntry(entries[i], zone.Kind, prefabs, manager, out _)) total += math.saturate(entries[i].Weight);
            if (!(total > 0f) || !math.isfinite(total)) return false;
            float roll = random.NextFloat() * total;
            for (int i = 0; i < entries.Length; i++)
            {
                ResourceFieldSpawnEntry candidate = entries[i];
                if (!IsValidEntry(candidate, zone.Kind, prefabs, manager, out Entity candidatePrefab)) continue;
                selected = candidate;
                prefab = candidatePrefab;
                roll -= math.saturate(candidate.Weight);
                if (roll < 0f) return true;
            }
            return prefab != Entity.Null;
        }

        private static bool IsValidEntry(ResourceFieldSpawnEntry entry, ResourceFieldSpawnKind kind,
            NativeParallelHashMap<int, Entity> prefabs, EntityManager manager, out Entity prefab)
        {
            prefab = Entity.Null;
            if (!(entry.Weight > 0f) || !math.isfinite(entry.Weight) ||
                !prefabs.TryGetValue(entry.PrefabId, out prefab)) return false;
            return kind == ResourceFieldSpawnKind.ResourceNode
                ? manager.HasComponent<ResourceNode>(prefab)
                : entry.ItemId >= 0 && manager.HasComponent<DroppedItem>(prefab)
                    && manager.HasComponent<DroppedItemPendingPickup>(prefab);
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

        private static bool TooClose(EntityManager manager, DynamicBuffer<ResourceFieldSpawnedEntity> spawned,
            NativeList<float3> pending, float3 position, float spacing)
        {
            float distanceSquared = spacing * spacing;
            foreach (var entry in spawned)
                if (manager.Exists(entry.Value) && manager.HasComponent<LocalTransform>(entry.Value) &&
                    math.distancesq(position.xz, manager.GetComponentData<LocalTransform>(entry.Value).Position.xz) < distanceSquared)
                    return true;
            foreach (float3 other in pending)
                if (math.distancesq(position.xz, other.xz) < distanceSquared) return true;
            return false;
        }

        private static bool IsExcluded(float3 position, NativeArray<ResourceSpawnExclusion> exclusions)
        {
            foreach (var exclusion in exclusions)
                if (exclusion.Contains(position)) return true;
            return false;
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
                if (hit.Fraction >= nearestFraction ||
                    !entityManager.HasComponent<ResourceSpawnTerrain>(hit.Entity)) continue;

                terrainHit = hit;
                nearestFraction = hit.Fraction;
                found = true;
            }

            // Reject a steep upper surface rather than spawning on a hidden layer below it.
            return found && terrainHit.SurfaceNormal.y >= minimumGroundNormalY;
        }

        private static quaternion SurfaceRotation(float3 normal)
        {
            normal = math.normalizesafe(normal, new float3(0f, 1f, 0f));
            // Choose a tangent that remains stable even on near-vertical slopes.
            float3 axis = math.abs(normal.z) < .9f ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f);
            float3 forward = math.normalizesafe(axis - normal * math.dot(axis, normal));
            return quaternion.LookRotationSafe(forward, normal);
        }

        private static void PruneDestroyed(EntityManager entityManager,
            DynamicBuffer<ResourceFieldSpawnedEntity> spawned)
        {
            for (int i = spawned.Length - 1; i >= 0; i--)
                if (!entityManager.Exists(spawned[i].Value)) spawned.RemoveAtSwapBack(i);
        }
    }
}
