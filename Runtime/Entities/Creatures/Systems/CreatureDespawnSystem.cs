using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct CreatureDespawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureDespawnRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<CreatureDespawnRequest>();
            DynamicBuffer<CreatureDespawnRequest> requests =
                state.EntityManager.GetBuffer<CreatureDespawnRequest>(runtime);
            if (requests.IsEmpty) return;

            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                Entity target = requests[i].Target;
                if (target == Entity.Null || !state.EntityManager.Exists(target) ||
                    !state.EntityManager.HasComponent<Creature>(target)) continue;
                commands.DestroyEntity(target);
            }

            requests.Clear();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
