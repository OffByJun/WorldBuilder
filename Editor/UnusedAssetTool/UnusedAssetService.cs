using System.Collections.Generic;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace WorldBuilder.Editor.UnusedAssetTool
{
    public static class UnusedAssetService
    {
        public static List<string> Scan(string folderPath)
        {
            List<string> unused = new List<string>();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return unused;
            }

            string scenePath = SceneManager.GetActiveScene().path;
            HashSet<string> referenced = new HashSet<string>();
            if (!string.IsNullOrEmpty(scenePath))
            {
                string[] dependencies = AssetDatabase.GetDependencies(scenePath, true);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    referenced.Add(dependencies[i]);
                }
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                if (path.EndsWith(".cs"))
                {
                    continue;
                }

                if (!referenced.Contains(path) && !unused.Contains(path))
                {
                    unused.Add(path);
                }
            }

            return unused;
        }
    }
}
