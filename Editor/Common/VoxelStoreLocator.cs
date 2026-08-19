using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor
{
    public static class VoxelStoreLocator
    {
        private const string AssetFolder = "Assets/WorldBuilder";
        private const string AssetPath = "Assets/WorldBuilder/VoxelStore.asset";

        public static VoxelStoreAsset LoadOrCreate()
        {
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

            VoxelStoreAsset created = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
