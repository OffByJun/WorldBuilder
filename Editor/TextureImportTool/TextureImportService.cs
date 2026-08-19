using UnityEditor;

namespace WorldBuilder.Editor.TextureImportTool
{
    public static class TextureImportService
    {
        public static int Apply(TextureImportPreset preset, string folderPath)
        {
            if (preset == null || string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { folderPath });
            int applied = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    {
                        continue;
                    }

                    importer.textureCompression = preset.compression;
                    importer.mipmapEnabled = preset.generateMipMaps;
                    importer.maxTextureSize = preset.maxSize;
                    importer.textureType = preset.isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;

                    TextureImporterPlatformSettings platformSettings = importer.GetDefaultPlatformTextureSettings();
                    platformSettings.format = preset.format;
                    platformSettings.maxTextureSize = preset.maxSize;
                    platformSettings.textureCompression = preset.compression;
                    importer.SetPlatformTextureSettings(platformSettings);

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
