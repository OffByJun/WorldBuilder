using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures
{
    public readonly struct CreatureHabitatRecord
    {
        public readonly Entity Entity;
        public readonly string DisplayName;
        public readonly float3 Position;
        public readonly CreatureEnvironmentMask Provided;
        public readonly int Capacity;
        public readonly int MemberCount;

        public CreatureHabitatRecord(Entity entity, string displayName, float3 position,
            CreatureEnvironmentMask provided, int capacity, int memberCount)
        {
            Entity = entity;
            DisplayName = displayName ?? string.Empty;
            Position = position;
            Provided = provided;
            Capacity = capacity;
            MemberCount = memberCount;
        }

        public bool HasRoom => Capacity <= 0 || MemberCount < Capacity;
    }

    public static partial class WorldCreatureCommandQueue
    {
        public static bool SetPlayerFocus(Vector3 position)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasComponent<CreaturePlayerFocus>(runtime)) return false;
            entityManager.SetComponentData(runtime, new CreaturePlayerFocus
            {
                Position = position,
                IsValid = 1
            });
            return true;
        }

        public static bool ClearPlayerFocus()
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasComponent<CreaturePlayerFocus>(runtime)) return false;
            entityManager.SetComponentData(runtime, new CreaturePlayerFocus());
            return true;
        }

        public static bool TrySettle(Entity target, Entity habitat, out uint requestId)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureSettleRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<CreatureSettleRequest>(runtime).Add(new CreatureSettleRequest
            {
                RequestId = requestId,
                Target = target,
                Habitat = habitat
            });
            return true;
        }

        public static bool TryUnsettle(Entity target, out uint requestId)
        {
            requestId = 0;
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureUnsettleRequest>(runtime)) return false;
            requestId = NextRequestId();
            entityManager.GetBuffer<CreatureUnsettleRequest>(runtime).Add(new CreatureUnsettleRequest
            {
                RequestId = requestId,
                Target = target
            });
            return true;
        }

        public static int DrainSettleResults(Action<CreatureSettleResult> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureSettleResult>(runtime)) return 0;
            DynamicBuffer<CreatureSettleResult> results = entityManager.GetBuffer<CreatureSettleResult>(runtime);
            int count = results.Length;
            for (int i = 0; i < results.Length; i++) visitor(results[i]);
            results.Clear();
            return count;
        }

        public static int DrainWorkEvents(Action<CreatureWorkCompletedEvent> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureWorkCompletedEvent>(runtime)) return 0;
            DynamicBuffer<CreatureWorkCompletedEvent> events =
                entityManager.GetBuffer<CreatureWorkCompletedEvent>(runtime);
            int count = events.Length;
            for (int i = 0; i < events.Length; i++) visitor(events[i]);
            events.Clear();
            return count;
        }

        public static int CollectHabitats(List<CreatureHabitatRecord> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return 0;

            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureHabitat>(),
                ComponentType.ReadOnly<LocalTransform>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                CreatureHabitat habitat = entityManager.GetComponentData<CreatureHabitat>(entities[i]);
                int members = entityManager.HasBuffer<CreatureHabitatMember>(entities[i])
                    ? entityManager.GetBuffer<CreatureHabitatMember>(entities[i], true).Length
                    : 0;
                destination.Add(new CreatureHabitatRecord(entities[i], habitat.DisplayName.ToString(),
                    entityManager.GetComponentData<LocalTransform>(entities[i]).Position, habitat.Provided,
                    habitat.Capacity, members));
            }
            entities.Dispose();
            query.Dispose();
            return destination.Count;
        }

        public static bool TryFindNearestHabitat(Vector3 position, float maximumDistance,
            out CreatureHabitatRecord nearest)
        {
            nearest = default;
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;

            float limit = maximumDistance <= 0f ? float.MaxValue : maximumDistance * maximumDistance;
            float best = float.MaxValue;
            bool found = false;

            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureHabitat>(),
                ComponentType.ReadOnly<LocalTransform>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                float3 habitatPosition = entityManager.GetComponentData<LocalTransform>(entities[i]).Position;
                float distance = math.distancesq(habitatPosition, (float3)position);
                if (distance > limit || distance >= best) continue;

                CreatureHabitat habitat = entityManager.GetComponentData<CreatureHabitat>(entities[i]);
                int members = entityManager.HasBuffer<CreatureHabitatMember>(entities[i])
                    ? entityManager.GetBuffer<CreatureHabitatMember>(entities[i], true).Length
                    : 0;
                best = distance;
                nearest = new CreatureHabitatRecord(entities[i], habitat.DisplayName.ToString(), habitatPosition,
                    habitat.Provided, habitat.Capacity, members);
                found = true;
            }
            entities.Dispose();
            query.Dispose();
            return found;
        }

        public static bool TryPreviewSettle(Entity target, Entity habitat, out CreatureSettleFailure failure)
        {
            failure = CreatureSettleFailure.InvalidTarget;
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;
            if (target == Entity.Null || !entityManager.Exists(target) ||
                !entityManager.HasComponent<Creature>(target) ||
                !entityManager.HasComponent<CreatureEnvironmentNeeds>(target) ||
                habitat == Entity.Null || !entityManager.Exists(habitat) ||
                !entityManager.HasComponent<CreatureHabitat>(habitat)) return false;

            bool isTamed = entityManager.HasComponent<CreatureTaming>(target) &&
                           entityManager.GetComponentData<CreatureTaming>(target).IsTamed != 0;
            bool isCustomized = entityManager.HasComponent<CreatureCustomized>(target) &&
                                entityManager.IsComponentEnabled<CreatureCustomized>(target);
            bool settled = entityManager.HasComponent<CreatureSettled>(target) &&
                           entityManager.IsComponentEnabled<CreatureSettled>(target);
            int members = entityManager.HasBuffer<CreatureHabitatMember>(habitat)
                ? entityManager.GetBuffer<CreatureHabitatMember>(habitat, true).Length
                : 0;

            failure = CreatureSettlementRules.Evaluate(entityManager.GetComponentData<Creature>(target), isTamed,
                isCustomized, settled, entityManager.GetComponentData<CreatureEnvironmentNeeds>(target),
                entityManager.GetComponentData<CreatureHabitat>(habitat), members);
            return true;
        }

        public static int CollectStorageContents(Entity storage, List<CreatureStorageSlot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetRuntime(out EntityManager entityManager, out _) || storage == Entity.Null ||
                !entityManager.Exists(storage) ||
                !entityManager.HasBuffer<CreatureStorageSlot>(storage)) return 0;

            DynamicBuffer<CreatureStorageSlot> slots = entityManager.GetBuffer<CreatureStorageSlot>(storage, true);
            for (int i = 0; i < slots.Length; i++) destination.Add(slots[i]);
            return destination.Count;
        }
    }
}
