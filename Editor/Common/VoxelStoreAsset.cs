using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    [Serializable]
    public sealed class VoxelChunkEntry
    {
        public Vector3Int coord;
        public int sizeX;
        public int sizeY;
        public int sizeZ;
        public float[] density;
    }

    [CreateAssetMenu(fileName = "VoxelStore", menuName = "WorldBuilder/VoxelStore")]
    public sealed class VoxelStoreAsset : ScriptableObject, IVoxelStore
    {
        [SerializeField] private int resolution = 16;
        [SerializeField] private List<VoxelChunkEntry> entries = new List<VoxelChunkEntry>();

        private Dictionary<Vector3Int, VoxelChunkEntry> lookup;

        public int Resolution => resolution;

        public IEnumerable<Vector3Int> Coords => Lookup.Keys;

        private Dictionary<Vector3Int, VoxelChunkEntry> Lookup
        {
            get
            {
                if (lookup == null)
                {
                    RebuildLookup();
                }

                return lookup;
            }
        }

        public VoxelChunkEntry GetOrCreate(Vector3Int coord)
        {
            if (Lookup.TryGetValue(coord, out VoxelChunkEntry existing))
            {
                return existing;
            }

            VoxelChunkEntry entry = new VoxelChunkEntry
            {
                coord = coord,
                sizeX = resolution,
                sizeY = resolution,
                sizeZ = resolution,
                density = new float[resolution * resolution * resolution]
            };

            entries.Add(entry);
            Lookup[coord] = entry;
            return entry;
        }

        public float GetDensity(VoxelChunkEntry entry, int x, int y, int z)
        {
            return entry.density[Index(entry, x, y, z)];
        }

        public void SetDensity(VoxelChunkEntry entry, int x, int y, int z, float value)
        {
            entry.density[Index(entry, x, y, z)] = value;
        }

        public bool TryGetVoxelData(Vector3Int coord, out VoxelData voxels)
        {
            if (!Lookup.TryGetValue(coord, out VoxelChunkEntry entry))
            {
                voxels = default;
                return false;
            }

            voxels = new VoxelData(entry.sizeX, entry.sizeY, entry.sizeZ);
            for (int x = 0; x < entry.sizeX; x++)
            {
                for (int y = 0; y < entry.sizeY; y++)
                {
                    for (int z = 0; z < entry.sizeZ; z++)
                    {
                        voxels.SetDensity(x, y, z, entry.density[Index(entry, x, y, z)]);
                    }
                }
            }

            return true;
        }

        public void SetVoxelData(Vector3Int coord, VoxelData voxels)
        {
            VoxelChunkEntry entry;
            if (!Lookup.TryGetValue(coord, out entry))
            {
                entry = new VoxelChunkEntry { coord = coord };
                entries.Add(entry);
                Lookup[coord] = entry;
            }

            entry.sizeX = voxels.sizeX;
            entry.sizeY = voxels.sizeY;
            entry.sizeZ = voxels.sizeZ;
            entry.density = new float[voxels.sizeX * voxels.sizeY * voxels.sizeZ];

            for (int x = 0; x < entry.sizeX; x++)
            {
                for (int y = 0; y < entry.sizeY; y++)
                {
                    for (int z = 0; z < entry.sizeZ; z++)
                    {
                        entry.density[Index(entry, x, y, z)] = voxels.GetDensity(x, y, z);
                    }
                }
            }
        }

        private static int Index(VoxelChunkEntry entry, int x, int y, int z)
        {
            return x + entry.sizeX * (y + entry.sizeY * z);
        }

        private void RebuildLookup()
        {
            lookup = new Dictionary<Vector3Int, VoxelChunkEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                VoxelChunkEntry entry = entries[i];
                if (entry != null && !lookup.ContainsKey(entry.coord))
                {
                    lookup[entry.coord] = entry;
                }
            }
        }
    }
}
