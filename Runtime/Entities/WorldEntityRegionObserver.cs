using System.Collections.Generic;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Entities
{
    public sealed class WorldEntityRegionObserver : IRegionSetObserver
    {
        public void SetLoadedRegions(IReadOnlyList<RegionCoord> coordinates)
        {
            WorldEntityCommandQueue.SetLoadedRegions(coordinates);
        }
    }
}
