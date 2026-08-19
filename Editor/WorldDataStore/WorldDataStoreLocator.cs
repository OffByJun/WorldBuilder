using UnityEditor;

namespace WorldBuilder.Editor
{
    public static class WorldDataStoreLocator
    {
        private static WorldDataStore activeStore;

        public static WorldDataStore Active
        {
            get
            {
                if (activeStore == null)
                    activeStore = FindFirst();
                return activeStore;
            }
            set => activeStore = value;
        }

        private static WorldDataStore FindFirst()
        {
            string[] guids = AssetDatabase.FindAssets("t:WorldDataStore");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<WorldDataStore>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
