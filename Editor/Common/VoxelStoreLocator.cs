using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public static class VoxelStoreLocator
    {
        private const string AssetFolder = "Assets/WorldBuilder";
        private const string AssetPath = "Assets/WorldBuilder/VoxelStore.asset";

        public static VoxelStoreAsset LoadOrCreate()
        {
            VoxelStoreAsset defaultStore = AssetDatabase.LoadAssetAtPath<VoxelStoreAsset>(AssetPath);
            if (defaultStore != null)
            {
                return defaultStore;
            }

            string[] found = AssetDatabase.FindAssets("t:VoxelStoreAsset");
            if (found.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(found[0]);
                VoxelStoreAsset existing = AssetDatabase.LoadAssetAtPath<VoxelStoreAsset>(path);
                if (existing != null)
                {
                    return existing;
                }
            }

            if (!AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.CreateFolder("Assets", "WorldBuilder");
            }

            // CreateAsset tries to delete an existing file before writing. Never do that from
            // an InitializeOnLoad path: a stale/broken asset may still contain user world data.
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(AssetPath)))
            {
                Debug.LogError(
                    $"WorldBuilder found an asset at '{AssetPath}', but it is not a valid {nameof(VoxelStoreAsset)}. " +
                    "The existing file was preserved.");
                return ScriptableObject.CreateInstance<VoxelStoreAsset>();
            }

            VoxelStoreAsset created = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
