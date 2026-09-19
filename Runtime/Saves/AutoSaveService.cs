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
            infos.RemoveAll(info => !IsAutosave(info.Slot));
            int index = 0;
            string slot;
            do
            {
                slot = $"{slotPattern}_{index++:00}";
            }
            while (WorldSaveService.Exists(slot) || WorldSaveService.Exists(slot + "_terrain") ||
                WorldSaveService.Exists(slot + "_extras"));

            try
            {
                service.Save(slot);
            }
            catch
            {
                try { WorldSaveService.Delete(slot); }
                catch (Exception) { }
                throw;
            }

            int keep = Mathf.Max(1, slotsToKeep);
            for (int i = infos.Count - 1; i >= keep - 1; i--)
                WorldSaveService.Delete(infos[i].Slot);

            LastSavedSlot = slot;
            AutoSaved?.Invoke(slot);
            return slot;
        }

        private void OnApplicationQuit()
        {
            if (!saveOnQuit || !bound) return;
            TickNow();
        }

        private bool IsAutosave(string slot)
        {
            string prefix = slotPattern + "_";
            if (!slot.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string suffix = slot.Substring(prefix.Length);
            return int.TryParse(suffix, out int index) && index >= 0 && suffix == index.ToString("00");
        }
    }
}
