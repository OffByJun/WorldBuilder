using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// Registry so mod files can reach every placed spot/node without scene scans.
    /// </summary>
    public static class GameplayRegistry
    {
        private static readonly List<FishingSpot> spots = new List<FishingSpot>();
        public static IReadOnlyList<FishingSpot> FishingSpots => spots;
        public static void Register(FishingSpot spot) { if (spot != null && !spots.Contains(spot)) spots.Add(spot); }
        public static void Unregister(FishingSpot spot) => spots.Remove(spot);
    }

    [Serializable]
    public class ModDocument
    {
        public ModFishEntry[] fish = Array.Empty<ModFishEntry>();
        public float growthSecondsPerStage = 600f;
        public ModYield[] harvestYields = Array.Empty<ModYield>();
        public Vector2 biteDelaySeconds = new Vector2(3f, 9f);
    }

    [Serializable]
    public class ModFishEntry
    {
        public string itemId = "fish";
        public int weight = 10;
        public float minDepth;
        public float maxDepth = 999f;
    }

    [Serializable]
    public class ModYield
    {
        public string itemId = "loot";
        public int minAmount = 1;
        public int maxAmount = 2;
    }

    /// <summary>
    /// Data-driven modding layer: drop JSON documents under
    /// <c>persistentDataPath/WorldBuilder/Mods/*.json</c> (or feed strings directly) and every
    /// registered FishingSpot / GrowableResource default / HarvestableNode yield set is
    /// reconfigured — no recompile, no scene edits.
    /// </summary>
    public static class ContentModLoader
    {
        public static event Action<string> ModApplied;

        public static ModDocument Parse(string json) => JsonUtility.FromJson<ModDocument>(json);

        public static bool Apply(string json, out string error)
        {
            error = null;
            ModDocument document;
            try
            {
                document = Parse(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (document == null) { error = "Empty mod document."; return false; }

            Apply(document);
            return true;
        }

        public static void Apply(ModDocument document)
        {
            if (document == null) return;

            if (document.fish is { Length: > 0 })
            {
                foreach (FishingSpot spot in GameplayRegistry.FishingSpots)
                    ApplyFishTable(spot, document.fish, document.biteDelaySeconds);
            }

            GrowableResource.DefaultsSecondsPerStage = document.growthSecondsPerStage;

            if (document.harvestYields is { Length: > 0 })
                HarvestableNode.DefaultYields = new List<GrowableResource.ItemYield>(
                    ToItemYields(document.harvestYields));

            ModApplied?.Invoke("applied");
        }

        public static void ApplyFishTable(FishingSpot spot, ModFishEntry[] entries,
            Vector2 biteDelay)
        {
            if (spot == null || entries == null || entries.Length == 0) return;
            var tableField = typeof(FishingSpot).GetField("table",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var converted = new List<FishingSpot.FishEntry>(entries.Length);
            foreach (ModFishEntry entry in entries)
                converted.Add(new FishingSpot.FishEntry
                {
                    itemId = entry.itemId,
                    weight = Mathf.Max(1, entry.weight),
                    minDepth = entry.minDepth,
                    maxDepth = entry.maxDepth
                });
            tableField?.SetValue(spot, converted);

            var delayField = typeof(FishingSpot).GetField("biteDelaySeconds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            delayField?.SetValue(spot, biteDelay);
        }

        private static IEnumerable<GrowableResource.ItemYield> ToItemYields(ModYield[] yields)
        {
            foreach (ModYield entry in yields)
                yield return new GrowableResource.ItemYield
                {
                    itemId = entry.itemId,
                    minAmount = Mathf.Max(0, entry.minAmount),
                    maxAmount = Mathf.Max(entry.minAmount, entry.maxAmount)
                };
        }
    }

    /// <summary>Editor convenience: applies every mod file in a folder.</summary>
#if UNITY_EDITOR
    public static class ContentModMenu
    {
        [UnityEditor.MenuItem("WorldBuilder/Mods/Apply From Folder")]
        public static void ApplyFromFolder()
        {
            string directory = UnityEditor.EditorUtility.OpenFolderPanel(
                "Select mod folder", System.IO.Path.Combine(
                    Application.persistentDataPath, "WorldBuilder", "Mods"), "");
            if (string.IsNullOrEmpty(directory)) return;

            int applied = 0;
            foreach (string file in System.IO.Directory.GetFiles(directory, "*.json"))
            {
                if (ContentModLoader.Apply(System.IO.File.ReadAllText(file), out string error))
                    applied++;
                else
                    Debug.LogWarning($"[WorldBuilder] Mod '{file}' failed: {error}");
            }
            Debug.Log($"[WorldBuilder] Applied {applied} mod file(s) from {directory}");
        }
    }
#endif
}
