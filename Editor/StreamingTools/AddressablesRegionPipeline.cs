#if WB_ADDRESSABLES_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
#endif
using UnityEngine;
using UnityEditor;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Editor.StreamingTools
{
    /// <summary>
    /// Region → Addressables automation: one group per region, chunk prefabs under
    /// Assets/WorldBuilderGenerated/Regions/R_x_z become addressable entries named
    /// "R_x_z/chunkName", then the default build script runs. Guarded so projects without
    /// Addressables compile fine.
    /// </summary>
    public static class AddressablesRegionPipeline
    {
        private const string RegionsRoot = "Assets/WorldBuilderGenerated/Regions";
        private const string GroupPrefix = "WB_Region_";

#if WB_ADDRESSABLES_EDITOR
        public static string PrepareGroups(WorldGridSettings grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var settings = AddressableAssetSettings.DefaultObject;
            if (settings == null)
                throw new InvalidOperationException(
                    "No AddressableAssetSettings found. Enable Addressables in the project first.");

            int regions = 0;
            if (!Directory.Exists(RegionsRoot))
            {
                Debug.LogWarning($"[WorldBuilder] '{RegionsRoot}' not found — nothing to address.");
                return "no-regions-folder";
            }

            foreach (string directory in Directory.GetDirectories(RegionsRoot, "R_*"))
            {
                string regionName = Path.GetFileName(directory);
                AddressableAssetGroup group = settings.FindGroup(GroupPrefix + regionName)
                    ?? settings.CreateGroup(GroupPrefix + regionName, setAsDefault: false,
                        readOnly: false, postEvent: true,
                        schemasToCopy: null);
                EnsureBundledSchema(group);

                foreach (string prefabPath in Directory.GetFiles(directory, "*.prefab",
                             SearchOption.TopDirectoryOnly))
                {
                    string assetPath = prefabPath.Replace('\\', '/');
                    string address = regionName + "/" + Path.GetFileNameWithoutExtension(prefabPath);
                    settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath),
                        group, readOnly: false, postEvent: true);
                    AddressableAssetEntry entry =
                        settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath));
                    entry?.SetAddress(address);
                }
                regions++;
            }

            Debug.Log($"[WorldBuilder] Prepared {regions} region group(s) under prefix '{GroupPrefix}'.");
            return $"{regions} groups";
        }

        public static string BuildContent()
        {
            var settings = AddressableAssetSettings.DefaultObject;
            if (settings == null) throw new InvalidOperationException("Addressables not enabled.");

            // BuildPlayerContent has shifted between versions/static-instance forms;
            // resolve whichever exists so this compiles against any Addressables release.
            var settingsType = typeof(AddressableAssetSettings);
            var method = settingsType.GetMethod("BuildPlayerContent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static, null, Type.EmptyTypes, null)
                ?? settingsType.GetMethod("BuildPlayerContentImpl",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.Static, null, Type.EmptyTypes, null);

            if (method == null)
                throw new InvalidOperationException(
                    "No compatible BuildPlayerContent method found in this Addressables version.");

            object target = method.IsStatic ? null : settings;
            object result = method.Invoke(target, null);
            string text = result?.ToString() ?? "completed";
            Debug.Log($"[WorldBuilder] Addressables build finished:\n{text}");
            return text;
        }

        private static void EnsureBundledSchema(AddressableAssetGroup group)
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema != null) return;
            group.AddSchema<BundledAssetGroupSchema>();
        }
#endif

        [MenuItem("WorldBuilder/Streaming/Prepare Region Addressable Groups")]
        public static void PrepareMenu()
        {
#if WB_ADDRESSABLES_EDITOR
            WorldGridSettings grid = FindGrid();
            try { PrepareGroups(grid); }
            catch (Exception exception) { Debug.LogWarning("[WorldBuilder] " + exception.Message); }
#else
            Debug.LogWarning("[WorldBuilder] Install com.unity.addressables to use region groups.");
#endif
        }

        [MenuItem("WorldBuilder/Streaming/Build Addressables Content")]
        public static void BuildMenu()
        {
#if WB_ADDRESSABLES_EDITOR
            try { BuildContent(); }
            catch (Exception exception) { Debug.LogWarning("[WorldBuilder] " + exception.Message); }
#else
            Debug.LogWarning("[WorldBuilder] Install com.unity.addressables to build content.");
#endif
        }

        private static WorldGridSettings FindGrid()
        {
            string[] guids = AssetDatabase.FindAssets("t:WorldGridSettings");
            foreach (string guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<WorldGridSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
