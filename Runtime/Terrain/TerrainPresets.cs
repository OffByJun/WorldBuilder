using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>One-click terrain archetypes for TerrainShapeParams.</summary>
    public enum TerrainPreset
    {
        Islands,
        Highlands,
        Dunes,
        Archipelago,
        Canyons
    }

    public static class TerrainPresets
    {
        public static void Apply(TerrainShapeParams p, TerrainPreset preset)
        {
            switch (preset)
            {
                case TerrainPreset.Islands:
                    p.seed = UnityEngine.Random.Range(1, 999999);
                    p.baseHeight = 18f;
                    p.heightAmplitude = 46f;
                    p.featureScale = 260f;
                    p.octaves = 6;
                    p.persistence = 0.48f;
                    p.lacunarity = 2.15f;
                    p.ridgeWeight = 0.35f;
                    p.warpStrength = 55f;
                    p.warpFrequency = 0.0035f;
                    p.terraceBlend = 0f;
                    p.islandRadius = 900f;
                    break;

                case TerrainPreset.Highlands:
                    p.seed = UnityEngine.Random.Range(1, 999999);
                    p.baseHeight = 42f;
                    p.heightAmplitude = 65f;
                    p.featureScale = 320f;
                    p.octaves = 7;
                    p.persistence = 0.52f;
                    p.lacunarity = 2.3f;
                    p.ridgeWeight = 0.55f;
                    p.warpStrength = 40f;
                    p.warpFrequency = 0.0028f;
                    p.terraceBlend = 0.15f;
                    p.islandRadius = 0f;
                    break;

                case TerrainPreset.Dunes:
                    p.seed = UnityEngine.Random.Range(1, 999999);
                    p.baseHeight = 12f;
                    p.heightAmplitude = 14f;
                    p.featureScale = 90f;
                    p.octaves = 3;
                    p.persistence = 0.4f;
                    p.lacunarity = 1.9f;
                    p.ridgeWeight = 0.6f;
                    p.warpStrength = 10f;
                    p.warpFrequency = 0.01f;
                    p.terraceBlend = 0f;
                    p.islandRadius = 0f;
                    break;

                case TerrainPreset.Archipelago:
                    p.seed = UnityEngine.Random.Range(1, 999999);
                    p.baseHeight = 8f;
                    p.heightAmplitude = 38f;
                    p.featureScale = 150f;
                    p.octaves = 5;
                    p.persistence = 0.5f;
                    p.lacunarity = 2.4f;
                    p.ridgeWeight = 0.2f;
                    p.warpStrength = 90f;
                    p.warpFrequency = 0.005f;
                    p.terraceBlend = 0f;
                    p.islandRadius = 520f;
                    break;

                case TerrainPreset.Canyons:
                    p.seed = UnityEngine.Random.Range(1, 999999);
                    p.baseHeight = 30f;
                    p.heightAmplitude = 70f;
                    p.featureScale = 240f;
                    p.octaves = 6;
                    p.persistence = 0.45f;
                    p.lacunarity = 2.05f;
                    p.ridgeWeight = 0.75f;
                    p.warpStrength = 30f;
                    p.warpFrequency = 0.0022f;
                    p.terraceBlend = 0.55f;
                    p.islandRadius = 0f;
                    break;
            }
        }
    }
}
