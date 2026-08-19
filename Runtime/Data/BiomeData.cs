using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    [CreateAssetMenu(fileName = "BiomeData", menuName = "WorldBuilder/BiomeData")]
    public sealed class BiomeData : ScriptableObject
    {
        [SerializeField] private BiomeType biomeType;
        [SerializeField] private Color debugColor = Color.white;

        public BiomeType BiomeType => biomeType;
        public Color DebugColor => debugColor;
    }
}
