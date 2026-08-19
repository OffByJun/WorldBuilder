using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class CreatureSpawnZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private BiomeType biome = BiomeType.Ocean;
        [SerializeField] private int prefabId;
        [SerializeField] private float density = 1f;

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
    }
}
