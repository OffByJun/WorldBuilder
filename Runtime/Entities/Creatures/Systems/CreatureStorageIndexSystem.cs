using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Creatures.Systems
{
    /// <summary>
    /// Keeps a cached list of storage entities on the runtime singleton so workers never build
    /// an EntityQuery mid-frame. Only the entity references are cached; positions are read live,
    /// so a storage that moves stays correct without a rebuild.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct CreatureStorageIndexSystem : ISystem
    {
        private EntityQuery storageQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureStorageIndex>();
            storageQuery = state.GetEntityQuery(ComponentType.ReadOnly<CreatureStorage>());
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureStorageIndex>();
            uint version = (uint)state.EntityManager.GetComponentOrderVersion<CreatureStorage>();
            CreatureStorageIndex index = state.EntityManager.GetComponentData<CreatureStorageIndex>(runtime);
            if (index.OrderVersion == version) return;

            DynamicBuffer<CreatureStorageIndexEntry> entries =
                state.EntityManager.GetBuffer<CreatureStorageIndexEntry>(runtime);
            entries.Clear();

            NativeArray<Entity> storages = storageQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < storages.Length; i++)
            {
                entries.Add(new CreatureStorageIndexEntry
                {
                    Storage = storages[i],
                    Habitat = state.EntityManager.GetComponentData<CreatureStorage>(storages[i]).Habitat
                });
            }
            storages.Dispose();

            index.OrderVersion = version;
            state.EntityManager.SetComponentData(runtime, index);
        }
    }
}
