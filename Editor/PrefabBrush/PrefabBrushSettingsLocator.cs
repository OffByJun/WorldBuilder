using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    public static class PrefabBrushSettingsLocator
    {
        private const string AssetFolder = "Assets/WorldBuilder";
        private const string AssetPath = "Assets/WorldBuilder/PrefabBrushSettings.asset";

        public static PrefabBrushSettings LoadOrCreate()
        {
            string[] found = AssetDatabase.FindAssets("t:PrefabBrushSettings");
            if (found.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(found[0]);
                PrefabBrushSettings existing = AssetDatabase.LoadAssetAtPath<PrefabBrushSettings>(path);
                if (existing != null)
                {
                    return existing;
                }
            }

            if (!AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.CreateFolder("Assets", "WorldBuilder");
            }

            PrefabBrushSettings created = ScriptableObject.CreateInstance<PrefabBrushSettings>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
