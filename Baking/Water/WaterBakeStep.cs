using System.Collections.Generic;
using WorldBuilder.Authoring.Water;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Water;
using WorldBuilder.Runtime.Zones;

namespace WorldBuilder.Baking.Water
{
    public sealed class WaterBakeStep : IWorldBakeStep
    {
        private readonly IEnumerable<WaterBodyAuthoring> bodies;
        private readonly IEnumerable<WaterCurrentZone> currentZones;
        public string StableId => "worldbuilder.water.query";
        public int Order => 500;
        public WaterWorldRuntimeData Result { get; private set; }

        public WaterBakeStep(IEnumerable<WaterBodyAuthoring> bodies,
            IEnumerable<WaterCurrentZone> currentZones = null)
        {
            this.bodies = bodies;
            this.currentZones = currentZones;
        }

        public void Execute(WorldBakeContext context, WorldBakeReport report)
        {
            WaterBakeResult result = WaterBaker.Bake(bodies, context.GridSettings, currentZones);
            Result = result.Data;
            report.Merge(result.Report);
            context.SetDeterministicOutput(StableId, Result.DeterministicHash);
        }
    }
}
