using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures
{
    public static partial class WorldCreatureCommandQueue
    {
        private static uint nextRequestId;

        public static bool TryGetInteractionInfo(Entity target, out CreatureInteractionInfo info)
        {
            info = default;
            if (!TryGetRuntime(out EntityManager entityManager, out _) || target == Entity.Null ||
                !entityManager.Exists(target) || entityManager.HasComponent<Disabled>(target) ||
                !entityManager.HasComponent<Creature>(target)) return false;

            CreatureCapture capture = entityManager.HasComponent<CreatureCapture>(target)
                ? entityManager.GetComponentData<CreatureCapture>(target)
                : new CreatureCapture { ItemId = -1, RequiredToolItemId = CreatureInteractionRules.AnyItemId };
            CreatureAffinity affinity = entityManager.HasComponent<CreatureAffinity>(target)
                ? entityManager.GetComponentData<CreatureAffinity>(target)
                : default;
            CreatureTaming taming = entityManager.HasComponent<CreatureTaming>(target)
                ? entityManager.GetComponentData<CreatureTaming>(target)
                : default;
            bool alarmed = entityManager.HasComponent<CreatureAlarm>(target) &&
                           CreatureTamingRules.IsAlarmed(
                               entityManager.GetComponentData<CreatureAlarm>(target), ElapsedTime());

            info = new CreatureInteractionInfo(entityManager.GetComponentData<Creature>(target), capture, affinity,
                taming, alarmed);
            return true;
        }

        public static bool TryRaycast(Vector3 origin, Vector3 direction, float distance, out Entity target,
            out float fraction)
        {
            target = Entity.Null;
            fraction = 1f;
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;
            EntityQuery physicsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            if (physicsQuery.CalculateEntityCount() != 1)
            {
                physicsQuery.Dispose();
                return false;
            }

            entityManager.CompleteDependencyBeforeRO<PhysicsWorldSingleton>();
            PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
            physicsQuery.Dispose();
            RaycastInput input = new RaycastInput
            {
                Start = origin,
                End = (float3)origin +
                      math.normalizesafe((float3)direction, new float3(0f, 0f, 1f)) * math.max(0f, distance),
                Filter = CollisionFilter.Default
            };
            if (!physics.CollisionWorld.CastRay(input, out Unity.Physics.RaycastHit hit)) return false;
            if (!TryGetInteractionInfo(hit.Entity, out _)) return false;
            target = hit.Entity;
            fraction = hit.Fraction;
            return true;
        }

        public static bool TryCapture(Entity target, int toolItemId, byte toolTier, out uint requestId)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureCaptureRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<CreatureCaptureRequest>(runtime).Add(new CreatureCaptureRequest
            {
                RequestId = requestId,
                Target = target,
                ToolItemId = toolItemId,
                ToolTier = toolTier
            });
            return true;
        }

        public static bool TryFeed(Entity target, int itemId, Vector3 sourcePosition, out uint requestId)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureFeedRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<CreatureFeedRequest>(runtime).Add(new CreatureFeedRequest
            {
                RequestId = requestId,
                Target = target,
                ItemId = itemId,
                SourcePosition = sourcePosition
            });
            return true;
        }

        public static bool TrySpawn(int prefabId, Vector3 position, Quaternion rotation, uint seed = 0u)
            => Enqueue(prefabId, position, rotation, default, seed, CreatureSpawnRequestFlags.None);

        public static bool TrySpawn(int prefabId, Vector3 position, Quaternion rotation, CreatureGrade grade,
            uint seed = 0u)
            => Enqueue(prefabId, position, rotation, grade, seed, CreatureSpawnRequestFlags.ExplicitGrade);

        public static int DrainCaptureResults(Action<CreatureCaptureResult> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureCaptureResult>(runtime)) return 0;
            DynamicBuffer<CreatureCaptureResult> results = entityManager.GetBuffer<CreatureCaptureResult>(runtime);
            int count = results.Length;
            for (int i = 0; i < results.Length; i++) visitor(results[i]);
            results.Clear();
            return count;
        }

        public static int DrainFeedResults(Action<CreatureFeedResult> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureFeedResult>(runtime)) return 0;
            DynamicBuffer<CreatureFeedResult> results = entityManager.GetBuffer<CreatureFeedResult>(runtime);
            int count = results.Length;
            for (int i = 0; i < results.Length; i++) visitor(results[i]);
            results.Clear();
            return count;
        }

        private static bool Enqueue(int prefabId, Vector3 position, Quaternion rotation, CreatureGrade grade,
            uint seed, CreatureSpawnRequestFlags flags)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureSpawnRequest>(runtime)) return false;
            entityManager.GetBuffer<CreatureSpawnRequest>(runtime).Add(new CreatureSpawnRequest
            {
                PrefabId = prefabId,
                Owner = Entity.Null,
                Position = position,
                Rotation = rotation,
                Grade = grade,
                Seed = seed == 0u ? NextRequestId() : seed,
                StateFlags = flags
            });
            return true;
        }

        private static double ElapsedTime()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            return world != null && world.IsCreated ? world.Time.ElapsedTime : 0d;
        }

        private static uint NextRequestId()
        {
            unchecked
            {
                nextRequestId++;
                if (nextRequestId == 0) nextRequestId = 1;
                return nextRequestId;
            }
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
