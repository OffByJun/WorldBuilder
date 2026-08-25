using System.IO;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.Inspectors
{
    /// <summary>
    /// ChunkRoot inspector: shows the source manifest/hash and offers a one-click
    /// re-import through the standard pipeline.
    /// </summary>
    [CustomEditor(typeof(ChunkRoot))]
    public sealed class ChunkRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ChunkRoot root = (ChunkRoot)target;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2IntField("Chunk", new Vector2Int(root.Coordinate.X, root.Coordinate.Z));
                EditorGUILayout.TextField("Source Hash", root.SourceHash);
                EditorGUILayout.TextField("Source Manifest", root.SourceManifest);
            }

            if (string.IsNullOrEmpty(root.SourceManifest)) return;

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping Manifest"))
                {
                    UnityEngine.Object manifest = AssetDatabase.LoadMainAssetAtPath(root.SourceManifest);
                    if (manifest != null) EditorGUIUtility.PingObject(manifest);
                    else Debug.LogWarning($"[WorldBuilder] Manifest not found: {root.SourceManifest}");
                }

                using (new EditorGUI.DisabledScope(FindBridge() == null))
                {
                    if (GUILayout.Button("Re-import"))
                    {
                        Reimport();
                    }
                }
            }
        }

        private void Reimport()
        {
            BlenderBridgeSettings bridge = FindBridge();
            if (bridge == null)
            {
                Debug.LogWarning("[WorldBuilder] No BlenderBridgeSettings asset in project.");
                return;
            }

            string manifestPath = ((ChunkRoot)target).SourceManifest;
            try
            {
                EditorUtility.DisplayProgressBar("WorldBuilder", $"Re-importing {Path.GetFileName(manifestPath)}…", 0.5f);
                ChunkImportResult result = ChunkImportPipeline.Import(manifestPath, bridge);
                ChunkManifestImporter.LogReport(manifestPath, result.Report);
                Debug.Log(result.WasRebuilt
                    ? $"[WorldBuilder] {Path.GetFileName(manifestPath)} rebuilt."
                    : $"[WorldBuilder] {Path.GetFileName(manifestPath)} already up to date.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static BlenderBridgeSettings FindBridge()
        {
            return ChunkManifestImporter.FindSettings(false);
        }
    }
}
