using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    /// <summary>One exported world data entry, editor-type agnostic.</summary>
    [Serializable]
    public sealed class WorldDataRecord
    {
        public string kind = string.Empty;
        public string id = string.Empty;
        public string displayName = string.Empty;
        public Vector3 position;
        public float value;

        public WorldDataRecord() { }

        public WorldDataRecord(string kind, string id, string displayName, Vector3 position, float value = 0f)
        {
            this.kind = kind ?? string.Empty;
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.position = position;
            this.value = value;
        }
    }

    /// <summary>
    /// Runtime-readable export of the editor WorldDataStore. Exported by
    /// Tools &gt; WorldBuilder &gt; Export World Data Snapshot and consumed by
    /// <see cref="WorldDataRuntimeLoader"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/World Data Snapshot", fileName = "WorldDataSnapshot")]
    public sealed class WorldDataSnapshot : ScriptableObject
    {
        [SerializeField] private List<WorldDataRecord> records = new List<WorldDataRecord>();

        public IReadOnlyList<WorldDataRecord> Records => records;

        public void Configure(IEnumerable<WorldDataRecord> values)
        {
            records = new List<WorldDataRecord>();
            if (values != null) records.AddRange(values);
        }
    }
}
