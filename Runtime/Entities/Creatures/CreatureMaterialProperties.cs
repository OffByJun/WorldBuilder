using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace WorldBuilder.Entities.Creatures
{
    [MaterialProperty("_PrimaryColor")]
    public struct CreaturePrimaryColor : IComponentData
    {
        public float4 Value;
    }

    [MaterialProperty("_SecondaryColor")]
    public struct CreatureSecondaryColor : IComponentData
    {
        public float4 Value;
    }

    [MaterialProperty("_AccentColor")]
    public struct CreatureAccentColor : IComponentData
    {
        public float4 Value;
    }

    [MaterialProperty("_PatternColor")]
    public struct CreaturePatternColor : IComponentData
    {
        public float4 Value;
    }

    [MaterialProperty("_PatternParams")]
    public struct CreaturePatternParams : IComponentData
    {
        public float4 Value;
    }
}
