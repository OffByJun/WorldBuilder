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
        public void DepletingRestoredMineral_RecreatesRespawnStateAndResetsPieces()
        {
            world.SetTime(new TimeData(5d, .1f));
            Entity target = entityManager.CreateEntity(typeof(ResourceNode), typeof(LocalTransform),
                typeof(ResourceNodePieces), typeof(ResourcePieceState));
            entityManager.SetComponentData(target, LocalTransform.Identity);
            entityManager.SetComponentData(target, new ResourceNodePieces { Kind = 0, MinimumDropMultiplier = 1, MaximumDropMultiplier = 1 });
            var pieces = new ResourcePieceState();
            pieces.Initialize(5, 0f);
            pieces.Health[2] = .5f;
            entityManager.SetComponentData(target, pieces);
            entityManager.SetComponentData(target, new ResourceNode
            {
                Health = 10, MaxHealth = 100, AllowedMethods = HarvestMethod.Pickaxe,
                RequiredToolItemId = -1, RespawnSeconds = .05f
            });
            entityManager.GetBuffer<ResourceHarvestRequest>(runtime).Add(new ResourceHarvestRequest
            {
                Target = target, Method = HarvestMethod.Pickaxe, Damage = 100, HasTargetPiece = 1, PieceIndex = 2
            });
            world.CreateSystem<ResourceHarvestSystem>().Update(world.Unmanaged);
            Assert.That(entityManager.HasComponent<ResourceRespawnState>(target), Is.True);
            Assert.That(entityManager.HasComponent<Disabled>(target), Is.True);
            world.CreateSystem<ResourceRespawnSystem>().Update(world.Unmanaged);
            Assert.That(entityManager.HasComponent<Disabled>(target), Is.False);
            Assert.That(entityManager.GetComponentData<ResourceNode>(target).Health, Is.EqualTo(100));
            Assert.That(entityManager.GetComponentData<ResourcePieceState>(target).Health.Length, Is.Zero);
        }

        [Test]
        public void TargetedMineral_DamagesOnlyHitPieceAndGrantsInventoryWithOverflow()
        {
            world.SetTime(new TimeData(5d, .1f));
            Entity target = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(LocalTransform), typeof(ResourceNodePieces), typeof(ResourcePieceState));
            entityManager.SetComponentData(target, LocalTransform.Identity);
            entityManager.SetComponentData(target, new ResourceNodePieces { Kind = 0, MinimumDropMultiplier = 1, MaximumDropMultiplier = 1 });
            entityManager.SetComponentData(target, new ResourceNode
            {
                Health = 100, MaxHealth = 100, AllowedMethods = HarvestMethod.Pickaxe,
                RequiredToolItemId = 6, RespawnSeconds = 10, DroppedItemPrefabId = 9
            });
            entityManager.AddBuffer<ResourceDrop>(target).Add(new ResourceDrop
                { ItemId = 100, MinimumCount = 2, MaximumCount = 2, Probability = 1 });
            SystemHandle system = world.CreateSystem<ResourceHarvestSystem>();
            for (int hit = 0; hit < 3; hit++)
            {
                entityManager.GetBuffer<ResourceHarvestRequest>(runtime).Add(new ResourceHarvestRequest
                {
                    Target = target, Method = HarvestMethod.Pickaxe, ToolItemId = 6,
                    Damage = 40, HasTargetPiece = 1, PieceIndex = 1, GrantToInventory = 1
                });
                system.Update(world.Unmanaged);
                var pieces = entityManager.GetComponentData<ResourcePieceState>(target);
                Assert.That(pieces.Health[0], Is.EqualTo(1));
                Assert.That(pieces.Health[2], Is.EqualTo(1));
                Assert.That(pieces.Health[1], Is.EqualTo(math.max(0f, 1f - .4f * (hit + 1))).Within(.0001f));
                Assert.That(entityManager.GetBuffer<InventoryGrantRequest>(runtime).Length, Is.EqualTo(hit == 2 ? 1 : 0));
                Assert.That(entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime).Length, Is.Zero);
            }
            World previous = World.DefaultGameObjectInjectionWorld;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                int accepted = 0;
                Assert.That(WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) => { accepted += 1; return 1; }), Is.EqualTo(1));
                Assert.That(accepted, Is.EqualTo(1));
                var drops = entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime);
                Assert.That(drops.Length, Is.EqualTo(1));
                Assert.That(drops[0].Count, Is.EqualTo(1));
                Assert.That(drops[0].ItemId, Is.EqualTo(100));
                Assert.That(entityManager.GetBuffer<InventoryGrantResult>(runtime).Length, Is.Zero);
                Assert.That(WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) => count), Is.Zero);
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
        }

        [TestCase(0)]
        [TestCase(2)]
        public void HarvestInventory_EmptyOrFullCapacityDropsOnlyUnacceptedCount(int capacity)
        {
            entityManager.GetBuffer<InventoryGrantRequest>(runtime).Add(new InventoryGrantRequest
            {
                ItemId = 100, RequestedCount = 2, IsHarvest = 1,
                HarvestDrop = new ResourceDropSpawnRequest { ItemId = 100, Count = 2, PrefabId = 9, Position = new float3(3, 4, 5) }
            });
            World previous = World.DefaultGameObjectInjectionWorld;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) => capacity);
                var drops = entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime);
                Assert.That(drops.Length, Is.EqualTo(capacity == 2 ? 0 : 1));
                if (drops.Length > 0)
                {
                    Assert.That(drops[0].Count, Is.EqualTo(2));
                    Assert.That(drops[0].Position, Is.EqualTo(new float3(3, 4, 5)));
                }
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
        }

        [Test]
        public void MineralPieces_RequireSeveralHitsAndDropOnlyWhenPieceBreaks()
        {
            world.SetTime(new TimeData(5d, .1f));
            Entity target = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(LocalTransform), typeof(ResourceNodePieces));
            entityManager.SetComponentData(target, LocalTransform.Identity);
            entityManager.SetComponentData(target, new ResourceNodePieces { Kind = 0, MinimumDropMultiplier = 1, MaximumDropMultiplier = 1 });
            entityManager.SetComponentData(target, new ResourceNode
            {
                Health = 100, MaxHealth = 100, AllowedMethods = HarvestMethod.Pickaxe,
                RequiredToolItemId = -1, RespawnSeconds = 10, DroppedItemPrefabId = 9
            });
            entityManager.AddBuffer<ResourceDrop>(target).Add(new ResourceDrop
            {
                ItemId = 4, MinimumCount = 1, MaximumCount = 1, Probability = 1
            });
            SystemHandle system = world.CreateSystem<ResourceHarvestSystem>();
            int seed = ResourceNodePieces.PositionSeed(float3.zero, 0);
            int count = ResourceNodePieces.RemainingCount(0, seed, 1);
            for (int hit = 1; hit <= count * 4; hit++)
            {
                entityManager.GetBuffer<ResourceHarvestRequest>(runtime).Add(new ResourceHarvestRequest
                {
                    RequestId = (uint)hit, Target = target, Method = HarvestMethod.Pickaxe, Damage = 25
                });
                system.Update(world.Unmanaged);
                Assert.That(entityManager.GetBuffer<ResourceDropSpawnRequest>(runtime).Length, Is.EqualTo(hit / 4));
                if (hit < 4)
                    Assert.That(ResourceNodePieces.RemainingCount(0, seed,
                        entityManager.GetComponentData<ResourceNode>(target).Health / 100), Is.EqualTo(count));
            }
            Assert.That(entityManager.HasComponent<Disabled>(target), Is.True);
            Assert.That(entityManager.GetComponentData<ResourceNode>(target).Health, Is.Zero);
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
        public void PickupTransfers_SurviveStructuralChangesAndDeferNewRequests()
        {
            World previous = World.DefaultGameObjectInjectionWorld;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                entityManager.GetBuffer<InventoryGrantRequest>(runtime).Add(new InventoryGrantRequest
                    { RequestId = 1, ItemId = 8, RequestedCount = 3 });
                entityManager.GetBuffer<InventoryGrantRequest>(runtime).Add(new InventoryGrantRequest
                    { RequestId = 2, ItemId = 4, RequestedCount = 2 });
                int calls = 0;
                int processed = WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) =>
                {
                    if (++calls == 1)
                    {
                        entityManager.AddComponent<LocalTransform>(runtime);
                        entityManager.GetBuffer<InventoryGrantRequest>(runtime).Add(new InventoryGrantRequest
                            { RequestId = 3, ItemId = 9, RequestedCount = 1 });
                    }
                    return count - 1;
                });
                Assert.That(processed, Is.EqualTo(2));
                Assert.That(calls, Is.EqualTo(2));
                var results = entityManager.GetBuffer<InventoryGrantResult>(runtime);
                Assert.That(results.Length, Is.EqualTo(2));
                Assert.That(results[0].AcceptedCount, Is.EqualTo(2));
                Assert.That(results[1].AcceptedCount, Is.EqualTo(1));
                Assert.That(entityManager.GetBuffer<InventoryGrantRequest>(runtime)[0].RequestId, Is.EqualTo(3));
                Assert.That(WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) => count), Is.EqualTo(1));
                Assert.That(WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) => count), Is.Zero);
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
        }

        [Test]
        public void HarvestResultCallbacks_SurviveStructuralChangesAndPreserveNewResults()
        {
            World previous = World.DefaultGameObjectInjectionWorld;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                entityManager.GetBuffer<ResourceHarvestResult>(runtime).Add(new ResourceHarvestResult { RequestId = 1 });
                entityManager.GetBuffer<ResourceHarvestResult>(runtime).Add(new ResourceHarvestResult { RequestId = 2 });
                int calls = 0;
                int drained = WorldResourceCommandQueue.DrainHarvestResults(result =>
                {
                    if (++calls == 1)
                    {
                        entityManager.AddComponent<LocalTransform>(runtime);
                        entityManager.GetBuffer<ResourceHarvestResult>(runtime).Add(new ResourceHarvestResult { RequestId = 3 });
                    }
                });
                Assert.That(drained, Is.EqualTo(2));
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(entityManager.GetBuffer<ResourceHarvestResult>(runtime)[0].RequestId, Is.EqualTo(3));
                Assert.That(WorldResourceCommandQueue.DrainHarvestResults(result => calls++), Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(3));
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
        }

        [Test]
        public void Pickup_RepeatedClicksGrantOnceAndRemoveTheFullDropHierarchy()
        {
            World previous = World.DefaultGameObjectInjectionWorld;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                Entity item = entityManager.CreateEntity(typeof(DroppedItem), typeof(DroppedItemPendingPickup));
                Entity child = entityManager.CreateEntity();
                entityManager.AddBuffer<LinkedEntityGroup>(item).Add(new LinkedEntityGroup { Value = item });
                entityManager.GetBuffer<LinkedEntityGroup>(item).Add(new LinkedEntityGroup { Value = child });
                entityManager.SetComponentData(item, new DroppedItem { ItemId = 8, Count = 3 });
                entityManager.SetComponentEnabled<DroppedItemPendingPickup>(item, false);
                Assert.That(WorldResourceCommandQueue.TryPickup(item, out _), Is.True);
                Assert.That(WorldResourceCommandQueue.TryPickup(item, out _), Is.True);
                world.CreateSystem<DroppedItemPickupRequestSystem>().Update(world.Unmanaged);
                int received = 0;
                Assert.That(WorldResourceCommandQueue.ProcessInventoryTransfers((id, count) =>
                {
                    received += count;
                    return count;
                }), Is.EqualTo(1));
                world.CreateSystem<InventoryGrantResultSystem>().Update(world.Unmanaged);
                Assert.That(received, Is.EqualTo(3));
                Assert.That(entityManager.Exists(item), Is.False);
                Assert.That(entityManager.Exists(child), Is.False);
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
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

        [Test]
        public void RespawnSystem_WaitsNearBuildingAndResumesWhenBuildingLeaves()
        {
            world.SetTime(new TimeData(2d, .5f));
            Entity building = entityManager.CreateEntity(typeof(ResourceSpawnExclusion));
            entityManager.SetComponentData(building, new ResourceSpawnExclusion
            {
                Minimum = new float3(-5f), Maximum = new float3(5f)
            });
            Entity node = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(LocalTransform), typeof(Disabled));
            entityManager.SetComponentData(node, LocalTransform.FromPosition(new float3(4f, 0f, 0f)));
            entityManager.SetComponentData(node, new ResourceNode { MaxHealth = 50f });
            Entity outside = entityManager.Instantiate(node);
            entityManager.SetComponentData(outside, LocalTransform.FromPosition(new float3(8f, 0f, 0f)));
            var system = world.CreateSystem<ResourceRespawnSystem>();
            system.Update(world.Unmanaged);

            Assert.That(entityManager.HasComponent<Disabled>(node), Is.True);
            Assert.That(entityManager.GetComponentData<ResourceNode>(node).Health, Is.Zero);
            Assert.That(entityManager.HasComponent<Disabled>(outside), Is.False);

            entityManager.SetComponentData(building, new ResourceSpawnExclusion
            {
                Minimum = new float3(20f), Maximum = new float3(30f)
            });
            world.SetTime(new TimeData(5d, 3f));
            system.Update(world.Unmanaged);
            Assert.That(entityManager.HasComponent<Disabled>(node), Is.False);
            Assert.That(entityManager.GetComponentData<ResourceNode>(node).Health, Is.EqualTo(50f));
        }

        [Test]
        public void RespawnSystem_UsesIndividualResourceRateAndZeroPreventsRegrowth()
        {
            world.SetTime(new TimeData(2d, .5f));
            Entity node = entityManager.CreateEntity(typeof(ResourceNode), typeof(ResourceRespawnState),
                typeof(WorldEntitySpawnRate), typeof(Disabled));
            entityManager.SetComponentData(node, new ResourceNode { MaxHealth = 50f });
            entityManager.SetComponentData(node, new ResourceRespawnState { RemainingSeconds = 1f });
            entityManager.SetComponentData(node, new WorldEntitySpawnRate { Value = 0f });
            var system = world.CreateSystem<ResourceRespawnSystem>();
            system.Update(world.Unmanaged);
            Assert.That(entityManager.GetComponentData<ResourceRespawnState>(node).RemainingSeconds, Is.EqualTo(1f));
            Assert.That(entityManager.HasComponent<Disabled>(node), Is.True);
            entityManager.SetComponentData(node, new WorldEntitySpawnRate { Value = 2f });
            system.Update(world.Unmanaged);
            Assert.That(entityManager.HasComponent<Disabled>(node), Is.False);
            Assert.That(entityManager.GetComponentData<ResourceNode>(node).Health, Is.EqualTo(50f));
        }
    }
}
