using System;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Seasonal recoloring for baked biome maps: per-biome colors for spring/summer/
    /// autumn/winter, sampled as a smooth blend between adjacent seasons. Feed the result
    /// into vertex colors at bake time (Terrain Forge season selector) or runtime material
    /// tints in game code.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Terrain/Season Palette", fileName = "SeasonPalette")]
    public sealed class SeasonPalette : ScriptableObject
    {
        [Serializable]
        public sealed class BiomeSeasons
        {
            public BiomeType biome = BiomeType.Forest;
            public Color spring = new Color(0.35f, 0.65f, 0.30f);
            public Color summer = new Color(0.20f, 0.55f, 0.22f);
            public Color autumn = new Color(0.75f, 0.48f, 0.18f);
            public Color winter = new Color(0.82f, 0.85f, 0.88f);
        }

        [SerializeField] private ListWrapper seasons = new ListWrapper();

        [Serializable]
        private sealed class ListWrapper
        {
            public BiomeSeasons[] entries = Array.Empty<BiomeSeasons>();
        }

        public int Count => seasons.entries.Length;
        public BiomeSeasons GetEntry(int index) => seasons.entries[index];

        /// <summary>Ensures an entry exists for every biome; returns this for chaining.</summary>
        public SeasonPalette EnsureDefaults()
        {
            var list = new System.Collections.Generic.List<BiomeSeasons>(seasons.entries);
            foreach (BiomeType biome in Enum.GetValues(typeof(BiomeType)))
            {
                if (list.Exists(entry => entry.biome == biome)) continue;
                Color baseColor = BiomeClassifier.DebugColor(biome);
                list.Add(new BiomeSeasons
                {
                    biome = biome,
                    spring = baseColor * 1.05f,
                    summer = baseColor,
                    autumn = Color.Lerp(baseColor, new Color(0.8f, 0.5f, 0.2f), 0.45f),
                    winter = Color.Lerp(baseColor, new Color(0.85f, 0.88f, 0.92f), 0.55f)
                });
            }
            seasons.entries = list.ToArray();
            return this;
        }

        /// <summary>
        /// Blended color for one biome at a continuous season value
        /// (0=spring, 1=summer, 2=autumn, 3=winter; wraps).
        /// </summary>
        public Color Sample(BiomeType biome, float season)
        {
            BiomeSeasons entry = Find(biome);
            if (entry == null) return BiomeClassifier.DebugColor(biome);
            return Blend(entry, season);
        }

        private BiomeSeasons Find(BiomeType biome)
        {
            foreach (BiomeSeasons entry in seasons.entries)
                if (entry != null && entry.biome == biome) return entry;
            return null;
        }

        private static Color Blend(BiomeSeasons entry, float season)
        {
            season = Mathf.Repeat(season, 4f);
            int fromIndex = Mathf.FloorToInt(season) % 4;
            int toIndex = (fromIndex + 1) % 4;
            Color from = FromIndex(entry, fromIndex);
            Color to = FromIndex(entry, toIndex);
            return Color.Lerp(from, to, season - Mathf.Floor(season));
        }

        private static Color FromIndex(BiomeSeasons entry, int index)
        {
            switch (index)
            {
                case 0: return entry.spring;
                case 1: return entry.summer;
                case 2: return entry.autumn;
                default: return entry.winter;
            }
        }
    }
}
