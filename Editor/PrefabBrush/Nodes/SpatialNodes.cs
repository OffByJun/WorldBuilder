using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public sealed class PositionToValueNode : ModifierNodeBase
    {
        public Axis axis = Axis.Y;

        public override string NodeName => "Position To Value";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Spatial;
        public override ModifierNodeBase CreateInstance() => new PositionToValueNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            switch (axis)
            {
                case Axis.X: return ctx.worldPosition.x;
                case Axis.Y: return ctx.worldPosition.y;
                default: return ctx.worldPosition.z;
            }
        }
    }

    [Serializable]
    public sealed class DistanceFromCenterNode : ModifierNodeBase
    {
        public bool normalize = true;

        public override string NodeName => "Distance From Center";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Spatial;
        public override ModifierNodeBase CreateInstance() => new DistanceFromCenterNode();

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float distance = Vector3.Distance(ctx.worldPosition, ctx.brushCenter);
            if (!normalize)
            {
                return distance;
            }

            float radius = Mathf.Max(0.0001f, ctx.brushRadius);
            return distance / radius;
        }
    }
}
