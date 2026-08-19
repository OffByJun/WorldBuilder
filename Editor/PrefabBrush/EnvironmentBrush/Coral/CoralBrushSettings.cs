using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Coral
{
    public enum CoralType
    {
        Branching,
        Fan,
        Pillar
    }

    [Serializable]
    public sealed class CoralBrushSettings
    {
        public int seed = 7777;
        public int variantCount = 3;

        public CoralType coralType = CoralType.Branching;

        public int branchCount = 3;
        public Vector2 branchAngleRange = new Vector2(25f, 55f);
        public float branchLengthDecay = 0.72f;
        public int maxBranchDepth = 4;
        public float minBranchDistance = 0.15f;

        public float rootThickness = 0.18f;
        public float tipThickness = 0.04f;

        public float knobDensity = 0.5f;
        public float knobSize = 0.05f;
        public float polypDensity = 0.4f;

        public Color baseColor = new Color(0.7f, 0.25f, 0.35f);
        public Color tipColor = new Color(0.95f, 0.6f, 0.5f);

        public float asymmetryStrength = 0.3f;
        public int asymmetrySeed = 11;
        public float globalTilt = 0.1f;
    }
}
