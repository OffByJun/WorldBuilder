using UnityEngine;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Authoring parameters for procedural terrain. All fields are plain serializable
    /// floats/ints so the whole shape can be versioned, diffed and hashed.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Terrain/Terrain Shape Params", fileName = "TerrainShapeParams")]
    public sealed class TerrainShapeParams : ScriptableObject
    {
        [Header("Identity")]
        public int seed = 1337;

        [Header("Base Landmass")]
        [Tooltip("Average terrain height in meters.")]
        public float baseHeight = 24f;
        [Tooltip("Height variation amplitude in meters.")]
        public float heightAmplitude = 40f;
        [Tooltip("Horizontal feature size in meters (larger = broader landmasses).")]
        public float featureScale = 220f;

        [Header("Detail")]
        [Range(1, 10)] public int octaves = 5;
        [Range(0.1f, 0.9f)] public float persistence = 0.45f;
        public float lacunarity = 2.1f;
        [Tooltip("Secondary high-frequency ridged layer weight (0 disables).")]
        [Range(0f, 1f)] public float ridgeWeight = 0.25f;

        [Header("Warping & Shaping")]
        [Tooltip("Domain warp strength in meters of horizontal displacement.")]
        public float warpStrength = 35f;
        public float warpFrequency = 0.004f;
        [Tooltip("Flattens low areas toward plateaus (0 = raw noise).")]
        [Range(0f, 1f)] public float terraceBlend;
        [Tooltip("Radial island falloff radius in meters; 0 disables falloff.")]
        public float islandRadius;

        [Header("Voxel Mapping")]
        [Tooltip("Surface transition width in voxels for density falloff.")]
        [Min(0.25f)] public float surfaceSharpness = 2.5f;
        [Tooltip("Solid fill below this world Y regardless of the field (caves can still cut it later).")]
        public float bottomClampY = -48f;

        private void Reset() => seed = UnityEngine.Random.Range(1, 999999);
    }
}
