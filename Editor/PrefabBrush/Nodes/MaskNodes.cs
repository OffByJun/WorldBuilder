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
}
