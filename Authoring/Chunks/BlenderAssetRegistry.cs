using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Authoring.Chunks
{
    [Serializable]
    public sealed class BlenderAssetRegistryEntry
    {
        [SerializeField] private string assetId = string.Empty;
        [SerializeField] private GameObject prefab;
        public string AssetId => assetId;
        public GameObject Prefab => prefab;
        public BlenderAssetRegistryEntry(string id, GameObject value) { assetId = id ?? string.Empty; prefab = value; }
    }

    [CreateAssetMenu(menuName = "WorldBuilder/Blender/Asset Registry", fileName = "BlenderAssetRegistry")]
    public sealed class BlenderAssetRegistry : ScriptableObject
    {
        [SerializeField] private List<BlenderAssetRegistryEntry> entries = new List<BlenderAssetRegistryEntry>();
        private Dictionary<string, GameObject> lookup;

        public IReadOnlyList<BlenderAssetRegistryEntry> Entries => entries;

        public bool TryGetPrefab(string assetId, out GameObject prefab)
        {
            EnsureLookup();
            return lookup.TryGetValue(assetId ?? string.Empty, out prefab) && prefab != null;
        }

        public void Configure(IEnumerable<BlenderAssetRegistryEntry> values)
        {
            entries.Clear();
            if (values != null) entries.AddRange(values);
            entries.Sort((left, right) => string.CompareOrdinal(left?.AssetId, right?.AssetId));
            lookup = null;
        }

        private void OnValidate() => lookup = null;

        private void EnsureLookup()
        {
            if (lookup != null) return;
            lookup = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                BlenderAssetRegistryEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.AssetId) || lookup.ContainsKey(entry.AssetId)) continue;
                lookup.Add(entry.AssetId, entry.Prefab);
            }
        }
    }
}
