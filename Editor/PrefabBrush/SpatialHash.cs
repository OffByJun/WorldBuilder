using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    public sealed class SpatialHash<T>
    {
        private readonly float cellSize;
        private readonly Dictionary<Vector3Int, List<Entry>> cells = new Dictionary<Vector3Int, List<Entry>>();

        public SpatialHash(float cellSize)
        {
            this.cellSize = Mathf.Max(0.0001f, cellSize);
        }

        public void Add(Vector3 position, T item)
        {
            Vector3Int key = ToCell(position);
            if (!cells.TryGetValue(key, out List<Entry> bucket))
            {
                bucket = new List<Entry>();
                cells[key] = bucket;
            }

            bucket.Add(new Entry { position = position, item = item });
        }

        public void Remove(Vector3 position, T item)
        {
            Vector3Int key = ToCell(position);
            if (!cells.TryGetValue(key, out List<Entry> bucket))
            {
                return;
            }

            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                if (EqualityComparer<T>.Default.Equals(bucket[i].item, item))
                {
                    bucket.RemoveAt(i);
                }
            }

            if (bucket.Count == 0)
            {
                cells.Remove(key);
            }
        }

        public void Clear()
        {
            cells.Clear();
        }

        public List<T> Query(Vector3 center, float radius)
        {
            List<T> result = new List<T>();
            Query(center, radius, result);
            return result;
        }

        /// <summary>Allocation-free variant for hot paths; clears and fills the given buffer.</summary>
        public void Query(Vector3 center, float radius, List<T> results)
        {
            results.Clear();
            float sqrRadius = radius * radius;
            int range = Mathf.CeilToInt(radius / cellSize);
            Vector3Int origin = ToCell(center);

            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int key = new Vector3Int(origin.x + x, origin.y + y, origin.z + z);
                        if (!cells.TryGetValue(key, out List<Entry> bucket))
                        {
                            continue;
                        }

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            if ((bucket[i].position - center).sqrMagnitude <= sqrRadius)
                            {
                                results.Add(bucket[i].item);
                            }
                        }
                    }
                }
            }
        }

        private Vector3Int ToCell(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x / cellSize),
                Mathf.FloorToInt(position.y / cellSize),
                Mathf.FloorToInt(position.z / cellSize));
        }

        private struct Entry
        {
            public Vector3 position;
            public T item;
        }
    }
}
