using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor
{
    [CreateAssetMenu(menuName = "WorldBuilder/WorldDataStore")]
    public sealed class WorldDataStore : ScriptableObject
    {
        [SerializeField] private List<WorldDataCategory> categories = new List<WorldDataCategory>();

        private Dictionary<Type, List<IWorldDataEntry>> cache;

        private void OnEnable()
        {
            BuildCache();
        }

        private void BuildCache()
        {
            cache = new Dictionary<Type, List<IWorldDataEntry>>();
            for (int i = 0; i < categories.Count; i++)
            {
                List<IWorldDataEntry> entries = categories[i].Entries;
                for (int j = 0; j < entries.Count; j++)
                {
                    IWorldDataEntry entry = entries[j];
                    if (entry == null) continue;
                    Type t = entry.GetType();
                    if (!cache.TryGetValue(t, out List<IWorldDataEntry> list))
                    {
                        list = new List<IWorldDataEntry>();
                        cache[t] = list;
                    }
                    list.Add(entry);
                }
            }
        }

        public void Add<T>(T entry) where T : IWorldDataEntry
        {
            if (entry == null) return;
            string typeName = typeof(T).Name;
            WorldDataCategory category = FindOrCreateCategory(typeName);
            category.Entries.Add(entry);

            Type t = typeof(T);
            if (!cache.TryGetValue(t, out List<IWorldDataEntry> cacheList))
            {
                cacheList = new List<IWorldDataEntry>();
                cache[t] = cacheList;
            }
            cacheList.Add(entry);
        }

        public void Remove<T>(string id) where T : IWorldDataEntry
        {
            string typeName = typeof(T).Name;
            WorldDataCategory category = FindCategory(typeName);
            if (category == null) return;
            category.Entries.RemoveAll(e => e != null && e.Id == id);

            if (cache.TryGetValue(typeof(T), out List<IWorldDataEntry> cacheList))
                cacheList.RemoveAll(e => e != null && e.Id == id);
        }

        public void RemoveWhere<T>(Func<T, bool> predicate) where T : IWorldDataEntry
        {
            string typeName = typeof(T).Name;
            WorldDataCategory category = FindCategory(typeName);
            if (category == null) return;

            List<string> toRemove = new List<string>();
            for (int i = 0; i < category.Entries.Count; i++)
            {
                if (category.Entries[i] is T typed && predicate(typed))
                    toRemove.Add(typed.Id);
            }

            foreach (string id in toRemove)
            {
                category.Entries.RemoveAll(e => e != null && e.Id == id);
                if (cache.TryGetValue(typeof(T), out List<IWorldDataEntry> cacheList))
                    cacheList.RemoveAll(e => e != null && e.Id == id);
            }
        }

        public IReadOnlyList<IWorldDataEntry> GetAll<T>() where T : IWorldDataEntry
        {
            if (cache == null) BuildCache();
            return cache.TryGetValue(typeof(T), out List<IWorldDataEntry> list)
                ? list
                : new List<IWorldDataEntry>();
        }

        public IReadOnlyDictionary<Type, List<IWorldDataEntry>> GetAllCategories()
        {
            if (cache == null) BuildCache();
            return cache;
        }

        public int GetTotalCount()
        {
            if (cache == null) BuildCache();
            int total = 0;
            foreach (List<IWorldDataEntry> list in cache.Values)
                total += list.Count;
            return total;
        }

        public int GetCount<T>() where T : IWorldDataEntry
        {
            if (cache == null) BuildCache();
            return cache.TryGetValue(typeof(T), out List<IWorldDataEntry> list) ? list.Count : 0;
        }

        private WorldDataCategory FindCategory(string typeName)
        {
            for (int i = 0; i < categories.Count; i++)
                if (categories[i].TypeName == typeName) return categories[i];
            return null;
        }

        private WorldDataCategory FindOrCreateCategory(string typeName)
        {
            WorldDataCategory existing = FindCategory(typeName);
            if (existing != null) return existing;
            WorldDataCategory newCategory = new WorldDataCategory { TypeName = typeName };
            categories.Add(newCategory);
            return newCategory;
        }
    }
}
