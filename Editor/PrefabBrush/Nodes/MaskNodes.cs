using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public sealed class HeightMaskNode : ModifierNodeBase
    {
        public float minHeight;
        public float maxHeight = 10f;
        public float falloff = 1f;

        public override string NodeName => "Height Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new HeightMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float f = Mathf.Max(0.0001f, falloff);
            float y = ctx.worldPosition.y;
            float lower = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minHeight - f, minHeight + f, y));
            float upper = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(maxHeight + f, maxHeight - f, y));
            return Mathf.Clamp01(Mathf.Min(lower, upper));
        }
    }

    [Serializable]
    public sealed class SlopeMaskNode : ModifierNodeBase
    {
        public float maxAngle = 45f;
        public float falloff = 5f;

        public override string NodeName => "Slope Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new SlopeMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float f = Mathf.Max(0.0001f, falloff);
            float angle = Vector3.Angle(ctx.surfaceNormal, Vector3.up);
            float t = Mathf.InverseLerp(maxAngle - f, maxAngle + f, angle);
            return 1f - Mathf.SmoothStep(0f, 1f, t);
        }
    }

    [Serializable]
    public sealed class BiomeMaskNode : ModifierNodeBase
    {
        public BiomeType targetBiome = BiomeType.Forest;

        public override string NodeName => "Biome Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new BiomeMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx) => ctx.biome == targetBiome ? 1f : 0f;
    }

    [Serializable]
    public sealed class RandomMaskNode : ModifierNodeBase
    {
        [Range(0f, 1f)] public float threshold = 0.5f;
        public bool invert;

        public override string NodeName => "Random Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new RandomMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float value = HashToFloat(ctx.seed, ctx.worldPosition);
            bool pass = value >= threshold;
            return pass != invert ? 1f : 0f;
        }

        internal static float HashToFloat(int seed, Vector3 position)
        {
            unchecked
            {
                int hash = seed == 0 ? 0x6d2b79f5 : seed;
                hash = (hash * 397) ^ Mathf.RoundToInt(position.x * 100f);
                hash = (hash * 397) ^ Mathf.RoundToInt(position.y * 100f);
                hash = (hash * 397) ^ Mathf.RoundToInt(position.z * 100f);
                hash ^= hash >> 16;
                hash *= 1274126177;
                hash ^= hash >> 15;
                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }

    [Serializable]
    public sealed class CellMaskNode : ModifierNodeBase
    {
        public float cellSize = 8f;
        [Range(0f, 1f)] public float threshold = 0.5f;
        public bool invert;

        public override string NodeName => "Cell Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new CellMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            Vector3 cell = new Vector3(
                Mathf.Floor(ctx.worldPosition.x / Mathf.Max(0.01f, cellSize)),
                Mathf.Floor(ctx.worldPosition.y / Mathf.Max(0.01f, cellSize)),
                Mathf.Floor(ctx.worldPosition.z / Mathf.Max(0.01f, cellSize)));
            float value = RandomMaskNode.HashToFloat(ctx.seed + 7919, cell);
            bool pass = value >= threshold;
            return pass != invert ? 1f : 0f;
        }
    }

    [Serializable]
    public sealed class BrushEdgeMaskNode : ModifierNodeBase
    {
        [Range(0.01f, 1f)] public float edgeFalloff = 0.35f;

        public override string NodeName => "Brush Edge Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new BrushEdgeMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float radius = Mathf.Max(0.0001f, ctx.brushRadius);
            float distance = Vector3.Distance(ctx.worldPosition, ctx.brushCenter) / radius;
            return 1f - Mathf.SmoothStep(1f - edgeFalloff, 1f, distance);
        }
    }

    [Serializable]
    public sealed class WaterDepthMaskNode : ModifierNodeBase
    {
        public bool requireWater = true;
        public float minDepth;
        public float maxDepth = 10f;
        public bool invert;

        public override string NodeName => "Water Depth Mask";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Mask;
        public override ModifierNodeBase CreateInstance() => new WaterDepthMaskNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            bool pass = requireWater
                ? ctx.inWater && ctx.waterDepth >= minDepth && ctx.waterDepth <= maxDepth
                : !ctx.inWater || ctx.waterDepth < minDepth || ctx.waterDepth > maxDepth;
            return pass != invert ? 1f : 0f;
        }
    }
}
