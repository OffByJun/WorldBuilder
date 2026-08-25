using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Editor.BlenderBridge
{
    /// <summary>
    /// Watches the Blender source root for .chunk.json changes and re-imports affected
    /// chunks automatically (debounced). Toggle via Tools > WorldBuilder > Chunks >
    /// Auto Import or the AutoImport flag on BlenderBridgeSettings.
    /// </summary>
    [InitializeOnLoad]
    public static class ChunkAutoImporter
    {
        private const string TogglePref = "WB_ChunkAutoImport";
        private const int DebounceMilliseconds = 700;

        private static readonly Queue<PendingFile> pending = new Queue<PendingFile>();
        private static FileSystemWatcher watcher;
        private static bool enabled;

        private struct PendingFile
        {
            public string fullPath;
            public DateTime queuedAt;
        }

        static ChunkAutoImporter()
        {
            enabled = EditorPrefs.GetBool(TogglePref, false);
            EditorApplication.update += Tick;
            if (enabled) StartWatcher();
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Auto Import")]
        private static void Toggle()
        {
            SetEnabled(!EditorPrefs.GetBool(TogglePref, false));
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Auto Import", validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/WorldBuilder/Chunks/Auto Import",
                EditorPrefs.GetBool(TogglePref, false));
            return true;
        }

        private static void SetEnabled(bool value)
        {
            EditorPrefs.SetBool(TogglePref, value);
            enabled = value;
            if (value) StartWatcher();
            else StopWatcher();
            Debug.Log($"WorldBuilder chunk auto import: {(value ? "ON" : "OFF")}");
        }

        private static void StartWatcher()
        {
            StopWatcher();

            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            if (bridge == null) return;

            string folder = ToAbsolute(bridge.SourceRoot);
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"[WorldBuilder] Auto import source root does not exist: {folder}");
                return;
            }

            watcher = new FileSystemWatcher(folder, "*.chunk.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Renamed += OnRenamed;
            Debug.Log($"[WorldBuilder] Watching {folder} for chunk changes.");
        }

        private static void StopWatcher()
        {
            if (watcher == null) return;
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
            pending.Clear();
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Enqueue(e.FullPath);
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted) return;
            Enqueue(e.FullPath);
        }

        private static void Enqueue(string fullPath)
        {
            lock (pending)
            {
                pending.Enqueue(new PendingFile { fullPath = fullPath, queuedAt = DateTime.UtcNow });
            }
        }

        private static void Tick()
        {
            if (!enabled || watcher == null) return;
            if (EditorApplication.isUpdating) return;
            if (pending.Count == 0) return;

            List<string> ready = new List<string>();
            lock (pending)
            {
                DateTime now = DateTime.UtcNow;
                while (pending.Count > 0)
                {
                    PendingFile item = pending.Peek();
                    if ((now - item.queuedAt).TotalMilliseconds < DebounceMilliseconds) break;
                    pending.Dequeue();
                    string path = item.fullPath;
                    if (!ready.Contains(path)) ready.Add(path);
                }
            }

            if (ready.Count == 0) return;

            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            if (bridge == null) return;

            AssetDatabase.Refresh();
            int rebuilt = 0;
            foreach (string fullPath in ready)
            {
                string assetPath = FileUtil.GetProjectRelativePath(fullPath).Replace('\\', '/');
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal)) continue;

                WorldBakeReport report = ChunkImportPipeline.Import(assetPath, bridge).Report;
                if (report.HasErrors)
                {
                    Debug.LogError($"[WorldBuilder] Auto import failed for {assetPath}. See report above.");
                }
                else
                {
                    rebuilt++;
                }
                ChunkManifestImporter.LogReport(assetPath, report);
            }

            if (rebuilt > 0)
            {
                RegionCatalogBuilder.RebuildAll(bridge);
                Debug.Log($"[WorldBuilder] Auto import rebuilt {rebuilt}/{ready.Count} chunk(s).");
            }
        }

        private static string ToAbsolute(string assetPath)
        {
            if (Path.IsPathRooted(assetPath)) return Path.GetFullPath(assetPath);
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }
    }
}
