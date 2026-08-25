using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Ready-made scatter rule presets for underwater floors and cave interiors. Rules are
    /// created with correct gates but empty prefab lists — assign your prefabs and go.
    /// </summary>
    public static class ScatterRuleSetFactory
    {
        public enum EcologyKind
        {
            CoralReef,
            KelpForest,
            CaveInterior
        }

        public static ScatterRuleSet Create(EcologyKind kind, string assetName)
        {
            ScatterRuleSet set = ScriptableObject.CreateInstance<ScatterRuleSet>();
            set.name = assetName;
            switch (kind)
            {
                case EcologyKind.CoralReef:
                    set.rules.Add(Rule("Coral Cluster", biome: BiomeType.CoralReef,
                        minElevation: -20f, maxElevation: -8f,
                        density: 0.02f, useDepthGate: true, minDepth: 8f, maxDepth: 20f,
                        maxFlowSpeed: 2.5f));
                    set.rules.Add(Rule("Sponge Patch", biome: BiomeType.CoralReef,
                        minElevation: -18f, maxElevation: -10f,
                        density: 0.008f, useDepthGate: true, minDepth: 10f, maxDepth: 18f,
                        maxFlowSpeed: 1.5f));
                    break;

                case EcologyKind.KelpForest:
                    set.rules.Add(Rule("Kelp Stalk", biome: BiomeType.KelpForest,
                        minElevation: -8f, maxElevation: -2f,
                        density: 0.03f, useDepthGate: true, minDepth: 2f, maxDepth: 8f,
                        maxFlowSpeed: 3f));
                    set.rules.Add(Rule("Shell Bed", biome: BiomeType.KelpForest,
                        minElevation: -6f, maxElevation: -1f,
                        density: 0.01f, useDepthGate: true, minDepth: 1f, maxDepth: 6f,
                        maxFlowSpeed: 999f));
                    break;

                case EcologyKind.CaveInterior:
                    // Interior placement runs through VoxelVolumeScatter (floor-based).
                    set.rules.Add(Rule("Ore Node", biome: BiomeType.Cave,
                        density: 0.002f, maxSlope: 45f));
                    set.rules.Add(Rule("Glow Moss", biome: BiomeType.Cave,
                        density: 0.01f, maxSlope: 35f));
                    set.rules.Add(Rule("Stalagmite", biome: BiomeType.Cave,
                        density: 0.004f, maxSlope: 25f));
                    break;
            }
            return set;
        }

        private static ScatterRuleSet.Rule Rule(string name, BiomeType? biome = null,
            float minElevation = -999f, float maxElevation = 999f, float density = 0.01f,
            float maxSlope = 35f, bool useDepthGate = false, float minDepth = 0f,
            float maxDepth = 999f, float maxFlowSpeed = 999f)
        {
            return new ScatterRuleSet.Rule
            {
                name = name,
                anyBiome = biome == null,
                biome = biome ?? BiomeType.Forest,
                minElevation = minElevation,
                maxElevation = maxElevation,
                densityPerSquareMeter = density,
                maxSlopeDegrees = maxSlope,
                useDepthGate = useDepthGate,
                minDepth = minDepth,
                maxDepth = maxDepth,
                maxFlowSpeed = maxFlowSpeed
            };
        }
    }
}
