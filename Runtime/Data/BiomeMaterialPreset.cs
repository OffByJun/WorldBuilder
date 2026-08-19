using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [CreateAssetMenu(fileName = "BiomeMaterialPreset", menuName = "WorldBuilder/BiomeMaterialPreset")]
    public sealed class BiomeMaterialPreset : ScriptableObject
    {
        [SerializeField] private BiomeType biome;
        [SerializeField] private List<Material> materials = new List<Material>();

        public BiomeType Biome
        {
            get => biome;
            set => biome = value;
        }

        public List<Material> Materials => materials;
    }
}
