using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures
{
    public static partial class WorldCreatureCommandQueue
    {
        public static bool TryDespawn(Entity target)
        {
            if (target == Entity.Null || !TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureDespawnRequest>(runtime)) return false;
            entityManager.GetBuffer<CreatureDespawnRequest>(runtime)
                .Add(new CreatureDespawnRequest { Target = target });
            return true;
        }

        public static int DespawnAll(CreatureFilter filter)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out Entity runtime) ||
                !entityManager.HasBuffer<CreatureDespawnRequest>(runtime)) return 0;
            DynamicBuffer<CreatureDespawnRequest> requests =
                entityManager.GetBuffer<CreatureDespawnRequest>(runtime);
            int queued = 0;
            EntityQuery query = CreateCreatureQuery(entityManager);
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryReadRecord(entityManager, entities[i], out CreatureRecord record)) continue;
                if (!filter.Matches(record)) continue;
                requests.Add(new CreatureDespawnRequest { Target = entities[i] });
                queued++;
            }
            entities.Dispose();
            return queued;
        }

        public static int Count(CreatureFilter filter)
        {
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return 0;
            EntityQuery query = CreateCreatureQuery(entityManager);
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int matched = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryReadRecord(entityManager, entities[i], out CreatureRecord record)) continue;
                if (filter.Matches(record)) matched++;
            }
            entities.Dispose();
            return matched;
        }

        public static int Collect(List<CreatureRecord> destination, CreatureFilter filter)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return 0;
            EntityQuery query = CreateCreatureQuery(entityManager);
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryReadRecord(entityManager, entities[i], out CreatureRecord record)) continue;
                if (filter.Matches(record)) destination.Add(record);
            }
            entities.Dispose();
            return destination.Count;
        }

        public static bool TryFindNearest(Vector3 position, float maximumDistance, CreatureFilter filter,
            out CreatureRecord nearest)
        {
            nearest = default;
            if (!TryGetRuntime(out EntityManager entityManager, out _)) return false;
            float limit = maximumDistance <= 0f ? float.MaxValue : maximumDistance * maximumDistance;
            float best = float.MaxValue;
            bool found = false;

            EntityQuery query = CreateCreatureQuery(entityManager);
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryReadRecord(entityManager, entities[i], out CreatureRecord record)) continue;
                if (!filter.Matches(record)) continue;
                float distance = math.distancesq(record.Position, (float3)position);
                if (distance > limit || distance >= best) continue;
                best = distance;
                nearest = record;
                found = true;
            }
            entities.Dispose();
            return found;
        }

        public static bool TryFindByIdentity(WorldEntityIdentity identity, out CreatureRecord match)
        {
            match = default;
            if (!identity.IsValid || !TryGetRuntime(out EntityManager entityManager, out _)) return false;
            EntityQuery query = CreateCreatureQuery(entityManager);
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            bool found = false;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryReadRecord(entityManager, entities[i], out CreatureRecord record)) continue;
                if (!record.Identity.Equals(identity)) continue;
                match = record;
                found = true;
                break;
            }
            entities.Dispose();
            return found;
        }

        public static bool TryGetRecord(Entity target, out CreatureRecord record)
        {
            record = default;
            return TryGetRuntime(out EntityManager entityManager, out _) &&
                   TryReadRecord(entityManager, target, out record);
        }

        private static EntityQuery CreateCreatureQuery(EntityManager entityManager)
            => WorldRuntimeLookup.Creatures(entityManager);

        // Records are read many times a frame; turning the same few names into new strings each time was most of the
        // garbage this file made. There are only as many names as there are kinds of creature.
        private static readonly Dictionary<FixedString64Bytes, string> NameCache = new();

        private static string NameOf(in FixedString64Bytes name)
        {
            if (NameCache.TryGetValue(name, out string text)) return text;
            if (NameCache.Count > 512) NameCache.Clear();
            text = name.ToString();
            NameCache.Add(name, text);
            return text;
        }

        private static bool TryReadRecord(EntityManager entityManager, Entity entity, out CreatureRecord record)
        {
            record = default;
            if (entity == Entity.Null || !entityManager.Exists(entity) ||
                !entityManager.HasComponent<Creature>(entity) || entityManager.HasComponent<DecorativeCreature>(entity) ||
                !entityManager.HasComponent<LocalTransform>(entity)) return false;

            Creature creature = entityManager.GetComponentData<Creature>(entity);
            WorldEntityIdentity identity = entityManager.HasComponent<WorldEntityIdentity>(entity)
                ? entityManager.GetComponentData<WorldEntityIdentity>(entity)
                : default;
            int prefabId = entityManager.HasComponent<WorldEntityDescriptor>(entity)
                ? entityManager.GetComponentData<WorldEntityDescriptor>(entity).PrefabId
                : CreatureFilter.AnyPrefab;
            int2 region = entityManager.HasComponent<WorldEntityChunk>(entity)
                ? entityManager.GetComponentData<WorldEntityChunk>(entity).Region
                : default;
            CreatureAffinity affinity = entityManager.HasComponent<CreatureAffinity>(entity)
                ? entityManager.GetComponentData<CreatureAffinity>(entity)
                : default;
            CreatureTaming taming = entityManager.HasComponent<CreatureTaming>(entity)
                ? entityManager.GetComponentData<CreatureTaming>(entity)
                : default;
            bool active = !entityManager.HasComponent<Disabled>(entity) &&
                          (!entityManager.HasComponent<WorldEntityActive>(entity) ||
                           entityManager.IsComponentEnabled<WorldEntityActive>(entity));

            record = new CreatureRecord(entity, identity, prefabId, NameOf(creature.DisplayName), creature.Grade,
                creature.SizeClass, creature.Personality,
                entityManager.GetComponentData<LocalTransform>(entity).Position, region, affinity.Value,
                affinity.MaximumValue, taming.AttemptCount, taming.IsTamed != 0, active);
            return true;
        }
    }
}
