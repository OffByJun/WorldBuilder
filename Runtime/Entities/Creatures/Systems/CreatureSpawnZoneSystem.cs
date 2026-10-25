using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureSpawnZoneSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<CreatureSpawnRequest>();
            state.RequireForUpdate<CreatureGradeDefinition>();
            state.RequireForUpdate<CreatureSpawnZone>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            double elapsed = SystemAPI.Time.ElapsedTime;
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            WorldEntityRuntimeConfig config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>();
            DynamicBuffer<CreatureGradeDefinition> definitions =
                state.EntityManager.GetBuffer<CreatureGradeDefinition>(runtime, true);
            DynamicBuffer<CreatureSpawnRequest> requests =
                state.EntityManager.GetBuffer<CreatureSpawnRequest>(runtime);
            using var prefabs = new NativeParallelHashSet<int>(16, Allocator.Temp);
            using var prefabRates = new NativeParallelHashMap<int, float>(16, Allocator.Temp);
            if (state.EntityManager.HasBuffer<WorldEntityPrefabElement>(runtime))
            {
                foreach (var entry in state.EntityManager.GetBuffer<WorldEntityPrefabElement>(runtime, true))
                    if (state.EntityManager.HasComponent<Creature>(entry.Prefab))
                    {
                        prefabs.Add(entry.PrefabId);
                        prefabRates.TryAdd(entry.PrefabId, state.EntityManager.HasComponent<WorldEntitySpawnRate>(entry.Prefab)
                            ? state.EntityManager.GetComponentData<WorldEntitySpawnRate>(entry.Prefab).Value : 1f);
                    }
            }

            if (prefabs.Count() == 0) return;
            var pending = new NativeParallelHashMap<Entity, int>(math.max(1, requests.Length), Allocator.Temp);
            foreach (var request in requests)
            {
                if (request.Owner == Entity.Null) continue;
                pending.TryGetValue(request.Owner, out int count);
                pending[request.Owner] = count + 1;
            }

            NativeParallelHashSet<int2> loaded = BuildLoadedRegions(state.EntityManager, runtime);

            foreach (var (zone, transform, spawned, zoneEntity) in
                     SystemAPI.Query<RefRW<CreatureSpawnZone>, RefRO<LocalTransform>,
                         DynamicBuffer<CreatureSpawnedEntity>>().WithEntityAccess())
            {
                PruneDestroyed(state.EntityManager, spawned);
                CreatureSpawnZone value = zone.ValueRO;
                float3 center = transform.ValueRO.Position;

                if (loaded.IsCreated &&
                    !loaded.Contains(WorldEntityGridUtility.WorldToRegion(center, config))) continue;
                bool initial = value.InitialPopulationRequested == 0;
                pending.TryGetValue(zoneEntity, out int queued);
                int available = value.MaximumAlive - spawned.Length - queued;
                if ((!initial && elapsed < value.NextSpawnTime) || available <= 0) continue;

                Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(value.RandomState));
                int count = initial ? available : math.min(value.SpawnPerTick, available);
                for (int i = 0; i < count; i++)
                {
                    if (!TrySelectPrefab(state.EntityManager, zoneEntity, value.PrefabId, prefabs, ref random, out int prefabId)) continue;
                    float individual = prefabRates.TryGetValue(prefabId, out float speciesRate) ? speciesRate : 1f;
                    float ceiling = state.EntityManager.HasComponent<WorldEntitySpawnRate>(zoneEntity)
                        ? state.EntityManager.GetComponentData<WorldEntitySpawnRate>(zoneEntity).Value : 1f;
                    if (individual <= 0f || (individual < ceiling && random.NextFloat() >= individual / ceiling)) continue;
                    float3 position = center + math.rotate(transform.ValueRO.Rotation, random.NextFloat3(-value.HalfExtents, value.HalfExtents));
                    requests.Add(new CreatureSpawnRequest
                    {
                        PrefabId = prefabId,
                        Owner = zoneEntity,
                        Position = position,
                        Rotation = quaternion.RotateY(random.NextFloat(0f, 2f * math.PI)),
                        Grade = CreatureGradeRules.SelectGrade(definitions, value.AllowedGrades, ref random),
                        Seed = random.NextUInt(),
                        StateFlags = CreatureSpawnRequestFlags.ExplicitGrade |
                            (initial ? CreatureSpawnRequestFlags.InitialPopulation : CreatureSpawnRequestFlags.None)
                    });
                }

                value.RandomState = CreatureGradeRules.SanitizeSeed(random.NextUInt());
                value.InitialPopulationRequested = 1;
                value.NextSpawnTime = elapsed + value.SpawnInterval;
                zone.ValueRW = value;
            }

            if (loaded.IsCreated) loaded.Dispose();
            pending.Dispose();
        }

        private static bool TrySelectPrefab(EntityManager manager, Entity zone, int legacyId,
            NativeParallelHashSet<int> prefabs, ref Random random, out int prefabId)
        {
            prefabId = legacyId;
            if (!manager.HasBuffer<CreatureZoneSpawnEntry>(zone)) return legacyId > 0;
            var entries = manager.GetBuffer<CreatureZoneSpawnEntry>(zone, true);
            if (entries.Length == 0) return legacyId > 0;
            float total = 0f;
            foreach (var entry in entries)
                if (entry.Weight > 0f && math.isfinite(entry.Weight) && prefabs.Contains(entry.PrefabId)) total += math.saturate(entry.Weight);
            if (!(total > 0f) || !math.isfinite(total)) return false;
            float roll = random.NextFloat() * total;
            foreach (var entry in entries)
            {
                if (!(entry.Weight > 0f) || !math.isfinite(entry.Weight) || !prefabs.Contains(entry.PrefabId)) continue;
                prefabId = entry.PrefabId;
                roll -= math.saturate(entry.Weight);
                if (roll < 0f) return true;
            }
            return true;
        }

        private static NativeParallelHashSet<int2> BuildLoadedRegions(EntityManager entityManager, Entity runtime)
        {
            if (!entityManager.HasBuffer<WorldEntityLoadedRegion>(runtime)) return default;
            DynamicBuffer<WorldEntityLoadedRegion> regions =
                entityManager.GetBuffer<WorldEntityLoadedRegion>(runtime, true);
            if (regions.IsEmpty) return default;

            NativeParallelHashSet<int2> loaded = new NativeParallelHashSet<int2>(regions.Length, Allocator.Temp);
            for (int i = 0; i < regions.Length; i++) loaded.Add(regions[i].Coordinate);
            return loaded;
        }

        private static void PruneDestroyed(EntityManager entityManager, DynamicBuffer<CreatureSpawnedEntity> spawned)
        {
            for (int i = spawned.Length - 1; i >= 0; i--)
                if (!entityManager.Exists(spawned[i].Value)) spawned.RemoveAtSwapBack(i);
        }
    }
}
