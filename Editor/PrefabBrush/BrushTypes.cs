using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public struct PrefabEntry
    {
        public GameObject prefab;
        public float weight;
        public EnvironmentType envType;
    }

    [Serializable]
    public struct BrushStroke
    {
        public int seed;
        public Vector3 center;
        public float radius;
        public int density;
    }

    [Serializable]
    public class BrushMask
    {
        public bool useHeightMask;
        public float minHeight;
        public float maxHeight = 100f;
        public bool useSlopeMask;
        public float maxSlopeAngle = 45f;
        public bool useBiomeMask;
        public BiomeType allowedBiome = BiomeType.Forest;
    }

    public struct BrushContext
    {
        public Vector3 position;
        public Vector3 normal;
        public Quaternion rotation;
        public Vector3 scale;
    }

    public struct BrushPlacement
    {
        public GameObject prefab;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }
}
