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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _exclusions = new EntityQueryBuilder(Allocator.Temp).WithAll<ResourceSpawnExclusion>().Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldBuilder.Entities.WorldSimulationAuthority.Enabled) return;
            float rate = SystemAPI.TryGetSingleton<ResourceRegenerationRate>(out var globalRate)
                ? Unity.Mathematics.math.max(0f, globalRate.Value) : 1f;
            if (rate <= 0f) return;
            using NativeArray<ResourceSpawnExclusion> exclusions = _exclusions.ToComponentDataArray<ResourceSpawnExclusion>(Allocator.TempJob);
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency = new RespawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime * rate,
                Pieces = state.GetComponentLookup<ResourcePieceState>(),
                Positions = state.GetComponentLookup<Unity.Transforms.LocalTransform>(true),
                Rates = state.GetComponentLookup<WorldEntitySpawnRate>(true),
                Exclusions = exclusions,
                Commands = commands.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities)]
        private partial struct RespawnJob : IJobEntity
        {
            public float DeltaTime;
            [NativeDisableParallelForRestriction] public ComponentLookup<ResourcePieceState> Pieces;
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
                Commands.RemoveComponent<Disabled>(sortKey, entity);
            }
        }
    }
}
