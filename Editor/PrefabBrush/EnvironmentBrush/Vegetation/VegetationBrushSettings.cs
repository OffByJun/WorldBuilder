using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Vegetation
{
    public enum LeafShape
    {
        Wide,
        Narrow
    }

    [Serializable]
    public sealed class VegetationBrushSettings
    {
        public int seed = 1234;
        public int variantCount = 3;

        public float height = 2.5f;
        public int segmentCount = 10;
        public float curviness = 0.25f;
        public float stemWidth = 0.06f;
        public float taper = 0.35f;
        public float twist = 0.4f;
        public Vector2 leanDirection = new Vector2(1f, 0f);
        public float leanStrength = 0.3f;

        public int leafCount = 24;
        public float leafSize = 0.4f;
        public float leafSpacing = 0.15f;
        public Vector2 leafAngleRange = new Vector2(20f, 70f);
        public LeafShape leafShape = LeafShape.Narrow;
        public float ribStrength = 0.3f;
        public float alphaGradient = 0.6f;

        public Color colorGradientRoot = new Color(0.12f, 0.32f, 0.16f);
        public Color colorGradientTip = new Color(0.35f, 0.7f, 0.32f);

        public Vector2 currentDirection = new Vector2(1f, 0f);
        public float currentStrength = 0.4f;
    }
}
