using UnityEditor;

namespace WorldBuilder.Editor.PrefabBrush
{
    [InitializeOnLoad]
    public static class ModifierGraphBootstrap
    {
        static ModifierGraphBootstrap()
        {
            ModifierNodeRegistry.Clear();

            ModifierNodeRegistry.Register(new AddNode());
            ModifierNodeRegistry.Register(new MultiplyNode());
            ModifierNodeRegistry.Register(new OverrideNode());

            ModifierNodeRegistry.Register(new PerlinNoiseNode());
            ModifierNodeRegistry.Register(new SimplexNoiseNode());
            ModifierNodeRegistry.Register(new VoronoiNoiseNode());
            ModifierNodeRegistry.Register(new FractalNoiseNode());

            ModifierNodeRegistry.Register(new ClampNode());
            ModifierNodeRegistry.Register(new RemapNode());
            ModifierNodeRegistry.Register(new AbsNode());
            ModifierNodeRegistry.Register(new PowerNode());
            ModifierNodeRegistry.Register(new LerpNode());

            ModifierNodeRegistry.Register(new PositionToValueNode());
            ModifierNodeRegistry.Register(new DistanceFromCenterNode());

            ModifierNodeRegistry.Register(new HeightMaskNode());
            ModifierNodeRegistry.Register(new SlopeMaskNode());
            ModifierNodeRegistry.Register(new BiomeMaskNode());
        }
    }
}
