using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using WorldBuilder.Entities.Resources;

namespace WorldBuilder.Entities
{
    public struct WorldEntitySnapshot
    {
        public WorldEntityIdentity Identity;
        public int PrefabId;
        public float3 Position;
        public quaternion Rotation;
        public float UniformScale;
        public float3 Velocity;
        public float RemainingLifetime;
        public float ResourceHealth;
        public float ResourceRespawnRemaining;
        public int DroppedItemId;
        public int DroppedItemCount;
        public WorldEntitySpawnRequestFlags StateFlags;
    }

    public static class WorldEntitySnapshotService
    {
        public static bool TryCapture(List<WorldEntitySnapshot> destination)
        {
            destination.Clear();
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            EntityManager entityManager = world.EntityManager;
            EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<WorldEntityIdentity>(),
                    ComponentType.ReadOnly<WorldEntityDescriptor>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            NativeArray<WorldEntityIdentity> identities =
                query.ToComponentDataArray<WorldEntityIdentity>(Allocator.Temp);
            NativeArray<WorldEntityDescriptor> descriptors =
                query.ToComponentDataArray<WorldEntityDescriptor>(Allocator.Temp);
            NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if ((descriptors[i].Flags & WorldEntityFlags.Persistent) == 0) continue;
                Entity entity = entities[i];
                WorldEntitySnapshot snapshot = new WorldEntitySnapshot
                {
                    Identity = identities[i],
                    PrefabId = descriptors[i].PrefabId,
                    Position = transforms[i].Position,
                    Rotation = transforms[i].Rotation,
                    UniformScale = transforms[i].Scale
                };
                if (entityManager.HasComponent<WorldEntityVelocity>(entity))
                {
                    snapshot.StateFlags |= WorldEntitySpawnRequestFlags.RestoreVelocity;
                    snapshot.Velocity = entityManager.GetComponentData<WorldEntityVelocity>(entity).Value;
                }
                if (entityManager.HasComponent<WorldEntityLifetime>(entity))
                {
                    snapshot.StateFlags |= WorldEntitySpawnRequestFlags.RestoreLifetime;
                    snapshot.RemainingLifetime =
                        entityManager.GetComponentData<WorldEntityLifetime>(entity).RemainingSeconds;
                }
                if (entityManager.HasComponent<ResourceNode>(entity))
                {
                    snapshot.StateFlags |= WorldEntitySpawnRequestFlags.RestoreResourceHealth;
                    snapshot.ResourceHealth = entityManager.GetComponentData<ResourceNode>(entity).Health;
                    if (entityManager.HasComponent<Disabled>(entity) &&
                        entityManager.HasComponent<ResourceRespawnState>(entity))
                    {
                        snapshot.StateFlags |= WorldEntitySpawnRequestFlags.RestoreResourceRespawn;
                        snapshot.ResourceRespawnRemaining =
                            entityManager.GetComponentData<ResourceRespawnState>(entity).RemainingSeconds;
                    }
                }
                if (entityManager.HasComponent<DroppedItem>(entity))
                {
                    DroppedItem dropped = entityManager.GetComponentData<DroppedItem>(entity);
                    snapshot.StateFlags |= WorldEntitySpawnRequestFlags.RestoreDroppedItem;
                    snapshot.DroppedItemId = dropped.ItemId;
                    snapshot.DroppedItemCount = dropped.Count;
                }
                destination.Add(snapshot);
            }

            transforms.Dispose();
            descriptors.Dispose();
            identities.Dispose();
            entities.Dispose();
            query.Dispose();
            return true;
        }
    }
}
