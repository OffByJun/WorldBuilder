using System;
using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    public abstract class WaterBodyAuthoring : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private int priority = 10;

        public string StableId => stableId;
        public int Priority { get => priority; set => priority = value; }

        public void SetStableId(string value) => stableId = value ?? string.Empty;

        protected virtual void Reset()
        {
            EnsureStableId();
        }

        protected virtual void OnValidate()
        {
            EnsureStableId();
        }

        private void EnsureStableId()
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                stableId = Guid.NewGuid().ToString("N");
            }
        }
    }
}
