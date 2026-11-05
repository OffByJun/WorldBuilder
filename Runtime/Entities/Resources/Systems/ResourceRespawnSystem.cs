using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Resources.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ResourceRespawnSystem : ISystem
    {
        // Built once here: building it inside OnUpdate made a managed array, which stopped Burst compiling OnUpdate.
        private EntityQuery _exclusions;
        private ComponentLookup<ResourcePieceState> _pieces;
        private ComponentLookup<ResourcePieceCut> _cuts;
        private ComponentLookup<Unity.Transforms.LocalTransform> _positions;
        private ComponentLookup<WorldEntitySpawnRate> _rates;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _exclusions = new EntityQueryBuilder(Allocator.Temp).WithAll<ResourceSpawnExclusion>().Build(ref state);
            _pieces = state.GetComponentLookup<ResourcePieceState>();
            _cuts = state.GetComponentLookup<ResourcePieceCut>();
            _positions = state.GetComponentLookup<Unity.Transforms.LocalTransform>(true);
            _rates = state.GetComponentLookup<WorldEntitySpawnRate>(true);
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            float rate = SystemAPI.TryGetSingleton<ResourceRegenerationRate>(out var globalRate)
                ? Unity.Mathematics.math.max(0f, globalRate.Value) : 1f;
            if (rate <= 0f) return;
            _pieces.Update(ref state);
            _cuts.Update(ref state);
            _positions.Update(ref state);
            _rates.Update(ref state);
            NativeArray<ResourceSpawnExclusion> exclusions = _exclusions.ToComponentDataArray<ResourceSpawnExclusion>(Allocator.TempJob);
            // The re-enabled nodes are played back at the end of the simulation group, so the frame does not wait on the job.
            EntityCommandBuffer commands = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            state.Dependency = new RespawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime * rate,
                Pieces = _pieces,
                Cuts = _cuts,
                Positions = _positions,
                Rates = _rates,
                Exclusions = exclusions,
                Commands = commands.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            exclusions.Dispose(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities)]
        private partial struct RespawnJob : IJobEntity
        {
            public float DeltaTime;
            [NativeDisableParallelForRestriction] public ComponentLookup<ResourcePieceState> Pieces;
            [NativeDisableParallelForRestriction] public ComponentLookup<ResourcePieceCut> Cuts;
            [ReadOnly] public ComponentLookup<Unity.Transforms.LocalTransform> Positions;
            [ReadOnly] public ComponentLookup<WorldEntitySpawnRate> Rates;
            [ReadOnly] public NativeArray<ResourceSpawnExclusion> Exclusions;
            public EntityCommandBuffer.ParallelWriter Commands;

            private void Execute(Entity entity, [EntityIndexInQuery] int sortKey, ref ResourceNode node,
                ref ResourceRespawnState respawn, in Disabled disabled)
            {
                float rate = Rates.HasComponent(entity) ? Rates[entity].Value : 1f;
                if (rate <= 0f) return;
                respawn.RemainingSeconds -= DeltaTime * rate;
                if (respawn.RemainingSeconds > 0f) return;
                if (Positions.HasComponent(entity))
                    foreach (var exclusion in Exclusions)
                        if (exclusion.Contains(Positions[entity].Position))
                        {
                            respawn.RemainingSeconds = 2f;
                            return;
                        }
                respawn.RemainingSeconds = 0f;
                node.Health = node.MaxHealth;
                node.NextHitTime = 0d;
                if (Pieces.HasComponent(entity)) Pieces[entity] = default;
                if (Cuts.HasComponent(entity)) Cuts[entity] = default;
                Commands.RemoveComponent<Disabled>(sortKey, entity);
            }
        }
    }
}
