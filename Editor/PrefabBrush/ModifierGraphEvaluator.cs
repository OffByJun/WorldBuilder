using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    public static class ModifierGraphEvaluator
    {
        public static Vector3 EvaluatePositionOffset(ModifierGraph graph, ModifierContext ctx)
        {
            return EvaluateChannel(graph, ModifierChannel.PositionOffset, 0f, ctx);
        }

        public static Vector3 EvaluateRotation(ModifierGraph graph, ModifierContext ctx)
        {
            return EvaluateChannel(graph, ModifierChannel.Rotation, 0f, ctx);
        }

        public static Vector3 EvaluateScale(ModifierGraph graph, ModifierContext ctx)
        {
            return EvaluateChannel(graph, ModifierChannel.Scale, 1f, ctx);
        }

        private static Vector3 EvaluateChannel(ModifierGraph graph, ModifierChannel channel, float fallback, ModifierContext ctx)
        {
            if (graph == null)
            {
                return new Vector3(fallback, fallback, fallback);
            }

            return new Vector3(
                Component(graph.GetChannelInput(channel, 0), fallback, ctx),
                Component(graph.GetChannelInput(channel, 1), fallback, ctx),
                Component(graph.GetChannelInput(channel, 2), fallback, ctx));
        }

        private static float Component(IModifierNode node, float fallback, ModifierContext ctx)
        {
            return node != null ? node.Evaluate(ctx) : fallback;
        }
    }
}
