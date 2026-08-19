using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using WorldBuilder.Entities.Systems;

namespace WorldBuilder.Entities.Tests
{
    public sealed class WorldEntitySystemTests
    {
        private World world;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            world = new World("WorldBuilder.Entities.Tests");
            entityManager = world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (world != null && world.IsCreated) world.Dispose();
        }

        [Test]
        public void ChunkOwnershipSystem_UpdatesNegativeChunkAndRegion()
        {
            Entity runtime = entityManager.CreateEntity(typeof(WorldEntityRuntimeConfig));
            entityManager.SetComponentData(runtime, new WorldEntityRuntimeConfig
            {
                ChunkSize = 128f,
                ChunksPerRegion = 4,
                WorldOrigin = float3.zero
            });
            Entity entity = entityManager.CreateEntity(typeof(LocalTransform), typeof(WorldEntityChunk),
                typeof(WorldEntityTrackChunk));
            entityManager.SetComponentData(entity, LocalTransform.FromPosition(new float3(-129f, 0f, -1f)));

            world.CreateSystem<WorldEntityChunkOwnershipSystem>().Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();

            WorldEntityChunk result = entityManager.GetComponentData<WorldEntityChunk>(entity);
            Assert.That(result.Chunk, Is.EqualTo(new int2(-2, -1)));
            Assert.That(result.Region, Is.EqualTo(new int2(-1, -1)));
        }

        [Test]
        public void VelocitySystem_OnlyMovesEnabledEntities()
        {
            world.SetTime(new TimeData(10d, 0.5f));
            Entity active = CreateMovingEntity(true);
            Entity inactive = CreateMovingEntity(false);

            world.CreateSystem<WorldEntityVelocitySystem>().Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();

            Assert.That(entityManager.GetComponentData<LocalTransform>(active).Position.x,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<LocalTransform>(inactive).Position.x,
                Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void SpawnSystem_InstantiatesCatalogPrefabAndAssignsRuntimeIdentity()
        {
            Entity runtime = entityManager.CreateEntity(typeof(WorldEntityRuntimeConfig));
            entityManager.SetComponentData(runtime, new WorldEntityRuntimeConfig
            {
                ChunkSize = 128f,
                ChunksPerRegion = 4,
                NextRuntimeId = 40
            });
            Entity prefab = entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform),
                typeof(WorldEntityIdentity), typeof(WorldEntityDescriptor), typeof(WorldEntityChunk),
                typeof(WorldEntityTrackChunk), typeof(WorldEntityActive));
            entityManager.SetComponentData(prefab,
                new WorldEntityDescriptor { PrefabId = 7, Kind = WorldEntityKind.Resource });
            entityManager.AddBuffer<WorldEntityPrefabElement>(runtime);
            entityManager.AddBuffer<WorldEntitySpawnRequest>(runtime);
            DynamicBuffer<WorldEntityPrefabElement> catalog =
                entityManager.GetBuffer<WorldEntityPrefabElement>(runtime);
            DynamicBuffer<WorldEntitySpawnRequest> requests =
                entityManager.GetBuffer<WorldEntitySpawnRequest>(runtime);
            catalog.Add(new WorldEntityPrefabElement { PrefabId = 7, Prefab = prefab });
            requests.Add(new WorldEntitySpawnRequest
            {
                PrefabId = 7,
                Position = new float3(4f, 5f, 6f),
                Rotation = quaternion.identity,
                UniformScale = 2f
            });

            world.CreateSystem<WorldEntitySpawnSystem>().Update(world.Unmanaged);

            EntityQuery instances = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldEntityIdentity>(), ComponentType.Exclude<Prefab>());
            Assert.That(instances.CalculateEntityCount(), Is.EqualTo(1));
            Entity instance = instances.GetSingletonEntity();
            Assert.That(entityManager.GetComponentData<WorldEntityIdentity>(instance).Low, Is.EqualTo(41));
            LocalTransform transform = entityManager.GetComponentData<LocalTransform>(instance);
            Assert.That(transform.Position, Is.EqualTo(new float3(4f, 5f, 6f)));
            Assert.That(transform.Scale, Is.EqualTo(2f));
        }

        [Test]
        public void RegionActivationSystem_UsesLoadedRegionBuffer()
        {
            Entity runtime = entityManager.CreateEntity(typeof(WorldEntityRuntimeConfig));
            entityManager.SetComponentData(runtime, new WorldEntityRuntimeConfig { RegionRevision = 1 });
            DynamicBuffer<WorldEntityLoadedRegion> regions =
                entityManager.AddBuffer<WorldEntityLoadedRegion>(runtime);
            Entity entity = entityManager.CreateEntity(typeof(WorldEntityChunk), typeof(WorldEntityDescriptor),
                typeof(WorldEntityActive));
            entityManager.SetComponentData(entity, new WorldEntityChunk { Region = new int2(2, -1) });
            entityManager.SetComponentData(entity,
                new WorldEntityDescriptor { Flags = WorldEntityFlags.RegionStreamed });

            SystemHandle system = world.CreateSystem<WorldEntityRegionActivationSystem>();
            system.Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();
            Assert.That(entityManager.IsComponentEnabled<WorldEntityActive>(entity), Is.False);

            regions = entityManager.GetBuffer<WorldEntityLoadedRegion>(runtime);
            regions.Add(new WorldEntityLoadedRegion { Coordinate = new int2(2, -1) });
            WorldEntityRuntimeConfig config = entityManager.GetComponentData<WorldEntityRuntimeConfig>(runtime);
            config.RegionRevision++;
            entityManager.SetComponentData(runtime, config);
            system.Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();
            Assert.That(entityManager.IsComponentEnabled<WorldEntityActive>(entity), Is.True);
        }

        private Entity CreateMovingEntity(bool enabled)
        {
            Entity entity = entityManager.CreateEntity(typeof(LocalTransform), typeof(WorldEntityVelocity),
                typeof(WorldEntityActive));
            entityManager.SetComponentData(entity, LocalTransform.FromPosition(float3.zero));
            entityManager.SetComponentData(entity, new WorldEntityVelocity { Value = new float3(4f, 0f, 0f) });
            entityManager.SetComponentEnabled<WorldEntityActive>(entity, enabled);
            return entity;
        }
    }
}
