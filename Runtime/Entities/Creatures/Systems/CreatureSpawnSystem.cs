using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using WorldBuilder.Entities.Systems;

namespace WorldBuilder.Entities.Creatures.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(WorldEntitySpawnSystem))]
    [UpdateBefore(typeof(WorldEntityRegionActivationSystem))]
    public partial struct CreatureSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldEntityRuntimeConfig>();
            state.RequireForUpdate<WorldEntityPrefabElement>();
            state.RequireForUpdate<CreatureSpawnRequest>();
            state.RequireForUpdate<CreatureGradeDefinition>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtime = SystemAPI.GetSingletonEntity<WorldEntityRuntimeConfig>();
            DynamicBuffer<CreatureSpawnRequest> requests =
                state.EntityManager.GetBuffer<CreatureSpawnRequest>(runtime);
            if (requests.IsEmpty) return;

            DynamicBuffer<WorldEntityPrefabElement> prefabs =
                state.EntityManager.GetBuffer<WorldEntityPrefabElement>(runtime, true);
            NativeParallelHashMap<int, Entity> prefabMap =
                new NativeParallelHashMap<int, Entity>(math.max(1, prefabs.Length), Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++) prefabMap.TryAdd(prefabs[i].PrefabId, prefabs[i].Prefab);

            DynamicBuffer<CreatureGradeDefinition> definitions =
                state.EntityManager.GetBuffer<CreatureGradeDefinition>(runtime, true);
            RefRW<WorldEntityRuntimeConfig> config = SystemAPI.GetSingletonRW<WorldEntityRuntimeConfig>();
            EntityCommandBuffer commands = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < requests.Length; i++)
            {
                CreatureSpawnRequest request = requests[i];
                if (!prefabMap.TryGetValue(request.PrefabId, out Entity prefab) ||
                    !state.EntityManager.HasComponent<Creature>(prefab)) continue;

                Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(request.Seed));
                CreatureGrade grade =
                    (request.StateFlags & CreatureSpawnRequestFlags.ExplicitGrade) != 0
                        ? request.Grade
                        : CreatureGradeRules.SelectGrade(definitions, CreatureGradeMask.All, ref random);
                CreatureGradeDefinition definition = CreatureGradeRules.Resolve(definitions, grade);
                CreatureAppearance appearance =
                    (request.StateFlags & CreatureSpawnRequestFlags.ExplicitAppearance) != 0
                        ? request.Appearance
                        : CreatureGradeRules.SpawnAppearance(definition, ref random);

                Entity instance = commands.Instantiate(prefab);
                WorldEntityIdentity identity = request.Identity;
                if ((request.StateFlags & CreatureSpawnRequestFlags.ExplicitIdentity) == 0 || !identity.IsValid)
                {
                    config.ValueRW.NextRuntimeId++;
                    identity = new WorldEntityIdentity { Low = config.ValueRO.NextRuntimeId };
                }
                commands.SetComponent(instance, identity);

                float baseScale = state.EntityManager.GetComponentData<LocalTransform>(prefab).Scale;
                commands.SetComponent(instance, LocalTransform.FromPositionRotationScale(request.Position,
                    request.Rotation, math.max(0.001f, baseScale * definition.SizeMultiplier)));

                int2 chunk = WorldEntityGridUtility.WorldToChunk(request.Position, config.ValueRO);
                int2 region = WorldEntityGridUtility.ChunkToRegion(chunk, config.ValueRO.ChunksPerRegion);
                commands.SetComponent(instance, new WorldEntityChunk { Chunk = chunk, Region = region });

                Creature creature = state.EntityManager.GetComponentData<Creature>(prefab);
                creature.Grade = grade;
                commands.SetComponent(instance, creature);
                commands.SetComponent(instance, new CreatureRandom { State = random.NextUInt() });
                commands.SetComponent(instance, appearance);
                commands.SetComponentEnabled<CreatureAppearanceDirty>(instance, true);

                if (state.EntityManager.HasComponent<CreatureSwim>(prefab))
                {
                    CreatureSwim swim = state.EntityManager.GetComponentData<CreatureSwim>(prefab);
                    swim.Home = (request.StateFlags & CreatureSpawnRequestFlags.ExplicitHome) != 0
                        ? request.Home
                        : request.Position;
                    swim.HomeRegion = WorldEntityGridUtility.WorldToRegion(swim.Home, config.ValueRO);
                    swim.TargetPoint = request.Position;
                    swim.CruiseSpeed *= definition.SpeedMultiplier;
                    swim.NextRepathTime = 0d;
                    commands.SetComponent(instance, swim);
                }

                if (state.EntityManager.HasComponent<CreatureAlarm>(prefab))
                {
                    CreatureAlarm alarm = state.EntityManager.GetComponentData<CreatureAlarm>(prefab);
                    alarm.AlarmedUntil = 0d;
                    commands.SetComponent(instance, alarm);
                }

                if ((request.StateFlags & CreatureSpawnRequestFlags.ExplicitTaming) != 0 &&
                    state.EntityManager.HasComponent<CreatureTaming>(prefab))
                {
                    CreatureTaming taming = state.EntityManager.GetComponentData<CreatureTaming>(prefab);
                    taming.AttemptCount = request.TameAttempts;
                    taming.IsTamed = request.IsTamed;
                    commands.SetComponent(instance, taming);
                    if (state.EntityManager.HasComponent<CreatureTamed>(prefab))
                        commands.SetComponentEnabled<CreatureTamed>(instance, request.IsTamed != 0);
                }
                else if (state.EntityManager.HasComponent<CreatureTamed>(prefab))
                {
                    commands.SetComponentEnabled<CreatureTamed>(instance, false);
                }

                if ((request.StateFlags & CreatureSpawnRequestFlags.ExplicitAffinity) != 0 &&
                    state.EntityManager.HasComponent<CreatureAffinity>(prefab))
                {
                    CreatureAffinity affinity = state.EntityManager.GetComponentData<CreatureAffinity>(prefab);
                    affinity.Value = math.clamp(request.Affinity, 0f, affinity.MaximumValue);
                    affinity.NextFeedTime = 0d;
                    commands.SetComponent(instance, affinity);
                }

                if (state.EntityManager.HasComponent<CreatureStreaming>(prefab))
                {
                    CreatureStreaming streaming = state.EntityManager.GetComponentData<CreatureStreaming>(prefab);
                    streaming.UnloadedSince = 0d;
                    commands.SetComponent(instance, streaming);
                }

                if (state.EntityManager.HasComponent<CreatureCaptured>(prefab))
                    commands.SetComponentEnabled<CreatureCaptured>(instance, false);
                commands.SetComponentEnabled<WorldEntityActive>(instance, true);

                if (request.Owner != Entity.Null &&
                    state.EntityManager.HasBuffer<CreatureSpawnedEntity>(request.Owner))
                    commands.AppendToBuffer(request.Owner, new CreatureSpawnedEntity { Value = instance });
            }

            config.ValueRW.RegionRevision++;
            requests.Clear();
            commands.Playback(state.EntityManager);
            commands.Dispose();
            prefabMap.Dispose();
        }
    }
}
