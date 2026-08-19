using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Entities.Authoring
{
    [AddComponentMenu("WorldBuilder/Entities/Region Focus Bridge")]
    public sealed class WorldEntityRegionFocus : MonoBehaviour
    {
        [SerializeField] private WorldGridSettings gridSettings;
        [SerializeField] private Transform focus;
        [Min(0), SerializeField] private int regionRadius = 1;

        private readonly List<RegionCoord> loadedRegions = new List<RegionCoord>(9);
        private RegionCoord previousRegion;
        private int previousRadius = -1;
        private bool initialized;

        private void Update()
        {
            if (gridSettings == null || !WorldEntityCommandQueue.IsReady) return;
            Transform target = focus != null ? focus : transform;
            WorldGrid grid = gridSettings.CreateGrid();
            RegionCoord center = grid.WorldToRegion(target.position);
            int radius = Mathf.Max(0, regionRadius);
            if (initialized && center == previousRegion && radius == previousRadius) return;

            initialized = true;
            previousRegion = center;
            previousRadius = radius;
            loadedRegions.Clear();
            for (int x = -radius; x <= radius; x++)
            for (int z = -radius; z <= radius; z++)
                loadedRegions.Add(new RegionCoord(center.X + x, center.Z + z));
            loadedRegions.Sort();
            WorldEntityCommandQueue.SetLoadedRegions(loadedRegions);
        }
    }
}
