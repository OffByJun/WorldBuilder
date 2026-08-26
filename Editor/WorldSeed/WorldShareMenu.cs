using System.IO;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.WorldSeed;

namespace WorldBuilder.Editor.WorldSeed
{
    /// <summary>World map PNG export + shareable seed file menus.</summary>
    public static class WorldShareMenu
    {
        private const float ChunkSize = 128f;
        private const float SeaLevel = 0f;

        [MenuItem("WorldBuilder/Export/Strategy World Map (PNG)")]
        public static void ExportWorldMap()
        {
            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset found.");
                return;
            }

            // Bounds from existing chunk data.
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            bool any = false;
            foreach (Vector3Int coord in store.Coords)
            {
                any = true;
                minX = Mathf.Min(minX, coord.x);
                maxX = Mathf.Max(maxX, coord.x);
                minZ = Mathf.Min(minZ, coord.z);
                maxZ = Mathf.Max(maxZ, coord.z);
            }
            if (!any)
            {
                Debug.LogWarning("[WorldBuilder] Store is empty — generate terrain first.");
                return;
            }

            Vector2 origin = new Vector2(minX * ChunkSize, minZ * ChunkSize);
            float sizeMeters = Mathf.Max(maxX - minX + 1, maxZ - minZ + 1) * ChunkSize;
            int resolutionPx = Mathf.Clamp((int)sizeMeters / 2, 256, 2048);

            EditorUtility.DisplayProgressBar("World Map", "Baking overview…", 0.5f);
            Texture2D texture = WorldMapBaker.BakeOverviewTexture(
                store, ChunkSize, FindAsset<HighResBiomeMap>(), origin, resolutionPx,
                sizeMeters, SeaLevel);
            EditorUtility.ClearProgressBar();

            string path = EditorUtility.SaveFilePanel("Save World Map", "",
                "world_map.png", "png");
            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(texture);
                return;
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();
            Debug.Log($"[WorldBuilder] World map saved to {path}");
        }

        [MenuItem("WorldBuilder/Export/World Seed File")]
        public static void ExportSeed()
        {
            TerrainShapeParams shape = FindAsset<TerrainShapeParams>();
            if (shape == null)
            {
                Debug.LogWarning("[WorldBuilder] No TerrainShapeParams asset found.");
                return;
            }
            CaveShapeParams caves = FindAsset<CaveShapeParams>();

            string json = WorldSeedCodec.Export(shape, caves);
            string path = EditorUtility.SaveFilePanel("Save World Seed", "",
                $"world_seed_{shape.seed}.wbseed", "wbseed");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, json);
            Debug.Log($"[WorldBuilder] Seed exported (fingerprint {WorldSeedCodec.Fingerprint(shape)}): {path}");
        }

        [MenuItem("WorldBuilder/Import/World Seed File")]
        public static void ImportSeed()
        {
            string path = EditorUtility.OpenFilePanel("Open World Seed", "", "wbseed,json");
            if (string.IsNullOrEmpty(path)) return;

            TerrainShapeParams shape = FindOrCreate<TerrainShapeParams>("ImportedTerrainShape");
            CaveShapeParams caves = FindOrCreate<CaveShapeParams>("ImportedCaveShape");

            if (!WorldSeedCodec.TryImport(File.ReadAllText(path), shape, caves, out string error))
            {
                EditorUtility.DisplayDialog("World Seed Import", error, "OK");
                return;
            }
            EditorUtility.SetDirty(shape);
            EditorUtility.SetDirty(caves);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WorldBuilder] Seed imported → {AssetDatabase.GetAssetPath(shape)} " +
                      $"(fingerprint {WorldSeedCodec.Fingerprint(shape)}). Re-generate terrain to apply.");
        }

        private static T FindAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }

        private static T FindOrCreate<T>(string name) where T : ScriptableObject
        {
            T existing = FindAsset<T>();
            if (existing != null) return existing;

            T created = ScriptableObject.CreateInstance<T>();
            created.name = name;
            AssetDatabase.CreateAsset(created, $"Assets/{name}.asset");
            return created;
        }
    }
}
