using Unity.Mathematics;
using UnityEngine;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Deterministic seeded fBm built on Unity.Mathematics simplex noise.
    /// Same seed always yields identical values across sessions.
    /// </summary>
    public readonly struct FbmNoise
    {
        private readonly float2 seedOffset;
        private readonly float3 seedOffset3;

        public FbmNoise(int seed)
        {
            seedOffset = new float2(seed * 0.173f % 977f, -seed * 0.271f % 911f);
            seedOffset3 = new float3(seed * 0.191f % 887f, -seed * 0.317f % 853f, seed * 0.409f % 797f);
        }

        public float Value2D(float2 position, float frequency, int octaves, float persistence, float lacunarity)
        {
            float amplitude = 1f;
            float total = 0f;
            float normalization = 0f;
            float freq = frequency;

            for (int o = 0; o < octaves; o++)
            {
                total += noise.snoise(position * freq + seedOffset) * amplitude;
                normalization += amplitude;
                amplitude *= persistence;
                freq *= lacunarity;
            }

            return normalization > 0f ? total / normalization : 0f;
        }

        public float Ridged2D(float2 position, float frequency, int octaves, float persistence, float lacunarity)
        {
            return 1f - Mathf.Abs(Value2D(position, frequency, octaves, persistence, lacunarity)) * 2f;
        }

        /// <summary>Domain-warped fBm in [-1, 1]; the workhorse for coastlines and ridges.</summary>
        public float Warped2D(float2 position, float frequency, int octaves, float persistence,
            float lacunarity, float warpStrength, float warpFrequency)
        {
            float2 warp = new float2(
                Value2D(position + new float2(31.416f, 47.852f), warpFrequency, 2, 0.5f, 2f),
                Value2D(position + new float2(-58.229f, 12.986f), warpFrequency, 2, 0.5f, 2f));
            return Value2D(position + warp * warpStrength, frequency, octaves, persistence, lacunarity);
        }

        /// <summary>Seeded fBm over 3D space in [-1, 1]; drives cave carving fields.</summary>
        public float Value3D(float3 position, float frequency, int octaves, float persistence, float lacunarity)
        {
            float amplitude = 1f;
            float total = 0f;
            float normalization = 0f;
            float freq = frequency;

            for (int o = 0; o < octaves; o++)
            {
                total += noise.snoise(position * freq + seedOffset3) * amplitude;
                normalization += amplitude;
                amplitude *= persistence;
                freq *= lacunarity;
            }

            return normalization > 0f ? total / normalization : 0f;
        }
    }
}
