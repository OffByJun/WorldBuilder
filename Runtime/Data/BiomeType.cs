namespace WorldBuilder.Runtime.Data
{
    public enum BiomeType
    {
        Ocean,
        Beach,
        Forest,
        Rocky,
        DeepSea,

        /// <summary>Enclosed underground space (caverns, tunnels, grottos).</summary>
        Cave,

        /// <summary>Sunlit shallow seafloor (roughly 8–20 m below sea level).</summary>
        CoralReef,

        /// <summary>Shallow vegetated seafloor just under the surface.</summary>
        KelpForest,

        /// <summary>Extreme-depth seafloor beyond the DeepSea band.</summary>
        AbyssalTrench
    }
}
