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

        [Tooltip("Use this entry's own scale and rotation instead of the brush-wide values.")]
        public bool customTransform;
        public Vector2 scaleRange;
        public bool randomYaw;
        public bool alignToNormal;
        [Tooltip("Metres each instance is lifted (negative sinks it) along its own up axis.")]
        public float heightOffset;

        public Vector2 ScaleRange(PrefabBrushSettings settings) => customTransform ? scaleRange : settings.scaleRange;
        public bool RandomYaw(PrefabBrushSettings settings) => customTransform ? randomYaw : settings.randomYaw;
        public bool AlignToNormal(PrefabBrushSettings settings) => customTransform ? alignToNormal : settings.alignToNormal;
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
