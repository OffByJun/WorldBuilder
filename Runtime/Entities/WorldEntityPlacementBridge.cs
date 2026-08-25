using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Editing;

namespace WorldBuilder.Entities
{
    /// <summary>
    /// Mirrors GameObject placements made through <see cref="RuntimePlacementService"/>
    /// into the DOTS entity world: every placed instance whose prefab id is bound here
    /// also spawns an entity via <see cref="WorldEntityCommandQueue"/>.
    /// Attach to any scene object and bind instance names to entity prefab ids.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldEntityPlacementBridge : MonoBehaviour
    {
        [Serializable]
        public sealed class NameBinding
        {
            public string objectName = string.Empty;
            public int prefabId;
        }

        [SerializeField] private List<NameBinding> bindings = new List<NameBinding>();
        [SerializeField] private bool mirrorOnEnable = true;

        private void OnEnable()
        {
            if (mirrorOnEnable) RuntimePlacementService.Placed += OnPlaced;
            RuntimePlacementService.Removed += OnRemoved;
        }

        private void OnDisable()
        {
            RuntimePlacementService.Placed -= OnPlaced;
            RuntimePlacementService.Removed -= OnRemoved;
        }

        public bool TryResolvePrefabId(string objectName, out int prefabId)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                NameBinding binding = bindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.objectName)) continue;
                if (string.Equals(binding.objectName, objectName, StringComparison.Ordinal))
                {
                    prefabId = binding.prefabId;
                    return true;
                }
            }

            prefabId = 0;
            return false;
        }

        private void OnPlaced(RuntimePlacementService.PlacementRecord record)
        {
            if (!TryResolvePrefabId(record.PrefabId, out int prefabId)) return;
            if (record.Instance == null) return;

            Transform t = record.Instance.transform;
            WorldEntityCommandQueue.TrySpawn(
                prefabId,
                t.position,
                t.rotation,
                Mathf.Max(0.0001f, t.localScale.x));
        }

        private void OnRemoved(RuntimePlacementService.PlacementRecord record)
        {
            // Entity despawn currently flows through region streaming / lifetime systems.
            // Surface the event so gameplay code can react if needed.
        }
    }
}
