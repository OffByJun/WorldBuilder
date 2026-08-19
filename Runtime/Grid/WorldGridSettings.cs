using UnityEngine;

namespace WorldBuilder.Runtime.Grid
{
    [CreateAssetMenu(menuName = "WorldBuilder/World Grid Settings", fileName = "WorldGridSettings")]
    public sealed class WorldGridSettings : ScriptableObject
    {
        [SerializeField] private string worldId = "World_01";
        [SerializeField, Min(1f)] private float authoringChunkSize = 128f;
        [SerializeField, Min(1)] private int chunksPerRegion = 4;
        [SerializeField, Min(0.25f)] private float queryCellSize = 32f;
        [SerializeField] private Vector3 worldOrigin;

        public string WorldId => worldId;
        public float AuthoringChunkSize => authoringChunkSize;
        public int ChunksPerRegion => chunksPerRegion;
        public float RegionSize => authoringChunkSize * chunksPerRegion;
        public float QueryCellSize => queryCellSize;
        public Vector3 WorldOrigin => worldOrigin;

        public WorldGrid CreateGrid() => new WorldGrid(authoringChunkSize, chunksPerRegion, queryCellSize, worldOrigin);

        public void Configure(float chunkSize, int regionChunkCount, float cellSize, Vector3 origin)
        {
            authoringChunkSize = Mathf.Max(1f, chunkSize);
            chunksPerRegion = Mathf.Max(1, regionChunkCount);
            queryCellSize = Mathf.Max(0.25f, cellSize);
            worldOrigin = origin;
        }

        public void SetWorldId(string value)
        {
            worldId = SanitizeWorldId(value);
        }

        private void OnValidate()
        {
            worldId = SanitizeWorldId(worldId);
            authoringChunkSize = Mathf.Max(1f, authoringChunkSize);
            chunksPerRegion = Mathf.Max(1, chunksPerRegion);
            queryCellSize = Mathf.Max(0.25f, queryCellSize);
        }

        private static string SanitizeWorldId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "World_01";
            char[] characters = value.Trim().ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                char character = characters[i];
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-') characters[i] = '_';
            }
            return new string(characters);
        }
    }
}
