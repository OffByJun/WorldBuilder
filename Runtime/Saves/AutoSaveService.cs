using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// Rotating autosave on top of <see cref="SaveSlotMenuService"/>: every interval (and
    /// optionally on quit) writes "autosave_NN", keeping only the newest N slots. Bind the
    /// same provider delegates the menu service uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutoSaveService : MonoBehaviour
    {
        [Min(30f)] [SerializeField] private float intervalSeconds = 300f;
        [Range(1, 10)] [SerializeField] private int slotsToKeep = 3;
        [SerializeField] private string slotPattern = "autosave";
        [Tooltip("Also write a snapshot when the application quits.")]
        [SerializeField] private bool saveOnQuit = true;

        private SaveSlotMenuService service;
        private float timer;
        private bool bound;

        public string LastSavedSlot { get; private set; }

        public event Action<string> AutoSaved;

        public void Bind(Func<VoxelStoreAsset> store,
            Func<IEnumerable<Vector3Int>> editedChunks,
            Func<string> placementsJson,
            Func<string, GameObject> prefabResolver)
        {
            service = new SaveSlotMenuService(store, editedChunks, placementsJson, prefabResolver);
            bound = true;
        }

        private void Update()
        {
            if (!bound) return;
            timer += Time.unscaledDeltaTime;
            if (timer < intervalSeconds) return;
            timer = 0f;
            TickNow();
        }

        /// <summary>Manual/queued save — also used by tests. Returns the slot written.</summary>
        public string TickNow()
        {
            if (!bound) return null;

            List<WorldSaveService.SaveInfo> infos = WorldSaveService.List();
            int existing = CountAutosaves(infos);
            if (existing >= slotsToKeep)
                DeleteOldestAutosave(infos);

            // After pruning there is at least one free ring index.
            int index = Math.Min(existing, Mathf.Max(0, slotsToKeep - 1));
            string slot = $"{slotPattern}_{index:00}";
            service.Save(slot);

            LastSavedSlot = slot;
            AutoSaved?.Invoke(slot);
            return slot;
        }

        private void OnApplicationQuit()
        {
            if (!saveOnQuit || !bound) return;
            TickNow();
        }

        private static int CountAutosaves(List<WorldSaveService.SaveInfo> infos) =>
            infos.FindAll(info => info.Slot.StartsWith("autosave", StringComparison.Ordinal)).Count;

        private static void DeleteOldestAutosave(List<WorldSaveService.SaveInfo> infos)
        {
            WorldSaveService.SaveInfo oldest = null;
            foreach (WorldSaveService.SaveInfo info in infos)
            {
                if (!info.Slot.StartsWith("autosave", StringComparison.Ordinal)) continue;
                if (oldest == null || info.TimestampUtc < oldest.TimestampUtc) oldest = info;
            }
            if (oldest != null) WorldSaveService.Delete(oldest.Slot);
        }
    }
}
