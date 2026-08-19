using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [Serializable]
    public struct PrefabEntry
    {
        public int id;
        public GameObject prefab;
    }

    [CreateAssetMenu(fileName = "PrefabRegistry", menuName = "WorldBuilder/PrefabRegistry")]
    public sealed class PrefabRegistry : ScriptableObject
    {
        [SerializeField] private List<PrefabEntry> prefabs = new List<PrefabEntry>();

        private Dictionary<int, GameObject> lookup;

        private Dictionary<int, GameObject> Lookup
        {
            get
            {
                if (lookup == null)
                {
                    RebuildLookup();
                }

                return lookup;
            }
        }

        public GameObject Get(int prefabId)
        {
            return Lookup[prefabId];
        }

        public bool TryGet(int prefabId, out GameObject prefab)
        {
            return Lookup.TryGetValue(prefabId, out prefab);
        }

        private void RebuildLookup()
        {
            lookup = new Dictionary<int, GameObject>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                PrefabEntry entry = prefabs[i];
                if (entry.prefab != null && !lookup.ContainsKey(entry.id))
                {
                    lookup[entry.id] = entry.prefab;
                }
            }
        }
    }
}
