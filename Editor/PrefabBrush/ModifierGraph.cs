using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    [CreateAssetMenu(fileName = "ModifierGraph", menuName = "WorldBuilder/Modifier Graph")]
    public sealed class ModifierGraph : ScriptableObject
    {
        [SerializeReference] public List<IModifierNode> nodes = new List<IModifierNode>();
        [SerializeReference] private IModifierNode[] channelInputs = new IModifierNode[9];

        public IModifierNode GetChannelInput(ModifierChannel channel, int axis)
        {
            int index = ((int)channel * 3) + axis;
            return index >= 0 && index < channelInputs.Length ? channelInputs[index] : null;
        }

        public void SetChannelInput(ModifierChannel channel, int axis, IModifierNode node)
        {
            EnsureChannelArray();
            channelInputs[((int)channel * 3) + axis] = node;
        }

        public void RemoveNode(IModifierNode node)
        {
            if (node == null)
            {
                return;
            }

            nodes.Remove(node);
            for (int i = 0; i < channelInputs.Length; i++)
            {
                if (ReferenceEquals(channelInputs[i], node))
                {
                    channelInputs[i] = null;
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is ModifierNodeBase op)
                {
                    for (int p = 0; p < op.InputPortCount; p++)
                    {
                        if (ReferenceEquals(op.GetInput(p), node))
                        {
                            op.SetInput(p, null);
                        }
                    }
                }
            }
        }

        private void EnsureChannelArray()
        {
            if (channelInputs == null || channelInputs.Length != 9)
            {
                channelInputs = new IModifierNode[9];
            }
        }

        public static string ChannelGuid(ModifierChannel channel, int axis)
        {
            return "__channel_" + channel + "_" + axis;
        }

        public static bool IsChannelGuid(string guid)
        {
            return !string.IsNullOrEmpty(guid) && guid.StartsWith("__channel_");
        }
    }
}
