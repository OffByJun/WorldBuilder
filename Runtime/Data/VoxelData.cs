using System;

namespace WorldBuilder.Runtime.Data
{
    [Serializable]
    public struct VoxelData
    {
        public int sizeX;
        public int sizeY;
        public int sizeZ;
        public float[,,] density;

        public VoxelData(int sizeX, int sizeY, int sizeZ)
        {
            this.sizeX = sizeX;
            this.sizeY = sizeY;
            this.sizeZ = sizeZ;
            density = new float[sizeX, sizeY, sizeZ];
        }

        public float GetDensity(int x, int y, int z)
        {
            if (!InBounds(x, y, z))
            {
                return 0f;
            }

            return density[x, y, z];
        }

        public void SetDensity(int x, int y, int z, float value)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }

            density[x, y, z] = value;
        }

        private bool InBounds(int x, int y, int z)
        {
            if (density == null)
            {
                return false;
            }

            return x >= 0 && x < sizeX
                && y >= 0 && y < sizeY
                && z >= 0 && z < sizeZ;
        }
    }
}
