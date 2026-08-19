using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public sealed class ClampNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;
        public float min;
        public float max = 1f;

        public override string NodeName => "Clamp";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
        public override ModifierNodeBase CreateInstance() => new ClampNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx) => Mathf.Clamp(EvaluateInput(input, ctx), min, max);
    }

    [Serializable]
    public sealed class RemapNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;
        public float inMin;
        public float inMax = 1f;
        public float outMin;
        public float outMax = 1f;

        public override string NodeName => "Remap";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
        public override ModifierNodeBase CreateInstance() => new RemapNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float t = Mathf.InverseLerp(inMin, inMax, EvaluateInput(input, ctx));
            return Mathf.Lerp(outMin, outMax, t);
        }
    }

    [Serializable]
    public sealed class AbsNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;

        public override string NodeName => "Abs";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
        public override ModifierNodeBase CreateInstance() => new AbsNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx) => Mathf.Abs(EvaluateInput(input, ctx));
    }

    [Serializable]
    public sealed class PowerNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;
        public float exponent = 2f;

        public override string NodeName => "Power";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
        public override ModifierNodeBase CreateInstance() => new PowerNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx) => Mathf.Pow(EvaluateInput(input, ctx), exponent);
    }

    [Serializable]
    public sealed class LerpNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode nodeA;
        [HideInInspector] [SerializeReference] private IModifierNode nodeB;
        [HideInInspector] [SerializeReference] private IModifierNode nodeT;
        [Range(0f, 1f)] public float t = 0.5f;

        public override string NodeName => "Lerp";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
        public override ModifierNodeBase CreateInstance() => new LerpNode();

        public override int InputPortCount => 3;

        public override string GetInputPortName(int index)
        {
            switch (index)
            {
                case 0: return "A";
                case 1: return "B";
                default: return "T";
            }
        }

        public override IModifierNode GetInput(int index)
        {
            switch (index)
            {
                case 0: return nodeA;
                case 1: return nodeB;
                default: return nodeT;
            }
        }

        public override void SetInput(int index, IModifierNode node)
        {
            switch (index)
            {
                case 0: nodeA = node; break;
                case 1: nodeB = node; break;
                default: nodeT = node; break;
            }
        }

        protected override float EvaluateInternal(ModifierContext ctx)
        {
            float blend = nodeT != null ? Mathf.Clamp01(EvaluateInput(nodeT, ctx)) : t;
            return Mathf.Lerp(EvaluateInput(nodeA, ctx), EvaluateInput(nodeB, ctx), blend);
        }
    }
}
