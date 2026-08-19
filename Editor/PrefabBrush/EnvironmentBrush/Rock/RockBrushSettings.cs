using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Rock
{
    public enum RockBaseShape
    {
        Sphere,
        Box,
        Capsule
    }

    [Serializable]
    public sealed class RockBrushSettings
    {
        public int seed = 2024;
        public int variantCount = 3;

        public RockBaseShape baseShape = RockBaseShape.Sphere;

        public float noiseFrequency = 1.8f;
        public float noiseStrength = 0.35f;
        public int noiseOctaves = 4;
        public bool layeredNoise = true;

        public float squashX = 1f;
        public float squashY = 0.8f;
        public float squashZ = 1f;
        public float sharpness = 0.4f;
        public Vector3 erosionDirection = new Vector3(0f, -1f, 0f);
        public float erosionStrength = 0.2f;

        public float surfaceRoughness = 0.15f;
        public float crackDepth = 0.15f;
        public float sharpEdgeRatio = 0.3f;

        public float mossHeightThreshold = 0.6f;
        public float sedimentHeightThreshold = 0.25f;

        public float minScale = 0.8f;
        public float maxScale = 1.4f;
        public float clusterVariance = 0.15f;
    }
}
