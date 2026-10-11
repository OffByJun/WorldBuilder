using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using WorldBuilder.Entities.Creatures.Systems;
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
            entities.EntityManager.CompleteAllTrackedJobs();
            physics.Dispose();
            ground.Dispose();
            wall.Dispose();
            entities.Dispose();
        }

        [Test]
        public void ExhaustedCreatureSinksAndStopsAboveTheSeabed()
        {
            float3 point = new float3(0f, 3f, 0f);
            point = CreatureTerrainCollision.Sink(physics.CollisionWorld, creatures, Entity.Null, point, .5f, 1f, out bool grounded);
            Assert.That(grounded, Is.False);
            Assert.That(point.y, Is.EqualTo(2f).Within(.001f));
            for (int i = 0; i < 10 && !grounded; i++)
                point = CreatureTerrainCollision.Sink(physics.CollisionWorld, creatures, Entity.Null, point, .5f, 1f, out grounded);
            Assert.That(grounded, Is.True);
            Assert.That(point.y, Is.EqualTo(.55f).Within(.01f));
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

        [Test]
        public void LocalClearanceDoesNotLiftSwimmerOntoOverheadStructure()
        {
            var bodies = physics.Bodies;
            var body = bodies[1];
            body.WorldFromBody = new RigidTransform(quaternion.identity, new float3(0f, 9f, 0f));
            bodies[1] = body;
            physics.CollisionWorld.BuildBroadphase(ref physics, 0f, float3.zero, true);
            Assert.That(CreatureTerrainCollision.TryClear(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, 2f, 0f), .5f, 12f, out float3 point), Is.True);
            Assert.That(point.y, Is.EqualTo(2f).Within(.001f));
            point = CreatureTerrainCollision.Move(physics.CollisionWorld, creatures, Entity.Null,
                new float3(-3f, 2f, 0f), new float3(3f, 2f, 0f), .5f, 12f);
            Assert.That(point.x, Is.EqualTo(3f).Within(.001f));
            Assert.That(point.y, Is.EqualTo(2f).Within(.001f));
        }

        [Test]
        public void LocalClearancePushesEmbeddedBodyOutOfSeabed()
        {
            Assert.That(CreatureTerrainCollision.TryClear(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, -.5f, 0f), 1f, 20f, out float3 point), Is.True);
            Assert.That(point.y, Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void OrderedSwimmerDetoursAroundWallAndReachesDestination()
            => CheckDetour(20f);

        [Test]
        public void LowWaterCeilingUsesSideDetourWithoutLeavingWater()
            => CheckDetour(5f);

        [Test]
        public void SwimSystemMoveOrderUsesDetourAndKeepsOriginalWorkTarget()
            => CheckSwimSystem(true);

        [Test]
        public void SwimSystemFreeSwimmingUsesTheSameObstacleAvoidance()
            => CheckSwimSystem(false);

        [Test]
        public void StationaryWorkerRecoversNearOriginalGoalAfterTenSecondsWithoutLosingCargo()
            => CheckSwimSystem(true, true);

        [Test]
        public void StationaryWandererRecoversEvenWhenItsDestinationChanges()
            => CheckSwimSystem(false, true, false, true);

        [Test]
        public void CapturedCreatureDoesNotTeleport()
            => CheckSwimSystem(true, true, true);

        [Test]
        public void RecoveryDestinationHasBodyClearanceAndStaysUnderwater()
        {
            Assert.That(CreatureTerrainCollision.TryRecover(physics.CollisionWorld, creatures, Entity.Null,
                new float3(5f, 3f, 0f), 1f, 5f, out float3 point), Is.True);
            Assert.That(point.y, Is.LessThanOrEqualTo(4f));
            float3 closest = math.clamp(point, new float3(4.5f, 0f, -10f), new float3(5.5f, 10f, 10f));
            Assert.That(math.distance(point, closest), Is.GreaterThanOrEqualTo(1f));
            Assert.That(math.distance(point, new float3(5f, 3f, 0f)), Is.LessThan(4.1f));
        }

        [Test]
        public void RecoveryRejectsDestinationWithoutEnoughWaterDepth()
        {
            Assert.That(CreatureTerrainCollision.TryRecover(physics.CollisionWorld, creatures, Entity.Null,
                new float3(0f, 0f, 0f), 1f, .5f, out _), Is.False);
        }

        private void CheckSwimSystem(bool ordered, bool jammed = false, bool captured = false, bool changingTarget = false)
        {
            var manager = entities.EntityManager;
            Entity runtime = manager.CreateEntity(typeof(WorldEntityRuntimeConfig), typeof(CreatureWaterSurface),
                typeof(PhysicsWorldSingleton));
            manager.SetComponentData(runtime, new CreatureWaterSurface { Height = 20f });
            manager.SetComponentData(runtime, new PhysicsWorldSingleton { PhysicsWorld = physics });
            float3 goal = new(20f, 3f, 0f);
            Entity creature = manager.CreateEntity(typeof(LocalTransform), typeof(CreatureSwim),
                typeof(CreatureRandom), typeof(CreatureAlarm), typeof(CreatureMoveOrder),
                typeof(CreatureCaptured), typeof(WorldEntityActive));
            manager.SetComponentData(creature, LocalTransform.FromPosition(new float3(0f, 3f, 0f)));
            manager.SetComponentData(creature, new CreatureSwim
            {
                BodyRadius = 1f, CruiseSpeed = 3f, TurnSpeedRadians = 5f,
                TargetPoint = goal, NextRepathTime = 100d
            });
            manager.SetComponentData(creature, new CreatureMoveOrder { Target = goal, SpeedMultiplier = 1f, ArriveRadius = .2f });
            manager.SetComponentEnabled<CreatureMoveOrder>(creature, ordered);
            manager.SetComponentEnabled<CreatureCaptured>(creature, captured);
            if (jammed) manager.AddComponentData(creature, new CreatureWorkState { CarriedItemId = 1, CarriedCount = 3 });
            var system = entities.GetOrCreateSystem(typeof(CreatureSwimSystem));
            float3 position = float3.zero;
            for (int frame = 0; frame < 1800; frame++)
            {
                if (jammed) manager.SetComponentData(creature, LocalTransform.FromPosition(new float3(0f, 3f, 0f)));
                if (changingTarget && frame % 120 == 0)
                {
                    goal.z = goal.z > 0f ? -2f : 2f;
                    var swim = manager.GetComponentData<CreatureSwim>(creature);
                    swim.TargetPoint = goal;
                    manager.SetComponentData(creature, swim);
                }
                entities.SetTime(new Unity.Core.TimeData(frame / 60d, 1f / 60f));
                system.Update(entities.Unmanaged);
                position = manager.GetComponentData<LocalTransform>(creature).Position;
                if (jammed && frame < 580) Assert.That(position.x, Is.LessThan(1f), "Recovery must wait for sustained lack of progress");
                if (math.distance(position, goal) <= .25f) break;
            }
            if (captured) Assert.That(position, Is.EqualTo(new float3(0f, 3f, 0f)));
            else Assert.That(math.distance(position, goal), Is.LessThan(.25f));
            if (jammed)
            {
                var cargo = manager.GetComponentData<CreatureWorkState>(creature);
                Assert.That(cargo.CarriedItemId, Is.EqualTo(1));
                Assert.That(cargo.CarriedCount, Is.EqualTo(3));
            }
            Assert.That(manager.GetComponentData<CreatureMoveOrder>(creature).Target, Is.EqualTo(new float3(20f, 3f, 0f)));
        }

        private void CheckDetour(float surface)
        {
            float3 position = new(0f, 3f, 0f);
            float3 goal = new(20f, 3f, 0f);
            var swim = new CreatureSwim();
            bool detoured = false;
            for (int frame = 0; frame < 1800 && math.distance(position, goal) > .2f; frame++)
            {
                float3 target = CreatureTerrainCollision.Navigate(physics.CollisionWorld, creatures,
                    Entity.Null, position, goal, 1f, 3f, surface, frame / 60d, ref swim);
                var step = CreatureSwimMath.Advance(position, quaternion.identity, target, 3f, 5f, 1f / 60f);
                position = CreatureTerrainCollision.Move(physics.CollisionWorld, creatures, Entity.Null,
                    position, step.Position, 1f, surface);
                detoured |= math.abs(position.z) > 1f || position.y > 4f;
                Assert.That(position.y, Is.LessThanOrEqualTo(surface - 1f));
                Assert.That(math.all(math.isfinite(position)), Is.True);
                float3 closest = math.clamp(position, new float3(4.5f, 0f, -10f), new float3(5.5f, 10f, 10f));
                Assert.That(math.distance(position, closest), Is.GreaterThanOrEqualTo(.999f),
                    "Movement must not pass through the wall");
            }
            Assert.That(detoured, Is.True);
            Assert.That(math.distance(position, goal), Is.LessThan(.25f), "A head-on obstacle must not stall a move order");
        }

        [DisableAutoCreation]
        private partial class LookupSystem : SystemBase
        {
            public ComponentLookup<CreatureAlarm> Lookup() => GetComponentLookup<CreatureAlarm>(true);
            protected override void OnUpdate() { }
        }
    }
}
