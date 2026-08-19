using System;
using System.Collections.Generic;

namespace WorldBuilder.Editor.WorldStatisticsTool
{
    public struct StatisticsSnapshot
    {
        public int totalObjects;
        public int totalTriangles;
        public int totalVertices;
        public int meshCount;
        public int materialCount;
        public int textureCount;
        public int rigidbodyCount;
        public int colliderCount;
        public int lightCount;
        public int reflectionProbeCount;
        public long textureMemoryBytes;
        public long meshMemoryBytes;
        public Dictionary<string, int> worldDataCounts;
    }
}
