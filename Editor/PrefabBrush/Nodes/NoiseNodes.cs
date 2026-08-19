using System;
using Unity.Mathematics;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public sealed class PerlinNoiseNode : ModifierNodeBase
    {
        public NoiseSettings settings = new NoiseSettings { scale = 10f, amplitude = 1f };

        public override string NodeName => "Perlin Noise";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Noise;
        public override ModifierNodeBase CreateInstance() => new PerlinNoiseNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float scale = settings.SafeScale;
            float x = (ctx.worldPosition.x + settings.offset.x) / scale;
            float z = (ctx.worldPosition.z + settings.offset.y) / scale;
            return Mathf.PerlinNoise(x, z) * settings.amplitude;
        }
    }

    [Serializable]
    public sealed class SimplexNoiseNode : ModifierNodeBase
    {
        public NoiseSettings settings = new NoiseSettings { scale = 10f, amplitude = 1f };

        public override string NodeName => "Simplex Noise";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Noise;
        public override ModifierNodeBase CreateInstance() => new SimplexNoiseNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float scale = settings.SafeScale;
            float x = (ctx.worldPosition.x + settings.offset.x) / scale;
            float z = (ctx.worldPosition.z + settings.offset.y) / scale;
            float n = noise.snoise(new float2(x, z));
            return (n * 0.5f + 0.5f) * settings.amplitude;
        }
    }

    [Serializable]
    public sealed class VoronoiNoiseNode : ModifierNodeBase
    {
        public NoiseSettings settings = new NoiseSettings { scale = 10f, amplitude = 1f };
        public int cellCount = 4;

        public override string NodeName => "Voronoi Noise";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Noise;
        public override ModifierNodeBase CreateInstance() => new VoronoiNoiseNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float scale = settings.SafeScale;
            float cells = Mathf.Max(1, cellCount);
            Vector2 point = new Vector2(
                (ctx.worldPosition.x + settings.offset.x) / scale * cells,
                (ctx.worldPosition.z + settings.offset.y) / scale * cells);

            Vector2 baseCell = new Vector2(Mathf.Floor(point.x), Mathf.Floor(point.y));
            float minDistance = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2 cell = baseCell + new Vector2(dx, dy);
                    Vector2 feature = cell + Hash(cell);
                    minDistance = Mathf.Min(minDistance, Vector2.Distance(point, feature));
                }
            }

            return Mathf.Clamp01(minDistance) * settings.amplitude;
        }

        private static Vector2 Hash(Vector2 cell)
        {
            float a = Mathf.Sin(Vector2.Dot(cell, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            float b = Mathf.Sin(Vector2.Dot(cell, new Vector2(269.5f, 183.3f))) * 43758.5453f;
            return new Vector2(a - Mathf.Floor(a), b - Mathf.Floor(b));
        }
    }

    [Serializable]
    public sealed class FractalNoiseNode : ModifierNodeBase
    {
        public NoiseSettings settings = new NoiseSettings { scale = 10f, amplitude = 1f };
        [Range(1, 8)] public int octaves = 4;
        public float lacunarity = 2f;
        public float persistence = 0.5f;

        public override string NodeName => "Fractal Noise";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Noise;
        public override ModifierNodeBase CreateInstance() => new FractalNoiseNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float scale = settings.SafeScale;
            float frequency = 1f / scale;
            float amplitude = 1f;
            float total = 0f;
            float totalAmplitude = 0f;
            int count = Mathf.Clamp(octaves, 1, 8);

            for (int i = 0; i < count; i++)
            {
                float x = (ctx.worldPosition.x + settings.offset.x) * frequency;
                float z = (ctx.worldPosition.z + settings.offset.y) * frequency;
                total += Mathf.PerlinNoise(x, z) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            float normalized = totalAmplitude > 0f ? total / totalAmplitude : 0f;
            return normalized * settings.amplitude;
        }
    }
}
