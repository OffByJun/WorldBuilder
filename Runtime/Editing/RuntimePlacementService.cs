using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Editing
{
    /// <summary>
    /// Runtime world editing foundation: places and removes GameObject-based structures
    /// at runtime with deterministic bookkeeping. Entity (DOTS) based spawning should go
    /// through WorldEntityCommandQueue instead.
    /// </summary>
    public static class RuntimePlacementService
    {
        public sealed class PlacementRecord
        {
            public int PlacementId { get; }
            public string PrefabId { get; }
            public GameObject Instance { get; internal set; }

            internal PlacementRecord(int placementId, string prefabId, GameObject instance)
            {
                PlacementId = placementId;
                PrefabId = prefabId;
                Instance = instance;
            }
        }

        private static readonly Dictionary<int, PlacementRecord> records = new Dictionary<int, PlacementRecord>();
        private static readonly Dictionary<string, List<int>> byPrefab = new Dictionary<string, List<int>>();
        private static Transform root;
        private static int nextId = 1;

        public static IReadOnlyDictionary<int, PlacementRecord> Records => records;

        public static event Action<PlacementRecord> Placed;
        public static event Action<PlacementRecord> Removed;

        public static Transform Root
        {
            get
            {
                if (root == null)
                {
                    GameObject existing = GameObject.Find("__WorldBuilder_RuntimeEdits");
                    root = existing != null ? existing.transform : new GameObject("__WorldBuilder_RuntimeEdits").transform;
                    if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root.gameObject);
                }

                return root;
            }
        }

        public static void Reset()
        {
            foreach (KeyValuePair<int, PlacementRecord> record in records)
            {
                if (record.Value.Instance != null) DestroyInstance(record.Value);
            }

            records.Clear();
            byPrefab.Clear();
        }

        public static PlacementRecord Place(GameObject prefab, Vector3 position, Quaternion rotation,
            float uniformScale = 1f, string prefabId = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                position,
                rotation,
                Root);
            instance.name = prefab.name;
            float scale = Mathf.Max(0.0001f, uniformScale);
            instance.transform.localScale *= scale;

            int id = nextId++;
            PlacementRecord record = new PlacementRecord(id, string.IsNullOrEmpty(prefabId) ? prefab.name : prefabId, instance);
            records.Add(id, record);

            if (!byPrefab.TryGetValue(record.PrefabId, out List<int> list))
            {
                list = new List<int>();
                byPrefab[record.PrefabId] = list;
            }

            list.Add(id);
            Placed?.Invoke(record);
            return record;
        }

        public static bool Remove(int placementId)
        {
            if (!records.TryGetValue(placementId, out PlacementRecord record)) return false;
            return Remove(record);
        }

        public static bool TryGetInstanceRecord(GameObject instance, out PlacementRecord record)
        {
            record = null;
            if (instance == null) return false;
            Transform current = instance.transform;
            while (current != null)
            {
                foreach (KeyValuePair<int, PlacementRecord> pair in records)
                {
                    if (pair.Value.Instance == current.gameObject)
                    {
                        record = pair.Value;
                        return true;
                    }
                }
                current = current.parent;
            }
            return false;
        }

        #region Persistence

        [Serializable]
        private sealed class SerializedPlacement
        {
            public string prefabId;
            public float px, py, pz;
            public float rx, ry, rz, rw;
            public float scale = 1f;
        }

        [Serializable]
        private sealed class PlacementSnapshot
        {
            public List<SerializedPlacement> placements = new List<SerializedPlacement>();
        }

        /// <summary>Serializes every active placement to JSON for save games.</summary>
        public static string ToJson()
        {
            PlacementSnapshot snapshot = new PlacementSnapshot();
            foreach (KeyValuePair<int, PlacementRecord> pair in records)
            {
                GameObject instance = pair.Value.Instance;
                if (instance == null) continue;
                Transform t = instance.transform;
                snapshot.placements.Add(new SerializedPlacement
                {
                    prefabId = pair.Value.PrefabId,
                    px = t.position.x, py = t.position.y, pz = t.position.z,
                    rx = t.rotation.x, ry = t.rotation.y, rz = t.rotation.z, rw = t.rotation.w,
                    scale = t.localScale.x
                });
            }
            return JsonUtility.ToJson(snapshot);
        }

        /// <summary>
        /// Restores placements from JSON. <paramref name="resolver"/> maps stored prefab ids
        /// back to prefabs; unknown ids are skipped and counted in the return value.
        /// </summary>
        public static int RestoreFromJson(string json, Func<string, GameObject> resolver)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            PlacementSnapshot snapshot = JsonUtility.FromJson<PlacementSnapshot>(json);
            if (snapshot?.placements == null) return 0;

            int restored = 0;
            for (int i = 0; i < snapshot.placements.Count; i++)
            {
                SerializedPlacement data = snapshot.placements[i];
                GameObject prefab = resolver(data.prefabId);
                if (prefab == null) continue;
                Place(prefab,
                    new Vector3(data.px, data.py, data.pz),
                    new Quaternion(data.rx, data.ry, data.rz, data.rw),
                    Mathf.Max(0.0001f, data.scale),
                    data.prefabId);
                restored++;
            }
            return restored;
        }

        #endregion

        public static bool RemoveNearest(Vector3 position, float maxDistance, out PlacementRecord removed)
        {
            removed = null;
            float bestDistance = maxDistance;
            foreach (KeyValuePair<int, PlacementRecord> pair in records)
            {
                GameObject instance = pair.Value.Instance;
                if (instance == null) continue;
                float distance = Vector3.Distance(instance.transform.position, position);
                if (!(distance < bestDistance)) continue;
                bestDistance = distance;
                removed = pair.Value;
            }

            return removed != null && Remove(removed);
        }

        private static bool Remove(PlacementRecord record)
        {
            records.Remove(record.PlacementId);
            if (byPrefab.TryGetValue(record.PrefabId, out List<int> list))
            {
                list.Remove(record.PlacementId);
                if (list.Count == 0) byPrefab.Remove(record.PrefabId);
            }

            if (record.Instance != null) DestroyInstance(record);
            record.Instance = null;
            Removed?.Invoke(record);
            return true;
        }

        private static void DestroyInstance(PlacementRecord record)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(record.Instance);
            else UnityEngine.Object.DestroyImmediate(record.Instance);
        }
    }
}
