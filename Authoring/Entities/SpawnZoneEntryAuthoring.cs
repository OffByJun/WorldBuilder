using System;
using UnityEngine;
using WorldBuilder.Entities.Resources.Authoring;

namespace WorldBuilder.Entities.Authoring
{
    [Serializable]
    public sealed class SpawnZoneEntryAuthoring
    {
        public GameObject Prefab;
        [ItemId] public int ItemId = -1;
        [Range(0f, 1f)] public float Weight = 1f;
        [Min(1)] public int MinimumCount = 1;
        [Min(1)] public int MaximumCount = 1;
    }
}
