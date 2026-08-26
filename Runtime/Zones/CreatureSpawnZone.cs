using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Zones
{
    /// <summary>Canonical season indices used by migration/spawn gating.</summary>
    public static class SeasonState
    {
        public const int Spring = 0, Summer = 1, Autumn = 2, Winter = 3;
        /// <summary>Game code sets this (e.g. from DayNightAtmosphere.SetSeason / % 4).</summary>
        public static int CurrentSeason { get; set; } = Summer;
    }

    public sealed class CreatureSpawnZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private BiomeType biome = BiomeType.Ocean;
        [SerializeField] private int prefabId;
        [SerializeField] private float density = 1f;
        [Tooltip("Seasons in which this zone spawns; empty means every season.")]
        [SerializeField] private List<int> activeSeasons = new List<int>();

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public BiomeType Biome
        {
            get => biome;
            set => biome = value;
        }

        public int PrefabId
        {
            get => prefabId;
            set => prefabId = value;
        }

        public float Density
        {
            get => density;
            set => density = value;
        }

        public List<int> ActiveSeasons => activeSeasons;

        public bool IsSeasonActive(int season) =>
            activeSeasons == null || activeSeasons.Count == 0 || activeSeasons.Contains(season);

        public bool IsActiveNow => IsSeasonActive(SeasonState.CurrentSeason);
    }
}
