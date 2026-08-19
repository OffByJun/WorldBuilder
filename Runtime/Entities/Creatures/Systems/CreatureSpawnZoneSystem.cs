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
            double elapsed = SystemAPI.Time.ElapsedTime;
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            WorldEntityRuntimeConfig config = SystemAPI.GetSingleton<WorldEntityRuntimeConfig>();
            DynamicBuffer<CreatureGradeDefinition> definitions =
                state.EntityManager.GetBuffer<CreatureGradeDefinition>(runtime, true);
            DynamicBuffer<CreatureSpawnRequest> requests =
                state.EntityManager.GetBuffer<CreatureSpawnRequest>(runtime);

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
                if (elapsed < value.NextSpawnTime || spawned.Length >= value.MaximumAlive) continue;

                Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(value.RandomState));
                int count = math.min(value.SpawnPerTick, value.MaximumAlive - spawned.Length);
                for (int i = 0; i < count; i++)
                {
                    float3 position = center + random.NextFloat3(-value.HalfExtents, value.HalfExtents);
                    requests.Add(new CreatureSpawnRequest
                    {
                        PrefabId = value.PrefabId,
                        Owner = zoneEntity,
                        Position = position,
                        Rotation = quaternion.RotateY(random.NextFloat(0f, 2f * math.PI)),
                        Grade = CreatureGradeRules.SelectGrade(definitions, value.AllowedGrades, ref random),
                        Seed = random.NextUInt(),
                        StateFlags = CreatureSpawnRequestFlags.ExplicitGrade
                    });
                }

                value.RandomState = CreatureGradeRules.SanitizeSeed(random.NextUInt());
                value.NextSpawnTime = elapsed + value.SpawnInterval;
                zone.ValueRW = value;
            }

            if (loaded.IsCreated) loaded.Dispose();
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
