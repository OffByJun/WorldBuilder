using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using WorldBuilder.Entities.Resources;
using WorldBuilder.Entities.Resources.Systems;

namespace WorldBuilder.Entities.Tests
{
    public sealed class WorldResourceSystemTests
    {
        private World world;
        private EntityManager entityManager;
        private Entity runtime;

        [SetUp]
        public void SetUp()
        {
            world = new World("WorldBuilder.Resource.Tests");
            entityManager = world.EntityManager;
            runtime = entityManager.CreateEntity(typeof(WorldEntityRuntimeConfig));
            entityManager.SetComponentData(runtime, new WorldEntityRuntimeConfig
            {
                ChunkSize = 128f,
                ChunksPerRegion = 4
            });
            entityManager.AddBuffer<ResourceHarvestRequest>(runtime);
            entityManager.AddBuffer<ResourceHarvestResult>(runtime);
            entityManager.AddBuffer<ResourceDropSpawnRequest>(runtime);
            entityManager.AddBuffer<DroppedItemPickupRequest>(runtime);
            entityManager.AddBuffer<InventoryGrantRequest>(runtime);
            entityManager.AddBuffer<InventoryGrantResult>(runtime);
        }

        [TearDown]
        public void TearDown()
        {
            if (world != null && world.IsCreated) world.Dispose();
        }

        [Test]
        public void Harvest_ValidatesTool_AppliesDamage_AndCreatesDropsOnDepletion()
        {
            world.SetTime(new TimeData(5d, 0.1f));
            Entity node = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(LocalTransform));
            entityManager.SetComponentData(node, new ResourceNode
            {
                MaxHealth = 20f,
                Health = 20f,
                AllowedMethods = HarvestMethod.Pickaxe,
                RequiredToolItemId = 42,
                MinimumToolTier = 2,
                MinimumToolPower = 3f,
                RespawnSeconds = 10f,
                DroppedItemPrefabId = 9,
                RandomSeed = 123
            });
            entityManager.SetComponentData(node, LocalTransform.FromPosition(new float3(1f, 2f, 3f)));
            DynamicBuffer<ResourceDrop> table = entityManager.AddBuffer<ResourceDrop>(node);
            table.Add(new ResourceDrop
            {
                ItemId = 100,
                MinimumCount = 2,
                MaximumCount = 2,
                Probability = 1f
            });

            DynamicBuffer<ResourceHarvestRequest> requests = entityManager.GetBuffer<ResourceHarvestRequest>(runtime);
            requests.Add(new ResourceHarvestRequest
            {
                RequestId = 1,
                Target = node,
                Method = HarvestMethod.Hand,
                ToolItemId = -1,
                Damage = 100f
            });
            SystemHandle system = world.CreateSystem<ResourceHarvestSystem>();
            system.Update(world.Unmanaged);
            DynamicBuffer<ResourceHarvestResult> results = entityManager.GetBuffer<ResourceHarvestResult>(runtime);
            Assert.That(results[0].Failure, Is.EqualTo(HarvestFailureReason.WrongMethod));
            Assert.That(entityManager.GetComponentData<ResourceNode>(node).Health, Is.EqualTo(20f));

            results.Clear();
            requests = entityManager.GetBuffer<ResourceHarvestRequest>(runtime);
            requests.Add(new ResourceHarvestRequest
            {
                RequestId = 2,
                Target = node,
                Method = HarvestMethod.Pickaxe,
                ToolItemId = 42,
                ToolTier = 2,
                ToolPower = 3f,
                Damage = 20f
            });
            system.Update(world.Unmanaged);

            results = entityManager.GetBuffer<ResourceHarvestResult>(runtime);
            Assert.That(results[0].Failure, Is.EqualTo(HarvestFailureReason.None));
            Assert.That(results[0].Depleted, Is.EqualTo(1));
            Assert.That(entityManager.HasComponent<Disabled>(node), Is.True);
            DynamicBuffer<ResourceDropSpawnRequest> drops =
                entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime);
            Assert.That(drops.Length, Is.EqualTo(1));
            Assert.That(drops[0].ItemId, Is.EqualTo(100));
            Assert.That(drops[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void Pickup_OnlySubtractsInventoryAcceptedCount()
        {
            Entity item = entityManager.CreateEntity(typeof(DroppedItem), typeof(DroppedItemPendingPickup));
            entityManager.SetComponentData(item, new DroppedItem { ItemId = 7, Count = 5 });
            entityManager.SetComponentEnabled<DroppedItemPendingPickup>(item, false);
            entityManager.GetBuffer<DroppedItemPickupRequest>(runtime).Add(new DroppedItemPickupRequest
            {
                RequestId = 10,
                Target = item
            });

            world.CreateSystem<DroppedItemPickupRequestSystem>().Update(world.Unmanaged);
            DynamicBuffer<InventoryGrantRequest> grants = entityManager.GetBuffer<InventoryGrantRequest>(runtime);
            Assert.That(grants.Length, Is.EqualTo(1));
            Assert.That(grants[0].RequestedCount, Is.EqualTo(5));
            Assert.That(entityManager.IsComponentEnabled<DroppedItemPendingPickup>(item), Is.True);

            grants.Clear();
            entityManager.GetBuffer<InventoryGrantResult>(runtime).Add(new InventoryGrantResult
            {
                RequestId = 10,
                Target = item,
                AcceptedCount = 2
            });
            world.CreateSystem<InventoryGrantResultSystem>().Update(world.Unmanaged);
            Assert.That(entityManager.GetComponentData<DroppedItem>(item).Count, Is.EqualTo(3));
            Assert.That(entityManager.IsComponentEnabled<DroppedItemPendingPickup>(item), Is.False);
        }

        [Test]
        public void RespawnSystem_RestoresHealthAndRemovesDisabled()
        {
            world.SetTime(new TimeData(2d, 0.5f));
            Entity node = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(Disabled));
            entityManager.SetComponentData(node, new ResourceNode { MaxHealth = 50f, Health = 0f });
            entityManager.SetComponentData(node, new ResourceRespawnState { RemainingSeconds = 0.25f });

            world.CreateSystem<ResourceRespawnSystem>().Update(world.Unmanaged);

            Assert.That(entityManager.HasComponent<Disabled>(node), Is.False);
            Assert.That(entityManager.GetComponentData<ResourceNode>(node).Health, Is.EqualTo(50f));
        }
    }
}
