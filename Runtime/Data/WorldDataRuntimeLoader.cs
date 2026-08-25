using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    /// <summary>
    /// Loads a <see cref="WorldDataSnapshot"/> at play: raises per-record events and
    /// optionally instantiates kind-mapped prefabs. Attach to any scene object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldDataRuntimeLoader : MonoBehaviour
    {
        [Serializable]
        public sealed class KindBinding
        {
            public string kind = string.Empty;
            public GameObject prefab;
        }

        [SerializeField] private WorldDataSnapshot snapshot;
        [SerializeField] private bool loadOnStart = true;
        [SerializeField] private List<KindBinding> prefabBindings = new List<KindBinding>();

        private readonly Dictionary<string, GameObject> bindingLookup =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public bool HasLoaded { get; private set; }
        public int LoadedCount { get; private set; }

        public event Action<WorldDataRecord> RecordLoaded;

        public WorldDataSnapshot Snapshot
        {
            get => snapshot;
            set => snapshot = value;
        }

        private void Start()
        {
            if (loadOnStart) Load();
        }

        public void Load()
        {
            if (HasLoaded) return;
            if (snapshot == null)
            {
                Debug.LogWarning("[WorldBuilder] WorldDataRuntimeLoader has no snapshot assigned.");
                return;
            }

            BuildLookup();
            IReadOnlyList<WorldDataRecord> records = snapshot.Records;
            for (int i = 0; i < records.Count; i++)
            {
                WorldDataRecord record = records[i];
                if (record == null) continue;

                if (bindingLookup.TryGetValue(record.kind, out GameObject prefab) && prefab != null)
                {
                    GameObject instance = Instantiate(prefab, record.position, Quaternion.identity, transform);
                    instance.name = string.IsNullOrEmpty(record.displayName) ? record.kind : record.displayName;
                }

                RecordLoaded?.Invoke(record);
                LoadedCount++;
            }

            HasLoaded = true;
            Debug.Log($"[WorldBuilder] World data loaded: {LoadedCount} record(s).");
        }

        public bool TryGetPrefab(string kind, out GameObject prefab)
        {
            BuildLookup();
            return bindingLookup.TryGetValue(kind ?? string.Empty, out prefab) && prefab != null;
        }

        /// <summary>Registers a kind→prefab mapping at runtime (code-driven setup).</summary>
        public void AddKindBinding(string kind, GameObject prefab)
        {
            if (string.IsNullOrEmpty(kind) || prefab == null) return;
            prefabBindings.Add(new KindBinding { kind = kind, prefab = prefab });
            lookupBuilt = false;
        }

        private void BuildLookup()
        {
            if (lookupBuilt) return;
            lookupBuilt = true;

            for (int i = 0; i < prefabBindings.Count; i++)
            {
                KindBinding binding = prefabBindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.kind) || binding.prefab == null) continue;
                bindingLookup[binding.kind] = binding.prefab;
            }
        }

        private bool lookupBuilt;
    }
}
