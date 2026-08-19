using System;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [Serializable]
    public abstract class ModifierNodeBase : IModifierNode
    {
        [HideInInspector] [SerializeField] private string guid = System.Guid.NewGuid().ToString();
        [HideInInspector] [SerializeField] private Vector2 graphPosition;

        [NonSerialized] private static int evaluationDepth;

        public string Guid => guid;

        public Vector2 GraphPosition
        {
            get => graphPosition;
            set => graphPosition = value;
        }

        public abstract string NodeName { get; }
        public abstract ModifierNodeCategory Category { get; }
        public abstract ModifierNodeBase CreateInstance();

        public virtual int InputPortCount => 0;
        public virtual string GetInputPortName(int index) => "In";
        public virtual IModifierNode GetInput(int index) => null;
        public virtual void SetInput(int index, IModifierNode node) { }

        public float Evaluate(ModifierContext ctx)
        {
            if (evaluationDepth >= 128)
            {
                return 0f;
            }

            evaluationDepth++;
            try
            {
                return EvaluateInternal(ctx);
            }
            finally
            {
                evaluationDepth--;
            }
        }

        protected abstract float EvaluateInternal(ModifierContext ctx);

        protected static float EvaluateInput(IModifierNode node, ModifierContext ctx, float fallback = 0f)
        {
            return node != null ? node.Evaluate(ctx) : fallback;
        }
    }
}
