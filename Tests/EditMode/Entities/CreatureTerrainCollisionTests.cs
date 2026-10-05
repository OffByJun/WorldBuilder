using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using WorldBuilder.Entities.Creatures;

namespace WorldBuilder.Entities.Tests
{
    public sealed partial class CreatureTerrainCollisionTests
    {
        private World entities;
        private PhysicsWorld physics;
        private BlobAssetReference<Collider> ground;
        private BlobAssetReference<Collider> wall;
        private ComponentLookup<CreatureAlarm> creatures;

        [SetUp]
        public void SetUp()
        {
            entities = new World("Creature terrain tests");
            Entity floorEntity = entities.EntityManager.CreateEntity();
            Entity wallEntity = entities.EntityManager.CreateEntity();
            var system = entities.GetOrCreateSystemManaged<LookupSystem>();
            creatures = system.Lookup();
            ground = BoxCollider.Create(new BoxGeometry
            {
                Size = new float3(100f, 20f, 100f), Orientation = quaternion.identity, BevelRadius = 0f
            }, CollisionFilter.Default);
            wall = BoxCollider.Create(new BoxGeometry
            {
                Size = new float3(1f, 10f, 20f), Orientation = quaternion.identity, BevelRadius = 0f
            }, CollisionFilter.Default);
            physics = new PhysicsWorld(2, 0, 0);
            var bodies = physics.Bodies;
            bodies[0] = new RigidBody { Entity = floorEntity, Collider = ground, Scale = 1f,
                WorldFromBody = new RigidTransform(quaternion.identity, new float3(0f, -10f, 0f)) };
            bodies[1] = new RigidBody { Entity = wallEntity, Collider = wall, Scale = 1f,
                WorldFromBody = new RigidTransform(quaternion.identity, new float3(5f, 5f, 0f)) };
            physics.UpdateIndexMaps();
            physics.CollisionWorld.BuildBroadphase(ref physics, 0f, float3.zero, true);
        }

        [TearDown]
        public void TearDown()
        {
            physics.Dispose();
            ground.Dispose();
            wall.Dispose();
            entities.Dispose();
        }

        [Test]
        public void DeeplyBuriedSpawnIsLiftedAboveTheEntireBody()
        {
            Assert.That(CreatureTerrainCollision.TryPlace(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, -8f, 0f), 2f, 20f, out float3 point), Is.True);
            Assert.That(point.y, Is.GreaterThanOrEqualTo(2f));
        }

        [Test]
        public void ShallowWaterRejectsSpawnInsteadOfClampingItIntoTerrain()
        {
            Assert.That(CreatureTerrainCollision.TryPlace(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, -8f, 0f), 2f, 3f, out _), Is.False);
        }

        [Test]
        public void FastMovementCannotTunnelThroughWall()
        {
            float3 point = CreatureTerrainCollision.Move(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, 3f, 0f), new float3(20f, 3f, 0f), 1f, 20f);
            Assert.That(point.x, Is.LessThan(3.51f));
            Assert.That(point.x, Is.GreaterThan(3f));
        }

        [Test]
        public void WallCollisionStillAllowsTangentialMovement()
        {
            float3 point = CreatureTerrainCollision.Move(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, 3f, 0f), new float3(10f, 3f, 5f), 1f, 20f);
            Assert.That(point.x, Is.LessThan(3.51f));
            Assert.That(point.z, Is.GreaterThan(4f));
        }

        [Test]
        public void DownwardMovementStopsAboveSeabed()
        {
            float3 point = CreatureTerrainCollision.Move(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, 5f, 0f), new float3(0f, -5f, 0f), 1f, 20f);
            Assert.That(point.y, Is.GreaterThanOrEqualTo(1f));
        }

        [DisableAutoCreation]
        private partial class LookupSystem : SystemBase
        {
            public ComponentLookup<CreatureAlarm> Lookup() => GetComponentLookup<CreatureAlarm>(true);
            protected override void OnUpdate() { }
        }
    }
}
