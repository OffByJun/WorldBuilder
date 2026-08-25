using System;
using Unity.Mathematics;
using UnityEngine;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Deterministic erosion on TerrainField.HeightMap: hydraulic droplet erosion plus
    /// thermal talus slippage. Operates in-place; same seed always produces the same map.
    /// </summary>
    public static class ErosionSimulator
    {
        public sealed class Params
        {
            public int DropletCount = 8000;
            public int MaxDropletLifetime = 24;
            public float Inertia = 0.06f;
            public float SedimentCapacity = 3.2f;
            public float ErodeSpeed = 0.32f;
            public float DepositSpeed = 0.32f;
            public float EvaporateSpeed = 0.02f;
            public float Gravity = 4f;
            public float MinSlope = 0.01f;
            [Range(0f, 1f)] public float ThermalWeight = 0.35f;
            [Tooltip("Max slope (height per cell) before material slips downhill.")]
            public float TalusAngle = 0.9f;
        }

        public static void Apply(TerrainField.HeightMap map, Params parameters, int seed)
        {
            Apply(map, parameters, seed, out _);
        }

        /// <summary>
        /// Applies erosion and reports the per-cell net change (negative = eroded,
        /// positive = deposited) so callers can bake an erosion intensity texture.
        /// </summary>
        public static void Apply(TerrainField.HeightMap map, Params parameters, int seed,
            out float[] erosionMap)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            parameters ??= new Params();
            var random = new Unity.Mathematics.Random((uint)Mathf.Max(1, seed));

            float[] before = new float[map.Heights.Length];
            Array.Copy(map.Heights, before, before.Length);

            for (int droplet = 0; droplet < parameters.DropletCount; droplet++)
            {
                float x = random.NextFloat(map.Size - 2f) + 1f;
                float z = random.NextFloat(map.Size - 2f) + 1f;
                float dx = 0f, dz = 0f;
                float speed = 1f;
                float water = 1f;
                float sediment = 0f;

                for (int lifetime = 0; lifetime < parameters.MaxDropletLifetime; lifetime++)
                {
                    int cellX = (int)x;
                    int cellZ = (int)z;
                    float tx = x - cellX;
                    float tz = z - cellZ;

                    Gradient(map, cellX, cellZ, out float gx, out float gz);
                    dx = dx * parameters.Inertia - gx * (1f - parameters.Inertia);
                    dz = dz * parameters.Inertia - gz * (1f - parameters.Inertia);
                    float length = math.length(new float2(dx, dz));
                    if (length < Mathf.Epsilon) break;
                    dx /= length;
                    dz /= length;

                    float newX = x + dx;
                    float newZ = z + dz;
                    if (newX < 1f || newX >= map.Size - 2f || newZ < 1f || newZ >= map.Size - 2f) break;

                    float heightOld = HeightBilinear(map, x, z);
                    float heightNew = HeightBilinear(map, newX, newZ);
                    float deltaHeight = heightNew - heightOld;

                    float capacity = Mathf.Max(-deltaHeight, parameters.MinSlope) *
                                     speed * water * parameters.SedimentCapacity;

                    if (sediment > capacity || deltaHeight > 0f)
                    {
                        float deposit = deltaHeight > 0f
                            ? Mathf.Min(deltaHeight, sediment)
                            : (sediment - capacity) * parameters.DepositSpeed;
                        sediment -= deposit;
                        Deposit(map, x, z, deposit);
                    }
                    else
                    {
                        float eroded = Mathf.Min((capacity - sediment) * parameters.ErodeSpeed, -deltaHeight);
                        Erode(map, cellX, cellZ, tx, tz, eroded);
                        sediment += eroded;
                    }

                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + deltaHeight * parameters.Gravity));
                    water *= 1f - parameters.EvaporateSpeed;
                    x = newX;
                    z = newZ;
                }
            }

            if (parameters.ThermalWeight > 0f) ThermalPass(map, parameters, seed);

            erosionMap = new float[map.Heights.Length];
            for (int i = 0; i < erosionMap.Length; i++)
                erosionMap[i] = map.Heights[i] - before[i];
        }

        private static void ThermalPass(TerrainField.HeightMap map, Params p, int seed)
        {
            var random = new Unity.Mathematics.Random((uint)(Mathf.Max(1, seed) ^ 0x5bf03635));
            int iterations = 4;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // Randomized sweep order keeps the pass deterministic but avoids directional bias.
                for (int index = 0; index < map.Size * map.Size; index++)
                {
                    int x = random.NextInt(1, map.Size - 1);
                    int z = random.NextInt(1, map.Size - 1);

                    float center = map.At(x, z);
                    float left = map.At(x - 1, z);
                    float right = map.At(x + 1, z);
                    float down = map.At(x, z - 1);
                    float up = map.At(x, z + 1);

                    Slide(map, x, z, x - 1, z, center, left, p);
                    Slide(map, x, z, x + 1, z, center, right, p);
                    Slide(map, x, z, x, z - 1, center, down, p);
                    Slide(map, x, z, x, z + 1, center, up, p);
                    center = map.At(x, z); // refresh after possible moves
                }
            }
        }

        private static void Slide(TerrainField.HeightMap map, int fromX, int fromZ, int toX, int toZ,
            float center, float neighbor, Params p)
        {
            float difference = center - neighbor;
            if (difference <= p.TalusAngle) return;
            float move = (difference - p.TalusAngle) * 0.25f * p.ThermalWeight;
            map.Set(fromX, fromZ, map.At(fromX, fromZ) - move);
            map.Set(toX, toZ, map.At(toX, toZ) + move);
        }

        private static float HeightBilinear(TerrainField.HeightMap map, float x, float z)
        {
            int cellX = (int)x;
            int cellZ = (int)z;
            float tx = x - cellX;
            float tz = z - cellZ;
            float h00 = map.At(cellX, cellZ);
            float h10 = map.At(cellX + 1, cellZ);
            float h01 = map.At(cellX, cellZ + 1);
            float h11 = map.At(cellX + 1, cellZ + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        private static void Gradient(TerrainField.HeightMap map, int x, int z, out float gx, out float gz)
        {
            gx = map.At(Mathf.Min(x + 1, map.Size - 1), z) - map.At(Mathf.Max(x - 1, 0), z);
            gz = map.At(x, Mathf.Min(z + 1, map.Size - 1)) - map.At(x, Mathf.Max(z - 1, 0));
        }

        private static void Deposit(TerrainField.HeightMap map, float x, float z, float amount)
        {
            int cellX = (int)x;
            int cellZ = (int)z;
            float tx = x - cellX;
            float tz = z - cellZ;
            Add(map, cellX, cellZ, amount * (1 - tx) * (1 - tz));
            Add(map, cellX + 1, cellZ, amount * tx * (1 - tz));
            Add(map, cellX, cellZ + 1, amount * (1 - tx) * tz);
            Add(map, cellX + 1, cellZ + 1, amount * tx * tz);
        }

        private static void Erode(TerrainField.HeightMap map, int cellX, int cellZ,
            float tx, float tz, float amount)
        {
            SubtractClamped(map, cellX, cellZ, amount * (1 - tx) * (1 - tz));
            SubtractClamped(map, cellX + 1, cellZ, amount * tx * (1 - tz));
            SubtractClamped(map, cellX, cellZ + 1, amount * (1 - tx) * tz);
            SubtractClamped(map, cellX + 1, cellZ + 1, amount * tx * tz);
        }

        private static void Add(TerrainField.HeightMap map, int x, int z, float amount)
        {
            if ((uint)x >= (uint)map.Size || (uint)z >= (uint)map.Size) return;
            map.Set(x, z, map.At(x, z) + amount);
        }

        private static void SubtractClamped(TerrainField.HeightMap map, int x, int z, float amount)
        {
            if ((uint)x >= (uint)map.Size || (uint)z >= (uint)map.Size) return;
            map.Set(x, z, Mathf.Max(BottomFloor, map.At(x, z) - amount));
        }

        // Erosion must not dig below the world floor.
        private const float BottomFloor = -500f;
    }
}
