using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.WorldSeed
{
    /// <summary>
    /// Shareable world identity: seed + shape + cave parameters as a compact JSON string.
    /// Hand the file to a friend and they regenerate the exact same terrain — meshes,
    /// caves, entrances, ecology seeds included.
    /// </summary>
    public static class WorldSeedCodec
    {
        private const int SchemaVersion = 1;

        [Serializable]
        private sealed class SeedDocument
        {
            public int schemaVersion;
            public string generator = "WorldBuilder";
            public string createdAtUtc;
            public TerrainShapeSnapshot terrain;
            public CaveShapeSnapshot caves;
        }

        [Serializable]
        public sealed class TerrainShapeSnapshot
        {
            public int seed;
            public float baseHeight;
            public float heightAmplitude;
            public float featureScale;
            public int octaves;
            public float persistence;
            public float lacunarity;
            public float ridgeWeight;
            public float warpStrength;
            public float warpFrequency;
            public float terraceBlend;
            public float islandRadius;
            public float surfaceSharpness;
            public float bottomClampY;

            public static TerrainShapeSnapshot From(TerrainShapeParams source) => new TerrainShapeSnapshot
            {
                seed = source.seed,
                baseHeight = source.baseHeight,
                heightAmplitude = source.heightAmplitude,
                featureScale = source.featureScale,
                octaves = source.octaves,
                persistence = source.persistence,
                lacunarity = source.lacunarity,
                ridgeWeight = source.ridgeWeight,
                warpStrength = source.warpStrength,
                warpFrequency = source.warpFrequency,
                terraceBlend = source.terraceBlend,
                islandRadius = source.islandRadius,
                surfaceSharpness = source.surfaceSharpness,
                bottomClampY = source.bottomClampY
            };

            public void ApplyTo(TerrainShapeParams target)
            {
                target.seed = seed;
                target.baseHeight = baseHeight;
                target.heightAmplitude = heightAmplitude;
                target.featureScale = featureScale;
                target.octaves = octaves;
                target.persistence = persistence;
                target.lacunarity = lacunarity;
                target.ridgeWeight = ridgeWeight;
                target.warpStrength = warpStrength;
                target.warpFrequency = warpFrequency;
                target.terraceBlend = terraceBlend;
                target.islandRadius = islandRadius;
                target.surfaceSharpness = surfaceSharpness;
                target.bottomClampY = bottomClampY;
            }
        }

        [Serializable]
        public sealed class CaveShapeSnapshot
        {
            public int seedOffset;
            public float minY;
            public float maxY;
            public float surfaceProtectDepth;
            public float tunnelScale;
            public float tunnelWidth;
            public float tunnelWinding;
            public float tunnelVerticalSquash;
            public float roomScale;
            public float roomThreshold;
            public float roomDepthBias;
            public float carveSharpness;
            public float waterTableY;

            public static CaveShapeSnapshot From(CaveShapeParams source) => new CaveShapeSnapshot
            {
                seedOffset = source.seedOffset,
                minY = source.minY,
                maxY = source.maxY,
                surfaceProtectDepth = source.surfaceProtectDepth,
                tunnelScale = source.tunnelScale,
                tunnelWidth = source.tunnelWidth,
                tunnelWinding = source.tunnelWinding,
                tunnelVerticalSquash = source.tunnelVerticalSquash,
                roomScale = source.roomScale,
                roomThreshold = source.roomThreshold,
                roomDepthBias = source.roomDepthBias,
                carveSharpness = source.carveSharpness,
                waterTableY = source.waterTableY
            };

            public void ApplyTo(CaveShapeParams target)
            {
                target.seedOffset = seedOffset;
                target.minY = minY;
                target.maxY = maxY;
                target.surfaceProtectDepth = surfaceProtectDepth;
                target.tunnelScale = tunnelScale;
                target.tunnelWidth = tunnelWidth;
                target.tunnelWinding = tunnelWinding;
                target.tunnelVerticalSquash = tunnelVerticalSquash;
                target.roomScale = roomScale;
                target.roomThreshold = roomThreshold;
                target.roomDepthBias = roomDepthBias;
                target.carveSharpness = carveSharpness;
                target.waterTableY = waterTableY;
            }
        }

        public static string Export(TerrainShapeParams shape, CaveShapeParams caves)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            var document = new SeedDocument
            {
                schemaVersion = SchemaVersion,
                createdAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                terrain = TerrainShapeSnapshot.From(shape),
                caves = caves != null ? CaveShapeSnapshot.From(caves) : null
            };
            return JsonUtility.ToJson(document, prettyPrint: true);
        }

        public static bool TryImport(string json, TerrainShapeParams shapeTarget,
            CaveShapeParams caveTarget, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "Empty seed document."; return false; }

            SeedDocument document;
            try
            {
                document = JsonUtility.FromJson<SeedDocument>(json);
            }
            catch (Exception exception)
            {
                error = "Malformed JSON: " + exception.Message;
                return false;
            }

            if (document == null || document.terrain == null) { error = "Missing terrain section."; return false; }
            if (document.schemaVersion != SchemaVersion)
            {
                error = $"Unsupported schema version {document.schemaVersion} (expected {SchemaVersion}).";
                return false;
            }

            document.terrain.ApplyTo(shapeTarget);
            if (caveTarget != null && document.caves != null) document.caves.ApplyTo(caveTarget);
            return true;
        }

        /// <summary>Short deterministic fingerprint for humans to compare seeds quickly.</summary>
        public static string Fingerprint(TerrainShapeParams shape)
        {
            if (shape == null) return "null";
            var builder = new StringBuilder();
            builder.Append(shape.seed).Append('|')
                .Append(F(shape.baseHeight)).Append(F(shape.heightAmplitude))
                .Append(F(shape.featureScale)).Append(shape.octaves)
                .Append(F(shape.persistence)).Append(F(shape.lacunarity))
                .Append(F(shape.ridgeWeight)).Append(F(shape.warpStrength))
                .Append(F(shape.warpFrequency)).Append(F(shape.terraceBlend))
                .Append(F(shape.islandRadius));
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            var text = new StringBuilder(8);
            for (int i = 0; i < 4; i++) text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static string F(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture) + ";";
    }
}
