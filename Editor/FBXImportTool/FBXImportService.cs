using UnityEditor;

namespace WorldBuilder.Editor.FBXImportTool
{
    public static class FBXImportService
    {
        public static int Apply(FBXImportPreset preset, string folderPath)
        {
            if (preset == null || string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
            int applied = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                    {
                        continue;
                    }

                    importer.globalScale = preset.scaleFactor;
                    importer.importNormals = preset.importNormals ? preset.normalMode : ModelImporterNormals.None;
                    importer.importTangents = preset.importTangents ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None;
                    importer.addCollider = preset.generateCollider;

                    EditorUtility.SetDirty(importer);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    applied++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            return applied;
        }
    }
}
