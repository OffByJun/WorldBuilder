using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// Natural regrowth: dug-up chunks slowly relax back toward the procedural heightfield
    /// (the same formula WriteDensity used), so player mining heals over time while caves
    /// carved BEFORE binding are part of the target field and stay untouched.
    /// Budgeted ticks keep the cost flat regardless of world size; fully healed chunks are
    /// remembered and skipped until deformed again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainRegrowth : MonoBehaviour
    {
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private TerrainShapeParams shapeParams;
        [SerializeField] private float chunkSize = 128f;
        [Min(1f)] [SerializeField] private float intervalSeconds = 30f;
        [Range(1, 64)] [SerializeField] private int chunksPerTick = 4;
        [Range(0.01f, 1f)] [SerializeField] private float healRate = 0.25f;

        private TerrainField.HeightMap baseline;
        private float timer;
        private readonly HashSet<Vector3Int> converged = new HashSet<Vector3Int>();

        public event Action<Vector3Int> ChunkProgressed;
        public event Action<Vector3Int> ChunkFullyHealed;

        public void Bind(VoxelStoreAsset target, TerrainShapeParams shape, float size)
        {
            store = target;
            shapeParams = shape;
            chunkSize = size;
            baseline = null;
            converged.Clear();
            TerrainDeformer.ChunkDeformed += OnChunkDeformed;
        }

        private void OnDestroy()
        {
            TerrainDeformer.ChunkDeformed -= OnChunkDeformed;
        }

        private void OnChunkDeformed(Vector3Int coord) => converged.Remove(coord);

        private void Update()
        {
            if (!Ready) return;
            timer += Time.deltaTime;
            if (timer < intervalSeconds) return;
            timer = 0f;
            TickNow(chunksPerTick);
        }

        private bool Ready => store != null && shapeParams != null;

        /// <summary>Heals up to <paramref name="maxChunks"/> edited chunks one step. Returns progressed chunk count.</summary>
        public int TickNow(int maxChunks)
        {
            if (!Ready || maxChunks <= 0) return 0;
            EnsureBaseline();

            var snapshot = new List<Vector3Int>(TerrainDeformer.EditedChunks);
            int progressed = 0;

            foreach (Vector3Int coord in snapshot)
            {
                if (progressed >= maxChunks) break;
                if (converged.Contains(coord)) continue;
                if (!store.TryGetEntry(coord, out VoxelChunkEntry entry)) continue;

                bool fullyHealed = HealChunk(entry, coord);
                progressed++;
                ChunkProgressed?.Invoke(coord);
                if (fullyHealed)
                {
                    converged.Add(coord);
                    ChunkFullyHealed?.Invoke(coord);
                }
            }
            return progressed;
        }

        /// <summary>One relaxation step over the whole chunk; true when it now matches the baseline.</summary>
        private bool HealChunk(VoxelChunkEntry entry, Vector3Int coord)
        {
            int resolution = store.Resolution;
            var sampler = new VoxelWorldSampler(store, chunkSize);
            Vector3 origin = new Vector3(coord.x * chunkSize, coord.y * chunkSize,
                coord.z * chunkSize);
            float spacing = chunkSize / resolution;
            float sharpness = shapeParams.surfaceSharpness / Mathf.Max(0.001f, spacing);

            bool stillOff = false;
            for (int x = 0; x < resolution; x++)
            for (int z = 0; z < resolution; z++)
            {
                float wx = origin.x + x * spacing;
                float wz = origin.z + z * spacing;
                float surface = baseline.SampleWorld(new Vector2(wx, wz));

                for (int y = 0; y < resolution; y++)
                {
                    float wy = origin.y + y * spacing;
                    float target = Mathf.Clamp01(
                        (surface - wy) * sharpness + 0.5f);
                    if (wy < shapeParams.bottomClampY) target = 1f;

                    float current = sampler.SamplePoint(
                        coord.x * resolution + x,
                        coord.y * resolution + y,
                        coord.z * resolution + z);
                    float updated = Mathf.Clamp01(Mathf.MoveTowards(current, target, healRate));

                    if (!Mathf.Approximately(current, updated))
                        store.SetDensity(entry, x, y, z, updated);
                    if (Mathf.Abs(updated - target) > 0.02f) stillOff = true;
                }
            }
            return !stillOff;
        }

        private void EnsureBaseline()
        {
            if (baseline != null) return;

            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;
            foreach (Vector3Int coord in TerrainDeformer.EditedChunks)
            {
                minX = Mathf.Min(minX, coord.x * chunkSize);
                maxX = Mathf.Max(maxX, (coord.x + 1) * chunkSize);
                minZ = Mathf.Min(minZ, coord.z * chunkSize);
                maxZ = Mathf.Max(maxZ, (coord.z + 1) * chunkSize);
            }
            if (minX > maxX) { minX = maxX = minZ = maxZ = 0f; }

            const float cellSize = 2f;
            int extent = Mathf.Max(
                Mathf.CeilToInt((maxX - minX) / cellSize),
                Mathf.CeilToInt((maxZ - minZ) / cellSize)) + 1;
            baseline = TerrainField.BuildHeightMap(shapeParams,
                new Vector2(minX, minZ), Mathf.Max(2, extent), cellSize);
        }
    }
}
