using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Editing;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// Scene-facing save/load component: binds known prefabs by name and exposes
    /// slot operations to game UI. Wraps <see cref="WorldSaveService"/> and
    /// <see cref="RuntimePlacementService"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSaveManager : MonoBehaviour
    {
        [Serializable]
        public sealed class KnownPrefab
        {
            public string prefabId = string.Empty;
            public GameObject prefab;
        }

        [SerializeField] private List<KnownPrefab> knownPrefabs = new List<KnownPrefab>();
        [SerializeField] private string defaultWorldId = "World_01";
        [SerializeField] private WorldBuilder.Runtime.Data.VoxelStoreAsset terrainStore;
        [SerializeField] private bool includeTerrainEdits = true;

        private Dictionary<string, GameObject> lookup;

        public event Action<string> Saved;
        public event Action<string> Loaded;

        /// <summary>Raised per chunk restored from a terrain delta (re-mesh here).</summary>
        public event Action<Vector3Int> TerrainChunkRestored;

        public WorldBuilder.Runtime.Data.VoxelStoreAsset TerrainStore
        {
            get => terrainStore;
            set => terrainStore = value;
        }

        private void Awake() => BuildLookup();

        private void BuildLookup()
        {
            if (lookup != null) return;
            lookup = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            for (int i = 0; i < knownPrefabs.Count; i++)
            {
                KnownPrefab entry = knownPrefabs[i];
                if (entry == null || string.IsNullOrEmpty(entry.prefabId) || entry.prefab == null) continue;
                if (!lookup.ContainsKey(entry.prefabId)) lookup[entry.prefabId] = entry.prefab;
            }
        }

        public void SaveToSlot(string slot)
        {
            WorldSaveService.DefaultWorldId = defaultWorldId;
            WorldSaveService.Save(slot, RuntimePlacementService.ToJson(), defaultWorldId);
            if (includeTerrainEdits && terrainStore != null &&
                Terrain.TerrainDeformer.EditedChunks.Count > 0)
            {
                WorldSaveService.SaveTerrain(slot, terrainStore, Terrain.TerrainDeformer.EditedChunks);
            }
            Saved?.Invoke(slot);
        }

        public bool LoadFromSlot(string slot)
        {
            BuildLookup();
            bool found = WorldSaveService.Load(slot, id => lookup.TryGetValue(id, out GameObject prefab) ? prefab : null);

            if (includeTerrainEdits && terrainStore != null)
            {
                int restored = WorldSaveService.LoadTerrain(slot, terrainStore,
                    coord => TerrainChunkRestored?.Invoke(coord));
                if (restored > 0) found = true;
                Terrain.TerrainDeformer.ResetJournal();
            }

            if (found) Loaded?.Invoke(slot);
            return found;
        }

        public bool DeleteSlot(string slot) => WorldSaveService.Delete(slot);
        public bool SlotExists(string slot) => WorldSaveService.Exists(slot);

        public List<WorldSaveService.SaveInfo> ListSlots() => WorldSaveService.List();

        public void AddKnownPrefab(string prefabId, GameObject prefab)
        {
            knownPrefabs.Add(new KnownPrefab { prefabId = prefabId, prefab = prefab });
            lookup = null;
        }
    }
}
