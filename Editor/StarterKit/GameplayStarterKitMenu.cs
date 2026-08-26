using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.StarterKit
{
    /// <summary>
    /// Creates ready-made gameplay prefabs (fishing spot, growable harvestable tree,
    /// gilled fish) so new projects get playable loops in one click.
    /// </summary>
    public static class GameplayStarterKitMenu
    {
        private const string OutputFolder = "Assets/WorldBuilderGenerated/StarterKit";

        [MenuItem("WorldBuilder/Starter Kit/Create Gameplay Prefabs")]
        public static void CreateAll()
        {
            EnsureFolder(OutputFolder);
            CreateFishingSpotPrefab();
            CreateGrowableTreePrefab();
            CreateGilledFishPrefab();
            AssetDatabase.Refresh();
            Debug.Log($"[WorldBuilder] Starter kit prefabs created under {OutputFolder}");
        }

        private static void CreateFishingSpotPrefab()
        {
            var root = new GameObject("FishingSpot");
            var spot = root.AddComponent<FishingSpot>();
            var tableField = typeof(FishingSpot).GetField("table",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tableField.SetValue(spot, new System.Collections.Generic.List<FishingSpot.FishEntry>
            {
                new FishingSpot.FishEntry { itemId = "fish_common", weight = 60, minDepth = 0.5f },
                new FishingSpot.FishEntry { itemId = "fish_rare", weight = 8, minDepth = 6f }
            });
            SavePrefab(root, "FishingSpot");
        }

        private static void CreateGrowableTreePrefab()
        {
            var root = new GameObject("GrowableTree");
            root.AddComponent<GrowableResource>();
            var node = root.AddComponent<HarvestableNode>();

            for (int stage = 0; stage < 3; stage++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Stage_{stage}";
                cube.transform.SetParent(root.transform, false);
                float scale = 0.4f + stage * 0.55f;
                cube.transform.localScale = new Vector3(scale, 0.6f + stage * 1.1f, scale);
                cube.transform.localPosition = new Vector3(0f, (0.6f + stage * 1.1f) * 0.5f, 0f);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            }

            var yieldsField = typeof(HarvestableNode).GetField("yields",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            yieldsField.SetValue(node, new System.Collections.Generic.List<GrowableResource.ItemYield>
            {
                new GrowableResource.ItemYield { itemId = "wood", minAmount = 2, maxAmount = 5 }
            });

            SavePrefab(root, "GrowableTree");
        }

        private static void CreateGilledFishPrefab()
        {
            var root = new GameObject("GilledFish");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.3f, 0.6f, 0.15f);

            root.AddComponent<Rigidbody>().useGravity = false;
            root.AddComponent<WaterDrifter>();
            var breather = root.AddComponent<WaterBreather>();
            var gilledField = typeof(WaterBreather).GetField("gilled",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            gilledField.SetValue(breather, true);

            SavePrefab(root, "GilledFish");
        }

        private static void SavePrefab(GameObject root, string name)
        {
            string path = $"{OutputFolder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
