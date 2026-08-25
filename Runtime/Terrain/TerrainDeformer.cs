using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Editing;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Registers a MeshFilter as the renderer of one terrain chunk so runtime edits can
    /// find and update it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainChunkRenderer : MonoBehaviour
    {
        public static readonly Dictionary<Vector3Int, TerrainChunkRenderer> Registry =
            new Dictionary<Vector3Int, TerrainChunkRenderer>();

        [SerializeField] private int chunkX;
        [SerializeField] private int chunkY;
        [SerializeField] private int chunkZ;

        public Vector3Int ChunkCoord => new Vector3Int(chunkX, chunkY, chunkZ);

        public void Configure(Vector3Int coord)
        {
            chunkX = coord.x;
            chunkY = coord.y;
            chunkZ = coord.z;
        }

        private void OnEnable() => Registry[ChunkCoord] = this;
        private void OnDisable()
        {
            if (Registry.TryGetValue(ChunkCoord, out TerrainChunkRenderer current) && current == this)
                Registry.Remove(ChunkCoord);
        }
    }

    /// <summary>
    /// Runtime terrain deformation (digging/destruction): edits voxel density in a sphere
    /// and re-meshes affected chunks through <see cref="SurfaceNetsMesher"/>.
    /// Tracks an edit journal for save integration.
    /// </summary>
    public static class TerrainDeformer
    {
        private static readonly HashSet<Vector3Int> editedChunks = new HashSet<Vector3Int>();

        public static event Action<Vector3Int> ChunkDeformed;

        /// <summary>Chunks modified since the last journal reset — feed these to WorldSaveService.SaveTerrain.</summary>
        public static IReadOnlyCollection<Vector3Int> EditedChunks => editedChunks;

        public static void ResetJournal() => editedChunks.Clear();

        /// <summary>
        /// Applies a spherical density delta (negative digs). Returns the number of voxels changed.
        /// Call <see cref="Remesh"/> afterwards for each reported chunk.
        /// </summary>
        public static int Modify(VoxelStoreAsset store, float chunkSize, Vector3 center,
            float radius, float delta)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (radius <= 0f) return 0;

            int resolution = store.Resolution;
            float spacing = chunkSize / resolution;
            var touched = new HashSet<Vector3Int>();
            int changed = 0;

            int minX = Mathf.FloorToInt((center.x - radius) / chunkSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / chunkSize);
            int minY = Mathf.FloorToInt((center.y - radius) / chunkSize);
            int maxY = Mathf.FloorToInt((center.y + radius) / chunkSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / chunkSize);
            int maxZ = Mathf.FloorToInt((center.z + radius) / chunkSize);

            float sqrRadius = radius * radius;

            for (int cx = minX; cx <= maxX; cx++)
            for (int cy = minY; cy <= maxY; cy++)
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                Vector3 origin = new Vector3(cx * chunkSize, cy * chunkSize, cz * chunkSize);
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, cy, cz));

                for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                for (int z = 0; z < resolution; z++)
                {
                    Vector3 world = origin + new Vector3(x * spacing, y * spacing, z * spacing);
                    float distanceSquared = (world - center).sqrMagnitude;
                    if (distanceSquared > sqrRadius) continue;

                    float falloff = 1f - Mathf.Sqrt(distanceSquared) / radius;
                    float previous = store.GetDensity(entry, x, y, z);
                    float updated = Mathf.Clamp01(previous + delta * falloff);
                    if (Mathf.Approximately(previous, updated)) continue;

                    store.SetDensity(entry, x, y, z, updated);
                    touched.Add(entry.coord);
                    changed++;
                }
            }

            foreach (Vector3Int coord in touched)
            {
                editedChunks.Add(coord);
                ChunkDeformed?.Invoke(coord);
            }
            return changed;
        }

        /// <summary>Rebuilds the mesh for one chunk onto its registered renderer.</summary>
        public static bool Remesh(VoxelStoreAsset store, float chunkSize, int resolution, Vector3Int coord)
        {
            if (!TerrainChunkRenderer.Registry.TryGetValue(coord, out TerrainChunkRenderer renderer))
                return false;

            var sampler = new VoxelWorldSampler(store, chunkSize);
            SurfaceNetsMesher.Result result =
                SurfaceNetsMesher.Mesh(sampler, coord, resolution, chunkSize);

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null) return false;

            if (result.Mesh == null)
            {
                filter.sharedMesh = null; // fully dug away
                return true;
            }

            result.Mesh.name = "Terrain_" + coord;
            filter.sharedMesh = result.Mesh;
            return true;
        }
    }
}
