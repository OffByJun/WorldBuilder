using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CropGrowthSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CropPlot>();
        }

        public void OnUpdate(ref SystemState state)
        {
            double elapsed = SystemAPI.Time.ElapsedTime;
            foreach (var (plot, site, ready) in
                     SystemAPI.Query<RefRW<CropPlot>, RefRW<CreatureWorkSite>,
                         EnabledRefRW<CreatureWorkSiteReady>>().WithOptions(
                         EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (site.ValueRO.State != CreatureWorkSiteState.Growing) continue;
                if (plot.ValueRO.ReadyTime <= 0d)
                {
                    plot.ValueRW.ReadyTime = elapsed + math.max(0.1f, plot.ValueRO.GrowSeconds);
                    continue;
                }
                if (elapsed < plot.ValueRO.ReadyTime) continue;

                site.ValueRW.State = CreatureWorkSiteState.Ready;
                site.ValueRW.Claimant = Entity.Null;
                ready.ValueRW = true;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CropGrowthSystem))]
    public partial struct CreatureWorkSiteRefreshSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureWorkSiteRefresh>();
        }

        public void OnUpdate(ref SystemState state)
        {
            double elapsed = SystemAPI.Time.ElapsedTime;
            foreach (var (refresh, site, ready) in
                     SystemAPI.Query<RefRW<CreatureWorkSiteRefresh>, RefRW<CreatureWorkSite>,
                         EnabledRefRW<CreatureWorkSiteReady>>().WithOptions(
                         EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (site.ValueRO.State != CreatureWorkSiteState.Spent) continue;
                if (refresh.ValueRO.NextReadyTime <= 0d)
                {
                    refresh.ValueRW.NextReadyTime =
                        elapsed + math.max(0.1f, refresh.ValueRO.RefreshSeconds);
                    continue;
                }
                if (elapsed < refresh.ValueRO.NextReadyTime) continue;

                refresh.ValueRW.NextReadyTime = 0d;
                site.ValueRW.State = CreatureWorkSiteState.Ready;
                site.ValueRW.Claimant = Entity.Null;
                ready.ValueRW = true;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureWorkSiteRefreshSystem))]
    public partial struct CreatureWorkAssignmentSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureWorkSite>();
            state.RequireForUpdate<CreatureSettlement>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;
            NativeList<Entity> openSites = new NativeList<Entity>(Allocator.Temp);

            foreach (var (site, siteEntity) in
                     SystemAPI.Query<RefRO<CreatureWorkSite>>().WithAll<CreatureWorkSiteReady>()
                         .WithEntityAccess())
            {
                if (site.ValueRO.State != CreatureWorkSiteState.Ready) continue;
                if (site.ValueRO.Claimant != Entity.Null) continue;
                openSites.Add(siteEntity);
            }

            if (openSites.IsEmpty)
            {
                openSites.Dispose();
                return;
            }

            foreach (var (settlement, aptitude, work, worker) in
                     SystemAPI.Query<RefRO<CreatureSettlement>, RefRO<CreatureRoleAptitude>,
                         RefRW<CreatureWorkState>>().WithAll<CreatureSettled>().WithEntityAccess())
            {
                if (work.ValueRO.Phase != CreatureWorkPhase.Idle) continue;

                for (int i = 0; i < openSites.Length; i++)
                {
                    Entity siteEntity = openSites[i];
                    CreatureWorkSite site = entityManager.GetComponentData<CreatureWorkSite>(siteEntity);
                    if (site.Claimant != Entity.Null) continue;
                    if ((aptitude.ValueRO.Roles & site.RequiredRole) == CreatureRole.None) continue;
                    if (site.Habitat != Entity.Null && site.Habitat != settlement.ValueRO.Habitat) continue;

                    site.Claimant = worker;
                    site.State = CreatureWorkSiteState.Claimed;
                    entityManager.SetComponentData(siteEntity, site);
                    entityManager.SetComponentEnabled<CreatureWorkSiteReady>(siteEntity, false);

                    work.ValueRW.Site = siteEntity;
                    work.ValueRW.Phase = CreatureWorkPhase.MoveToSite;
                    work.ValueRW.CarriedItemId = -1;
                    work.ValueRW.CarriedCount = 0;
                    openSites.RemoveAtSwapBack(i);
                    break;
                }

                if (openSites.IsEmpty) break;
            }

            openSites.Dispose();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureWorkAssignmentSystem))]
    public partial struct CreatureWorkExecutionSystem : ISystem
    {
        private ComponentLookup<LocalTransform> transformLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureWorkState>();
            transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;
            double elapsed = SystemAPI.Time.ElapsedTime;
            transformLookup.Update(ref state);
            Entity runtime = SystemAPI.HasSingleton<CreatureWorkCompletedEvent>()
                ? SystemAPI.GetSingletonEntity<CreatureWorkCompletedEvent>()
                : Entity.Null;

            foreach (var (work, settlement, transform, order, worker) in
                     SystemAPI.Query<RefRW<CreatureWorkState>, RefRO<CreatureSettlement>, RefRO<LocalTransform>,
                         EnabledRefRW<CreatureMoveOrder>>().WithAll<CreatureSettled>().WithEntityAccess())
            {
                switch (work.ValueRO.Phase)
                {
                    case CreatureWorkPhase.MoveToSite:
                        TickMoveToSite(entityManager, work, settlement, transform, order, worker, elapsed);
                        break;
                    case CreatureWorkPhase.Interact:
                        TickInteract(entityManager, transformLookup, runtime, work, settlement,
                            transform, order, worker, elapsed);
                        break;
                    case CreatureWorkPhase.MoveToDelivery:
                        TickMoveToDelivery(entityManager, work, settlement, transform, order, worker);
                        break;
                    case CreatureWorkPhase.Deposit:
                        TickDeposit(entityManager, work, settlement, order, worker, runtime);
                        break;
                    case CreatureWorkPhase.Return:
                        TickReturn(work, order);
                        break;
                }
            }
        }

        private static void TickMoveToSite(EntityManager entityManager, RefRW<CreatureWorkState> work,
            RefRO<CreatureSettlement> settlement, RefRO<LocalTransform> transform,
            EnabledRefRW<CreatureMoveOrder> order, Entity worker, double elapsed)
        {
            Entity siteEntity = work.ValueRO.Site;
            if (!IsValidSite(entityManager, siteEntity, worker))
            {
                Abort(work, order);
                return;
            }

            CreatureWorkSite site = entityManager.GetComponentData<CreatureWorkSite>(siteEntity);
            float3 target = entityManager.GetComponentData<LocalTransform>(siteEntity).Position;
            entityManager.SetComponentData(worker, new CreatureMoveOrder
            {
                Target = target,
                SpeedMultiplier = math.max(1f, settlement.ValueRO.WorkSpeed),
                ArriveRadius = math.max(0.5f, site.InteractRadius)
            });
            order.ValueRW = true;

            if (math.distance(transform.ValueRO.Position, target) > math.max(0.5f, site.InteractRadius)) return;
            work.ValueRW.Phase = CreatureWorkPhase.Interact;
            work.ValueRW.PhaseEndTime = elapsed +
                math.max(0.1f, site.WorkSeconds / math.max(0.1f, settlement.ValueRO.WorkSpeed));
        }

        private static void TickInteract(EntityManager entityManager,
            in ComponentLookup<LocalTransform> transforms, Entity runtime, RefRW<CreatureWorkState> work,
            RefRO<CreatureSettlement> settlement, RefRO<LocalTransform> transform,
            EnabledRefRW<CreatureMoveOrder> order, Entity worker, double elapsed)
        {
            Entity siteEntity = work.ValueRO.Site;
            if (!IsValidSite(entityManager, siteEntity, worker))
            {
                Abort(work, order);
                return;
            }

            order.ValueRW = false;
            if (elapsed < work.ValueRO.PhaseEndTime) return;

            CreatureWorkSite site = entityManager.GetComponentData<CreatureWorkSite>(siteEntity);
            work.ValueRW.CarriedItemId = site.OutputItemId;
            work.ValueRW.CarriedCount = math.min(math.max(0, site.OutputCount),
                math.max(1, settlement.ValueRO.CarryCapacity));

            site.State = CreatureWorkSiteState.Spent;
            site.Claimant = Entity.Null;
            entityManager.SetComponentData(siteEntity, site);

            if (entityManager.HasComponent<CropPlot>(siteEntity))
            {
                CropPlot plot = entityManager.GetComponentData<CropPlot>(siteEntity);
                if (plot.AutoReplant != 0)
                {
                    plot.ReadyTime = elapsed + math.max(0.1f, plot.GrowSeconds);
                    entityManager.SetComponentData(siteEntity, plot);
                    site.State = CreatureWorkSiteState.Growing;
                    entityManager.SetComponentData(siteEntity, site);
                }
            }

            work.ValueRW.Delivery = FindStorage(entityManager, transforms, runtime,
                settlement.ValueRO.Habitat, transform.ValueRO.Position);
            work.ValueRW.Phase = work.ValueRO.Delivery == Entity.Null
                ? CreatureWorkPhase.Return
                : CreatureWorkPhase.MoveToDelivery;
        }

        private static void TickMoveToDelivery(EntityManager entityManager, RefRW<CreatureWorkState> work,
            RefRO<CreatureSettlement> settlement, RefRO<LocalTransform> transform,
            EnabledRefRW<CreatureMoveOrder> order, Entity worker)
        {
            Entity storage = work.ValueRO.Delivery;
            if (storage == Entity.Null || !entityManager.Exists(storage) ||
                !entityManager.HasComponent<CreatureStorage>(storage) ||
                !entityManager.HasComponent<LocalTransform>(storage))
            {
                work.ValueRW.Phase = CreatureWorkPhase.Return;
                return;
            }

            float3 target = entityManager.GetComponentData<LocalTransform>(storage).Position;
            entityManager.SetComponentData(worker, new CreatureMoveOrder
            {
                Target = target,
                SpeedMultiplier = math.max(1f, settlement.ValueRO.WorkSpeed),
                ArriveRadius = 1.5f
            });
            order.ValueRW = true;

            if (math.distance(transform.ValueRO.Position, target) <= 1.5f)
                work.ValueRW.Phase = CreatureWorkPhase.Deposit;
        }

        private static void TickDeposit(EntityManager entityManager, RefRW<CreatureWorkState> work,
            RefRO<CreatureSettlement> settlement, EnabledRefRW<CreatureMoveOrder> order, Entity worker,
            Entity runtime)
        {
            order.ValueRW = false;
            Entity storage = work.ValueRO.Delivery;
            int accepted = 0;
            if (storage != Entity.Null && entityManager.Exists(storage) &&
                entityManager.HasBuffer<CreatureStorageSlot>(storage))
            {
                CreatureStorage inventory = entityManager.GetComponentData<CreatureStorage>(storage);
                DynamicBuffer<CreatureStorageSlot> slots = entityManager.GetBuffer<CreatureStorageSlot>(storage);
                accepted = Deposit(slots, inventory, work.ValueRO.CarriedItemId, work.ValueRO.CarriedCount);
            }

            if (runtime != Entity.Null && entityManager.HasBuffer<CreatureWorkCompletedEvent>(runtime))
            {
                CreatureRoleAptitude aptitude = entityManager.GetComponentData<CreatureRoleAptitude>(worker);
                entityManager.GetBuffer<CreatureWorkCompletedEvent>(runtime).Add(new CreatureWorkCompletedEvent
                {
                    Worker = worker,
                    Site = work.ValueRO.Site,
                    Storage = storage,
                    Role = aptitude.Roles,
                    ItemId = work.ValueRO.CarriedItemId,
                    Count = work.ValueRO.CarriedCount,
                    Accepted = accepted
                });
            }

            work.ValueRW.CarriedItemId = -1;
            work.ValueRW.CarriedCount = 0;
            work.ValueRW.Site = Entity.Null;
            work.ValueRW.Delivery = Entity.Null;
            work.ValueRW.Phase = CreatureWorkPhase.Return;
        }

        private static void TickReturn(RefRW<CreatureWorkState> work, EnabledRefRW<CreatureMoveOrder> order)
        {
            order.ValueRW = false;
            work.ValueRW.Site = Entity.Null;
            work.ValueRW.Delivery = Entity.Null;
            work.ValueRW.CarriedItemId = -1;
            work.ValueRW.CarriedCount = 0;
            work.ValueRW.Phase = CreatureWorkPhase.Idle;
        }

        private static void Abort(RefRW<CreatureWorkState> work, EnabledRefRW<CreatureMoveOrder> order)
        {
            order.ValueRW = false;
            work.ValueRW.Site = Entity.Null;
            work.ValueRW.Delivery = Entity.Null;
            work.ValueRW.CarriedItemId = -1;
            work.ValueRW.CarriedCount = 0;
            work.ValueRW.Phase = CreatureWorkPhase.Idle;
        }

        private static bool IsValidSite(EntityManager entityManager, Entity siteEntity, Entity worker)
        {
            if (siteEntity == Entity.Null || !entityManager.Exists(siteEntity) ||
                !entityManager.HasComponent<CreatureWorkSite>(siteEntity) ||
                !entityManager.HasComponent<LocalTransform>(siteEntity)) return false;
            return entityManager.GetComponentData<CreatureWorkSite>(siteEntity).Claimant == worker;
        }

        private static Entity FindStorage(EntityManager entityManager,
            in ComponentLookup<LocalTransform> transforms, Entity runtime, Entity habitat, float3 from)
        {
            if (runtime == Entity.Null || !entityManager.HasBuffer<CreatureStorageIndexEntry>(runtime))
                return Entity.Null;

            DynamicBuffer<CreatureStorageIndexEntry> entries =
                entityManager.GetBuffer<CreatureStorageIndexEntry>(runtime, true);
            Entity match = Entity.Null;
            Entity fallback = Entity.Null;
            float bestMatch = float.MaxValue;
            float bestFallback = float.MaxValue;

            for (int i = 0; i < entries.Length; i++)
            {
                Entity storage = entries[i].Storage;
                if (storage == Entity.Null || !transforms.HasComponent(storage)) continue;
                float distance = math.distancesq(transforms[storage].Position, from);

                if (entries[i].Habitat == habitat)
                {
                    if (distance >= bestMatch) continue;
                    bestMatch = distance;
                    match = storage;
                }
                else if (entries[i].Habitat == Entity.Null)
                {
                    if (distance >= bestFallback) continue;
                    bestFallback = distance;
                    fallback = storage;
                }
            }

            return match != Entity.Null ? match : fallback;
        }

        private static int Deposit(DynamicBuffer<CreatureStorageSlot> slots, in CreatureStorage storage,
            int itemId, int count)
        {
            if (itemId < 0 || count <= 0) return 0;
            int stackCapacity = math.max(1, storage.StackCapacity);
            int remaining = count;

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                CreatureStorageSlot slot = slots[i];
                if (slot.ItemId != itemId || slot.Count >= stackCapacity) continue;
                int room = stackCapacity - slot.Count;
                int moved = math.min(room, remaining);
                slot.Count += moved;
                slots[i] = slot;
                remaining -= moved;
            }

            while (remaining > 0 && slots.Length < math.max(1, storage.SlotCapacity))
            {
                int moved = math.min(stackCapacity, remaining);
                slots.Add(new CreatureStorageSlot { ItemId = itemId, Count = moved });
                remaining -= moved;
            }

            return count - remaining;
        }
    }
}
