using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using WorldBuilder.Entities.Resources;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Entities.Authoring
{
    [Serializable]
    public struct WorldEntityPrefabEntry
    {
        public int PrefabId;
        public GameObject Prefab;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("WorldBuilder/Entities/World Entity Runtime")]
    public sealed class WorldEntityRuntimeAuthoring : MonoBehaviour
    {
        [SerializeField] private WorldGridSettings gridSettings;
        [SerializeField] private WorldEntityPrefabEntry[] prefabs = Array.Empty<WorldEntityPrefabEntry>();

        public IReadOnlyList<WorldEntityPrefabEntry> Prefabs => prefabs ?? Array.Empty<WorldEntityPrefabEntry>();
        public WorldGridSettings GridSettings => gridSettings;

        private sealed class RuntimeBaker : Baker<WorldEntityRuntimeAuthoring>
        {
            public override void Bake(WorldEntityRuntimeAuthoring authoring)
            {
                if (authoring.gridSettings == null)
                {
                    Debug.LogError("World Entity Runtime requires the authoritative WorldGridSettings asset.", authoring);
                    return;
                }

                DependsOn(authoring.gridSettings);
                Entity entity = GetEntity(TransformUsageFlags.None);
                Vector3 origin = authoring.gridSettings.WorldOrigin;
                AddComponent(entity, new WorldEntityRuntimeConfig
                {
                    ChunkSize = authoring.gridSettings.AuthoringChunkSize,
                    ChunksPerRegion = authoring.gridSettings.ChunksPerRegion,
                    WorldOrigin = origin,
                    NextRuntimeId = 0
                });

                DynamicBuffer<WorldEntityPrefabElement> catalog = AddBuffer<WorldEntityPrefabElement>(entity);
                DynamicBuffer<WorldEntitySpawnRequest> requests = AddBuffer<WorldEntitySpawnRequest>(entity);
                DynamicBuffer<WorldEntityLoadedRegion> loadedRegions = AddBuffer<WorldEntityLoadedRegion>(entity);
                AddBuffer<ResourceHarvestRequest>(entity);
                AddBuffer<ResourceHarvestResult>(entity);
                AddBuffer<ResourceDropSpawnRequest>(entity);
                AddBuffer<DroppedItemPickupRequest>(entity);
                AddBuffer<InventoryGrantRequest>(entity);
                AddBuffer<InventoryGrantResult>(entity);
                requests.Clear();
                loadedRegions.Clear();

                WorldEntityPrefabEntry[] entries = authoring.prefabs ?? Array.Empty<WorldEntityPrefabEntry>();
                HashSet<int> registeredIds = new HashSet<int>();
                for (int i = 0; i < entries.Length; i++)
                {
                    WorldEntityPrefabEntry entry = entries[i];
                    if (entry.Prefab == null) continue;
                    if (!registeredIds.Add(entry.PrefabId))
                    {
                        Debug.LogError($"Duplicate entity prefab id {entry.PrefabId}.", authoring);
                        continue;
                    }
                    WorldEntityAuthoring entityAuthoring = entry.Prefab.GetComponent<WorldEntityAuthoring>();
                    if (entityAuthoring == null)
                    {
                        Debug.LogError($"Entity prefab '{entry.Prefab.name}' requires WorldEntityAuthoring.", entry.Prefab);
                        continue;
                    }
                    if (entityAuthoring.PrefabId != entry.PrefabId)
                    {
                        Debug.LogError($"Catalog id {entry.PrefabId} does not match '{entry.Prefab.name}' id {entityAuthoring.PrefabId}.", entry.Prefab);
                        continue;
                    }

                    catalog.Add(new WorldEntityPrefabElement
                    {
                        PrefabId = entry.PrefabId,
                        Prefab = GetEntity(entry.Prefab, TransformUsageFlags.Dynamic)
                    });
                }
            }
        }
    }
}
