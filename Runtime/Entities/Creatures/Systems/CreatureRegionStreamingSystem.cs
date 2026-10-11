using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using WorldBuilder.Entities.Systems;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldEntityChunkOwnershipSystem))]
    public partial struct CreatureRegionStreamingSystem : ISystem
    {
        private NativeQueue<Entity> _pendingDespawns;
        private JobHandle _streamJob;

        public void OnCreate(ref SystemState state)
        {
            _pendingDespawns = new NativeQueue<Entity>(Allocator.Persistent);
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntityLoadedRegion>();
            state.RequireForUpdate<CreatureStreaming>();
        }

        public void OnDestroy(ref SystemState state)
        {
            state.CompleteDependency();
            _streamJob.Complete();
            _pendingDespawns.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            // Drain the previous frame after its job has finished. Other despawn paths may
            // already have removed a creature, so validate before the structural change.
            _streamJob.Complete();
            while (_pendingDespawns.TryDequeue(out Entity target))
                if (state.EntityManager.Exists(target)) state.EntityManager.DestroyEntity(target);
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<WorldEntityLoadedRegion> regions =
                state.EntityManager.GetBuffer<WorldEntityLoadedRegion>(runtime, true);
            if (regions.IsEmpty) return;

            NativeParallelHashSet<int2> loaded = new NativeParallelHashSet<int2>(regions.Length, Allocator.TempJob);
            for (int i = 0; i < regions.Length; i++) loaded.Add(regions[i].Coordinate);

            state.Dependency = new StreamCreaturesJob
            {
                Loaded = loaded,
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Despawns = _pendingDespawns.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency = loaded.Dispose(state.Dependency);
            _streamJob = state.Dependency;
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct StreamCreaturesJob : IJobEntity
        {
            [ReadOnly] public NativeParallelHashSet<int2> Loaded;
            public double ElapsedTime;
            public NativeQueue<Entity>.ParallelWriter Despawns;

            private void Execute(Entity entity,
                ref CreatureStreaming streaming, in WorldEntityChunk ownership,
                in WorldEntityDescriptor descriptor, in Creature creature)
            {
                if ((descriptor.Flags & WorldEntityFlags.Persistent) != 0) return;
                if (Loaded.Contains(ownership.Region))
                {
                    streaming.UnloadedSince = 0d;
                    return;
                }

                if (streaming.UnloadedSince <= 0d)
                {
                    streaming.UnloadedSince = ElapsedTime;
                    return;
                }

                if (ElapsedTime - streaming.UnloadedSince >= streaming.DespawnGraceSeconds)
                    Despawns.Enqueue(entity);
            }
        }
    }
}
