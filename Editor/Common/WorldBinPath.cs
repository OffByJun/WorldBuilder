using System.IO;
using UnityEngine;

namespace WorldBuilder.Editor
{
    public static class WorldBinPath
    {
        public const string RelativePath = "WorldBuilder/world.bin";

        public static string Full => Path.Combine(Application.streamingAssetsPath, RelativePath);
    }
}
