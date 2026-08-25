using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.PrefabBrush
{
    public enum ModifierChannel
    {
        PositionOffset,
        Rotation,
        Scale
    }

    public enum Axis
    {
        X,
        Y,
        Z
    }

    public enum ModifierNodeCategory
    {
        Basic,
        Noise,
        Math,
        Spatial,
        Mask
    }

    [Serializable]
    public struct ModifierContext
    {
        public Vector3 worldPosition;
        public Vector3 brushCenter;
        public float brushRadius;
        public Vector3 surfaceNormal;
        public BiomeType biome;
        public int seed;

        /// <summary>True when the placement point is below a water surface (0 when dry).</summary>
        public bool inWater;

        /// <summary>Distance from the water surface down to the placement point, in meters.</summary>
        public float waterDepth;
    }

    public interface IModifierNode
    {
        string NodeName { get; }
        float Evaluate(ModifierContext ctx);
    }
}
