using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public interface IVoxelStore
    {
        int Resolution { get; }
        IEnumerable<Vector3Int> Coords { get; }
        bool TryGetVoxelData(Vector3Int coord, out VoxelData voxels);
        void SetVoxelData(Vector3Int coord, VoxelData voxels);
    }
}
