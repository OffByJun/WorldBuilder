using System;
using System.Globalization;

namespace WorldBuilder.Runtime.Grid
{
    public static class WorldCoordNaming
    {
        public static string ChunkName(ChunkCoord coordinate) =>
            "CH_" + Signed(coordinate.X) + "_" + Signed(coordinate.Z);

        public static string RegionName(RegionCoord coordinate) =>
            "RG_" + Signed(coordinate.X) + "_" + Signed(coordinate.Z);

        public static bool TryParseChunkName(string value, out ChunkCoord coordinate)
        {
            coordinate = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split('_');
            if (parts.Length != 3 || !string.Equals(parts[0], "CH", StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int z)) return false;
            coordinate = new ChunkCoord(x, z);
            return true;
        }

        private static string Signed(int value) => value.ToString("+0000;-0000;+0000", CultureInfo.InvariantCulture);
    }
}
