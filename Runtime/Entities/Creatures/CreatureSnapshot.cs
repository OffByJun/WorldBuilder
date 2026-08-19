using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Creatures
{
    public struct CreatureSnapshot
    {
        public WorldEntityIdentity Identity;
        public int PrefabId;
        public int2 Region;
        public float3 Position;
        public quaternion Rotation;
        public float3 Home;
        public CreatureGrade Grade;
        public CreatureAppearance Appearance;
        public float Affinity;
        public byte TameAttempts;
        public byte IsTamed;
    }

    public static class CreatureSnapshotService
    {
        public static bool TryCapture(List<CreatureSnapshot> destination)
        {
            if (destination == null) return false;
            destination.Clear();
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;

            EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Creature>(),
                    ComponentType.ReadOnly<WorldEntityIdentity>(),
                    ComponentType.ReadOnly<WorldEntityDescriptor>(),
                    ComponentType.ReadOnly<WorldEntityChunk>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities |
                          EntityQueryOptions.IgnoreComponentEnabledState
            });

            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                WorldEntityDescriptor descriptor = entityManager.GetComponentData<WorldEntityDescriptor>(entity);
                bool tamed = entityManager.HasComponent<CreatureTaming>(entity) &&
                             entityManager.GetComponentData<CreatureTaming>(entity).IsTamed != 0;
                if ((descriptor.Flags & WorldEntityFlags.Persistent) == 0 && !tamed) continue;

                LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
                Creature creature = entityManager.GetComponentData<Creature>(entity);
                CreatureSnapshot snapshot = new CreatureSnapshot
                {
                    Identity = entityManager.GetComponentData<WorldEntityIdentity>(entity),
                    PrefabId = descriptor.PrefabId,
                    Region = entityManager.GetComponentData<WorldEntityChunk>(entity).Region,
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Home = transform.Position,
                    Grade = creature.Grade
                };
                if (entityManager.HasComponent<CreatureAppearance>(entity))
                    snapshot.Appearance = entityManager.GetComponentData<CreatureAppearance>(entity);
                if (entityManager.HasComponent<CreatureSwim>(entity))
                    snapshot.Home = entityManager.GetComponentData<CreatureSwim>(entity).Home;
                if (entityManager.HasComponent<CreatureAffinity>(entity))
                    snapshot.Affinity = entityManager.GetComponentData<CreatureAffinity>(entity).Value;
                if (entityManager.HasComponent<CreatureTaming>(entity))
                {
                    CreatureTaming taming = entityManager.GetComponentData<CreatureTaming>(entity);
                    snapshot.TameAttempts = taming.AttemptCount;
                    snapshot.IsTamed = taming.IsTamed;
                }
                destination.Add(snapshot);
            }

            entities.Dispose();
            query.Dispose();
            return true;
        }

        public static int Restore(IReadOnlyList<CreatureSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureSpawnRequest>(runtime)) return 0;

            DynamicBuffer<CreatureSpawnRequest> requests =
                entityManager.GetBuffer<CreatureSpawnRequest>(runtime);
            for (int i = 0; i < snapshots.Count; i++)
            {
                CreatureSnapshot snapshot = snapshots[i];
                requests.Add(new CreatureSpawnRequest
                {
                    PrefabId = snapshot.PrefabId,
                    Owner = Entity.Null,
                    Identity = snapshot.Identity,
                    Position = snapshot.Position,
                    Rotation = snapshot.Rotation,
                    Home = snapshot.Home,
                    Grade = snapshot.Grade,
                    Appearance = snapshot.Appearance,
                    Affinity = snapshot.Affinity,
                    TameAttempts = snapshot.TameAttempts,
                    IsTamed = snapshot.IsTamed,
                    Seed = CreatureGradeRules.SanitizeSeed(snapshot.Identity.Low != 0
                        ? (uint)snapshot.Identity.Low
                        : (uint)(i + 1)),
                    StateFlags = CreatureSpawnRequestFlags.ExplicitGrade |
                                 CreatureSpawnRequestFlags.ExplicitAppearance |
                                 CreatureSpawnRequestFlags.ExplicitHome |
                                 CreatureSpawnRequestFlags.ExplicitIdentity |
                                 CreatureSpawnRequestFlags.ExplicitAffinity |
                                 CreatureSpawnRequestFlags.ExplicitTaming
                });
            }

            return snapshots.Count;
        }

        public static int RestoreRegion(IReadOnlyList<CreatureSnapshot> snapshots, int2 region)
        {
            if (snapshots == null || snapshots.Count == 0) return 0;
            List<CreatureSnapshot> filtered = new List<CreatureSnapshot>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i].Region.Equals(region)) filtered.Add(snapshots[i]);
            return Restore(filtered);
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
