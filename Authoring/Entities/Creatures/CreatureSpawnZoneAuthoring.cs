using System;
using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    [AddComponentMenu("WorldBuilder/Entities/Creature Spawn Zone")]
    public sealed class CreatureSpawnZoneAuthoring : MonoBehaviour
    {
        [Tooltip("WorldEntity prefab id of the creature prefab.")]
        [SerializeField] private int prefabId;
        [SerializeField] private WorldBuilder.Entities.Authoring.SpawnZoneEntryAuthoring[] spawnEntries = Array.Empty<WorldBuilder.Entities.Authoring.SpawnZoneEntryAuthoring>();
        [SerializeField] private Vector3 size = new Vector3(30f, 12f, 30f);
        [SerializeField] private CreatureGradeMask allowedGrades = CreatureGradeMask.All;
        [Min(0.01f), SerializeField] private float spawnInterval = 6f;
        [Min(0), SerializeField] private int maximumAlive = 12;
        [Min(1), SerializeField] private int spawnPerTick = 2;
        [SerializeField] private uint randomSeed = 1;

        private void OnValidate()
        {
            foreach (var entry in spawnEntries ?? Array.Empty<WorldBuilder.Entities.Authoring.SpawnZoneEntryAuthoring>())
                if (entry != null) entry.Weight = float.IsNaN(entry.Weight) ? 0f : Mathf.Clamp01(entry.Weight);
            size = new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));
            spawnInterval = Mathf.Max(0.01f, spawnInterval);
            maximumAlive = Mathf.Max(0, maximumAlive);
            spawnPerTick = Mathf.Max(1, spawnPerTick);
            if (allowedGrades == CreatureGradeMask.None) allowedGrades = CreatureGradeMask.All;
            if (randomSeed == 0) randomSeed = 1;
        }

        private sealed class CreatureSpawnZoneBaker : Baker<CreatureSpawnZoneAuthoring>
        {
            public override void Bake(CreatureSpawnZoneAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureSpawnZone
                {
                    PrefabId = authoring.prefabId,
                    HalfExtents = authoring.size * 0.5f,
                    AllowedGrades = authoring.allowedGrades == CreatureGradeMask.None
                        ? CreatureGradeMask.All
                        : authoring.allowedGrades,
                    SpawnInterval = Mathf.Max(0.01f, authoring.spawnInterval),
                    MaximumAlive = Mathf.Max(0, authoring.maximumAlive),
                    SpawnPerTick = Mathf.Max(1, authoring.spawnPerTick),
                    RandomState = CreatureGradeRules.SanitizeSeed(authoring.randomSeed)
                });
                AddBuffer<CreatureSpawnedEntity>(entity);
                var entries = AddBuffer<CreatureZoneSpawnEntry>(entity);
                foreach (var entry in authoring.spawnEntries ?? Array.Empty<WorldBuilder.Entities.Authoring.SpawnZoneEntryAuthoring>())
                {
                    if (entry == null) continue;
                    int id = authoring.prefabId;
                    if (entry.Prefab)
                    {
                        DependsOn(entry.Prefab);
                        var identity = entry.Prefab.GetComponent<WorldBuilder.Entities.Authoring.WorldEntityAuthoring>();
                        if (!identity) continue;
                        DependsOn(identity);
                        id = identity.PrefabId;
                    }
                    entries.Add(new CreatureZoneSpawnEntry { PrefabId = id,
                        Weight = float.IsNaN(entry.Weight) || float.IsInfinity(entry.Weight) ? 0f : Mathf.Clamp01(entry.Weight) });
                }
                if (authoring.spawnEntries != null && authoring.spawnEntries.Length > 0 && entries.Length == 0)
                    entries.Add(default);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 0.6f, 1f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
#endif
    }
}
