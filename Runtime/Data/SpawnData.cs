using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [Serializable]
    public struct SpawnData
    {
        public int prefabId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }
}
