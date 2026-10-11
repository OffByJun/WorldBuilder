using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using WorldBuilder.Entities.Creatures;

namespace WorldBuilder.Entities
{
    /// <summary>
    /// The queries every command queue needs, made once per world. Building a query on each call allocated and
    /// looked the query up again, and these calls run many times a frame from gameplay code.
    /// Queries belong to their world and go away with it, so a new world simply gets new ones.
    /// </summary>
    internal static class WorldRuntimeLookup
    {
        private static World _world;
        private static EntityQuery _runtime;
        private static EntityQuery _physics;
        private static EntityQuery _creatures;

        public static bool TryGetRuntime(out EntityManager entityManager, out Entity runtime)
        {
            if (!TryGetWorld(out entityManager))
            {
                runtime = Entity.Null;
                return false;
            }
            bool found = _runtime.CalculateEntityCount() == 1;
            runtime = found ? _runtime.GetSingletonEntity() : Entity.Null;
            return found;
        }

        public static bool TryGetPhysics(out PhysicsWorldSingleton physics)
        {
            physics = default;
            if (!TryGetWorld(out EntityManager entityManager) || _physics.CalculateEntityCount() != 1) return false;
            entityManager.CompleteDependencyBeforeRO<PhysicsWorldSingleton>();
            physics = _physics.GetSingleton<PhysicsWorldSingleton>();
            return true;
        }

        /// <summary>Every non-decorative creature, disabled ones included. Do not dispose it.</summary>
        public static EntityQuery Creatures(EntityManager entityManager)
        {
            TryGetWorld(out _);
            return _creatures;
        }

        private static bool TryGetWorld(out EntityManager entityManager)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                entityManager = default;
                return false;
            }
            entityManager = world.EntityManager;
            if (_world == world) return true;

            _world = world;
            _runtime = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldEntityRuntimeConfig>());
            _physics = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            _creatures = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Creature>(), ComponentType.ReadOnly<LocalTransform>() },
                None = new[] { ComponentType.ReadOnly<DecorativeCreature>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState
            });
            return true;
        }
    }
}
