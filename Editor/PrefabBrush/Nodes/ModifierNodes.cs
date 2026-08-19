using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public sealed class AddNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;
        public float value;

        public override string NodeName => "Add";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Basic;
        public override ModifierNodeBase CreateInstance() => new AddNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx) => EvaluateInput(input, ctx) + value;
    }

    [Serializable]
    public sealed class MultiplyNode : ModifierNodeBase
    {
        [HideInInspector] [SerializeReference] private IModifierNode input;
        public float value = 1f;

        public override string NodeName => "Multiply";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Basic;
        public override ModifierNodeBase CreateInstance() => new MultiplyNode();

        public override int InputPortCount => 1;
        public override IModifierNode GetInput(int index) => input;
        public override void SetInput(int index, IModifierNode node) => input = node;

        protected override float EvaluateInternal(ModifierContext ctx) => EvaluateInput(input, ctx, 1f) * value;
    }

    [Serializable]
    public sealed class OverrideNode : ModifierNodeBase
    {
        public float value;

        public override string NodeName => "Override";
        public override ModifierNodeCategory Category => ModifierNodeCategory.Basic;
        public override ModifierNodeBase CreateInstance() => new OverrideNode();

        protected override float EvaluateInternal(ModifierContext ctx) => value;
    }
}
