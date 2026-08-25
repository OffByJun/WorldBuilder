using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.WorldSnapshotExporter
{
    /// <summary>
    /// Exports the active editor WorldDataStore into a runtime-readable
    /// WorldDataSnapshot asset (deterministic order, editor types stripped).
    /// </summary>
    public static class WorldSnapshotExporter
    {
        private const string DefaultPath = "Assets/WorldBuilder/WorldDataSnapshot.asset";

        [MenuItem("Tools/WorldBuilder/Export World Data Snapshot")]
        public static void Export()
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No active WorldDataStore asset. Assign one in the World Data Browser.");
                return;
            }

            string path = DefaultPath;

            List<WorldDataRecord> records = new List<WorldDataRecord>();
            foreach (KeyValuePair<Type, List<IWorldDataEntry>> category in store.GetAllCategories())
            {
                string kind = KindName(category.Key);
                for (int i = 0; i < category.Value.Count; i++)
                {
                    IWorldDataEntry entry = category.Value[i];
                    if (entry == null || !entry.Enabled) continue;
                    records.Add(new WorldDataRecord(kind, entry.Id, entry.DisplayName, entry.Position));
                }
            }

            // Deterministic order: kind, then id.
            records.Sort((left, right) =>
            {
                int byKind = string.CompareOrdinal(left.kind, right.kind);
                return byKind != 0 ? byKind : string.CompareOrdinal(left.id, right.id);
            });

            WorldDataSnapshot snapshot = AssetDatabase.LoadAssetAtPath<WorldDataSnapshot>(path);
            bool created = snapshot == null;
            if (created)
            {
                ChunkImportPipeline.EnsureFolder("Assets/WorldBuilder");
                snapshot = ScriptableObject.CreateInstance<WorldDataSnapshot>();
                AssetDatabase.CreateAsset(snapshot, path);
            }
            else
            {
                Undo.RecordObject(snapshot, "Export World Data Snapshot");
            }

            snapshot.Configure(records);
            EditorUtility.SetDirty(snapshot);
            AssetDatabase.SaveAssets();

            Debug.Log($"[WorldBuilder] {(created ? "Created" : "Updated")} {path} with {records.Count} record(s).");
        }

        private static string KindName(Type entryType)
        {
            string name = entryType.Name;
            const string suffix = "Entry";
            return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }
    }
}
