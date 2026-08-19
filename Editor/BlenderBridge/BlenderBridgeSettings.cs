using UnityEngine;
using WorldBuilder.Authoring.Chunks;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Editor.BlenderBridge
{
    [CreateAssetMenu(menuName = "WorldBuilder/Blender/Bridge Settings", fileName = "BlenderBridgeSettings")]
    public sealed class BlenderBridgeSettings : ScriptableObject
    {
        [SerializeField] private WorldGridSettings worldGrid;
        [SerializeField] private BlenderAssetRegistry assetRegistry;
        [SerializeField] private string sourceRoot = "Assets/WorldSource";
        [SerializeField] private string generatedRoot = "Assets/WorldBuilderGenerated";
        [SerializeField] private bool autoImport = true;
        [SerializeField] private float[] lodTransitionHeights = { 0.6f, 0.3f, 0.1f, 0.03f };

        public WorldGridSettings WorldGrid => worldGrid;
        public BlenderAssetRegistry AssetRegistry => assetRegistry;
        public string SourceRoot => NormalizeAssetPath(sourceRoot);
        public string GeneratedRoot => NormalizeAssetPath(generatedRoot);
        public bool AutoImport => autoImport;
        public float[] LodTransitionHeights => SanitizeLodTransitions(lodTransitionHeights);

        public void Configure(WorldGridSettings grid, BlenderAssetRegistry registry, string source, string generated)
        {
            worldGrid = grid;
            assetRegistry = registry;
            sourceRoot = NormalizeAssetPath(source);
            generatedRoot = NormalizeAssetPath(generated);
        }

        private void OnValidate()
        {
            sourceRoot = NormalizeAssetPath(sourceRoot);
            generatedRoot = NormalizeAssetPath(generatedRoot);
            lodTransitionHeights = SanitizeLodTransitions(lodTransitionHeights);
        }

        internal static float[] SanitizeLodTransitions(float[] values)
        {
            if (values == null || values.Length == 0) return new[] { 0.6f, 0.3f, 0.1f, 0.03f };
            float[] result = new float[values.Length];
            float previous = 1f;
            for (int i = 0; i < result.Length; i++)
            {
                float value = Mathf.Clamp(values[i], 0.001f, 0.999f);
                result[i] = Mathf.Min(value, previous - 0.001f);
                result[i] = Mathf.Max(result[i], 0.001f);
                previous = result[i];
            }
            return result;
        }

        private static string NormalizeAssetPath(string value)
        {
            string path = string.IsNullOrWhiteSpace(value) ? "Assets" : value.Replace('\\', '/').TrimEnd('/');
            return path.StartsWith("Assets", System.StringComparison.Ordinal) ? path : "Assets/" + path.TrimStart('/');
        }
    }
}
