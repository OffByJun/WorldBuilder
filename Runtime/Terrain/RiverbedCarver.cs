using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Carves a smooth channel into the voxel store along a polyline of riverbed centre
    /// points (already sunk below the local surface). Built on <see cref="TerrainDeformer"/>
    /// sphere sweeps so results compose with erosion output and runtime digging.
    /// </summary>
    public static class RiverbedCarver
    {
        /// <summary>
        /// Stamps overlapping cutters along the centreline. Points are expected in world
        /// space at roughly (water surface − depth·½). Returns changed voxel count.
        /// </summary>
        public static int Carve(VoxelStoreAsset store, float chunkSize,
            IReadOnlyList<Vector3> centerline, float width, float depth,
            float delta = -2.5f)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (centerline == null || centerline.Count < 2) return 0;

            float radius = Mathf.Max(width * 0.55f, depth * 0.7f);
            int changed = 0;
            for (int i = 1; i < centerline.Count; i++)
            {
                changed += TerrainDeformer.Drill(store, chunkSize,
                    centerline[i - 1], centerline[i], radius, delta);
            }
            return changed;
        }
    }
}
