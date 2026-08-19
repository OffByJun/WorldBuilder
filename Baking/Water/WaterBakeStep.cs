using System.Collections.Generic;
using WorldBuilder.Authoring.Water;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Baking.Water
{
    public sealed class WaterBakeStep : IWorldBakeStep
    {
        private readonly IEnumerable<WaterBodyAuthoring> bodies;
        public string StableId => "worldbuilder.water.query";
        public int Order => 500;
        public WaterWorldRuntimeData Result { get; private set; }

        public WaterBakeStep(IEnumerable<WaterBodyAuthoring> bodies)
        {
            this.bodies = bodies;
        }

        public void Execute(WorldBakeContext context, WorldBakeReport report)
        {
            WaterBakeResult result = WaterBaker.Bake(bodies, context.GridSettings);
            Result = result.Data;
            report.Merge(result.Report);
            context.SetDeterministicOutput(StableId, Result.DeterministicHash);
        }
    }
}
