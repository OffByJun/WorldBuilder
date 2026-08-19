using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Entities
{
    public static class WorldEntityCommandQueue
    {
        public static bool IsReady => TryGetRuntime(out _, out _);

        public static bool TrySpawn(int prefabId, Vector3 position, Quaternion rotation,
            float uniformScale = 1f, WorldEntityIdentity identity = default)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime)) return false;
            DynamicBuffer<WorldEntitySpawnRequest> requests =
                entityManager.GetBuffer<WorldEntitySpawnRequest>(runtime);
            requests.Add(new WorldEntitySpawnRequest
            {
                PrefabId = prefabId,
                Identity = identity,
                Position = position,
                Rotation = new quaternion(rotation.x, rotation.y, rotation.z, rotation.w),
                UniformScale = Mathf.Max(0.0001f, uniformScale)
            });
            return true;
        }

        public static bool TrySpawn(in WorldEntitySnapshot snapshot)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime)) return false;
            DynamicBuffer<WorldEntitySpawnRequest> requests =
                entityManager.GetBuffer<WorldEntitySpawnRequest>(runtime);
            requests.Add(new WorldEntitySpawnRequest
            {
                PrefabId = snapshot.PrefabId,
                Identity = snapshot.Identity,
                Position = snapshot.Position,
                Rotation = snapshot.Rotation,
                UniformScale = snapshot.UniformScale,
                Velocity = snapshot.Velocity,
                RemainingLifetime = snapshot.RemainingLifetime,
                ResourceHealth = snapshot.ResourceHealth,
                ResourceRespawnRemaining = snapshot.ResourceRespawnRemaining,
                DroppedItemId = snapshot.DroppedItemId,
                DroppedItemCount = snapshot.DroppedItemCount,
                StateFlags = snapshot.StateFlags
            });
            return true;
        }

        public static bool SetLoadedRegions(IReadOnlyList<RegionCoord> regions)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime)) return false;
            DynamicBuffer<WorldEntityLoadedRegion> loaded =
                entityManager.GetBuffer<WorldEntityLoadedRegion>(runtime);
            loaded.Clear();
            for (int i = 0; i < regions.Count; i++)
                loaded.Add(new WorldEntityLoadedRegion { Coordinate = new int2(regions[i].X, regions[i].Z) });

            WorldEntityRuntimeConfig config = entityManager.GetComponentData<WorldEntityRuntimeConfig>(runtime);
            config.RegionRevision++;
            entityManager.SetComponentData(runtime, config);
            return true;
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
