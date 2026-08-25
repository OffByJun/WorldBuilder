using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// Raises enter/exit events when the followed transform comes within range of
    /// world data records (POIs, loot containers...). Bridge these events into
    /// MessagePipe/VContainer in game code for quest or loot systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoiProximityTracker : MonoBehaviour
    {
        [SerializeField] private WorldDataRuntimeLoader loader;
        [SerializeField] private Transform followTarget;
        [SerializeField] private float triggerRadius = 3f;
        [SerializeField] private bool useCameraAsFallback = true;
        [SerializeField] private bool drawGizmos = true;

        private readonly List<WorldDataRecord> records = new List<WorldDataRecord>();
        private readonly HashSet<string> inside = new HashSet<string>();

        public event Action<WorldDataRecord> Entered;
        public event Action<WorldDataRecord> Exited;

        public Transform FollowTarget
        {
            get => followTarget;
            set => followTarget = value;
        }

        public float TriggerRadius
        {
            get => triggerRadius;
            set => triggerRadius = Mathf.Max(0.01f, value);
        }

        public IReadOnlyList<WorldDataRecord> Records => records;

        private void OnEnable()
        {
            Collect();
            if (loader != null) loader.RecordLoaded += OnRecordLoaded;
        }

        private void OnDisable()
        {
            if (loader != null) loader.RecordLoaded -= OnRecordLoaded;
        }

        private void OnRecordLoaded(WorldDataRecord record)
        {
            if (record != null) records.Add(record);
        }

        /// <summary>Re-reads all records from the assigned loader/snapshot.</summary>
        public void Collect()
        {
            records.Clear();
            inside.Clear();
            if (loader == null || loader.Snapshot == null) return;
            foreach (WorldDataRecord record in loader.Snapshot.Records)
            {
                if (record != null) records.Add(record);
            }
        }

        private void Update()
        {
            Transform target = followTarget;
            if (target == null && useCameraAsFallback && Camera.main != null) target = Camera.main.transform;
            if (target == null) return;

            Vector3 position = target.position;
            for (int i = 0; i < records.Count; i++)
            {
                WorldDataRecord record = records[i];
                bool inRange = (record.position - position).sqrMagnitude <=
                    triggerRadius * triggerRadius;
                string key = record.id;

                if (inRange && inside.Add(key))
                {
                    Entered?.Invoke(record);
                    Debug.Log($"[WorldBuilder] Entered {record.kind} '{record.displayName}' ({key}).");
                }
                else if (!inRange && inside.Remove(key))
                {
                    Exited?.Invoke(record);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            for (int i = 0; i < records.Count; i++)
            {
                Gizmos.DrawWireSphere(records[i].position, triggerRadius);
            }
        }
    }
}
