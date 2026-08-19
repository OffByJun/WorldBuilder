using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.Export
{
    public sealed class SceneDataCollector
    {
        private readonly IChunkBiomeMap biomeMap;
        private readonly ISpawnerSceneQuery spawnerQuery;
        private readonly IVoxelStore voxelStore;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();

        public SceneDataCollector(IChunkBiomeMap biomeMap, ISpawnerSceneQuery spawnerQuery, IVoxelStore voxelStore)
        {
            this.biomeMap = biomeMap;
            this.spawnerQuery = spawnerQuery;
            this.voxelStore = voxelStore;
        }

        public ChunkData[] Collect(float chunkSize)
        {
            IReadOnlyDictionary<Vector3Int, BiomeType> entries = biomeMap.Entries;
            IReadOnlyList<ISpawner> spawners = spawnerQuery.GetAll();

            List<ChunkData> chunks = new List<ChunkData>(entries.Count);

            foreach (KeyValuePair<Vector3Int, BiomeType> entry in entries)
            {
                Vector3Int coord = entry.Key;

                List<SpawnData> spawns = new List<SpawnData>();
                for (int i = 0; i < spawners.Count; i++)
                {
                    ISpawner spawner = spawners[i];
                    if (calculator.ToChunkCoord(spawner.SpawnPosition, chunkSize) != coord)
                    {
                        continue;
                    }

                    Transform transform = (spawner as MonoBehaviour) != null ? ((MonoBehaviour)spawner).transform : null;
                    spawns.Add(new SpawnData
                    {
                        prefabId = spawner.PrefabId,
                        position = spawner.SpawnPosition,
                        rotation = transform != null ? transform.rotation : Quaternion.identity,
                        scale = transform != null ? transform.localScale : Vector3.one
                    });
                }

                VoxelData voxels = default;
                if (voxelStore != null)
                {
                    voxelStore.TryGetVoxelData(coord, out voxels);
                }

                chunks.Add(new ChunkData
                {
                    position = calculator.ToWorldOrigin(coord, chunkSize),
                    biome = entry.Value,
                    voxels = voxels,
                    spawns = spawns.ToArray()
                });
            }

            return chunks.ToArray();
        }
    }
}
