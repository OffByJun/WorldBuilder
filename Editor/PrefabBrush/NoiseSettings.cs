using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public struct NoiseSettings
    {
        public float scale;
        public float amplitude;
        public Vector2 offset;

        public float SafeScale => Mathf.Max(0.0001f, scale);
    }
}
