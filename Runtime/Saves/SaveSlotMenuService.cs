using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// UI-agnostic save menu facade: list / save / load / delete over unified snapshots.
    /// Bind any UI toolkit (uGUI, UI Toolkit, DOTS) to the returned models and events —
    /// this class owns zero visuals so it stays testable and renderer-free.
    /// </summary>
    public sealed class SaveSlotMenuService
    {
        private readonly Func<VoxelStoreAsset> storeProvider;
        private readonly Func<IEnumerable<Vector3Int>> editedChunkProvider;
        private readonly Func<string> placementsJsonProvider;
        private readonly Func<string, GameObject> prefabResolver;

        public SaveSlotMenuService(Func<VoxelStoreAsset> storeProvider,
            Func<IEnumerable<Vector3Int>> editedChunkProvider,
            Func<string> placementsJsonProvider,
            Func<string, GameObject> prefabResolver)
        {
            this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
            this.editedChunkProvider = editedChunkProvider;
            this.placementsJsonProvider = placementsJsonProvider;
            this.prefabResolver = prefabResolver;
        }

        /// <summary>Fired after any successful save/load/delete so menus can refresh.</summary>
        public event Action Changed;

        public List<WorldSaveService.SaveInfo> Refresh() => WorldSaveService.List();

        public void Save(string slot, string extrasJson = null)
        {
            if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Slot name required.");
            VoxelStoreAsset store = storeProvider();
            WorldSaveService.SaveSnapshot(slot,
                store,
                editedChunkProvider?.Invoke(),
                placementsJsonProvider?.Invoke() ?? "{}",
                extrasJson);
            Changed?.Invoke();
        }

        public bool Load(string slot)
        {
            bool loaded = WorldSaveService.LoadSnapshot(slot, storeProvider(),
                prefabResolver, out _);
            if (loaded) Changed?.Invoke();
            return loaded;
        }

        public bool Delete(string slot)
        {
            // WorldSaveService.Delete clears the main file plus terrain/extras sidecars.
            bool deleted = WorldSaveService.Delete(slot);
            if (deleted) Changed?.Invoke();
            return deleted;
        }
    }
}
