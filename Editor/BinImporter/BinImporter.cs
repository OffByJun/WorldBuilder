using System.IO;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.BinImporter
{
    public sealed class BinImporter
    {
        public ChunkData[] Parse(string path)
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new BinaryReader(stream);

            int chunkCount = reader.ReadInt32();
            ChunkData[] chunks = new ChunkData[chunkCount];

            for (int i = 0; i < chunkCount; i++)
            {
                Vector3 position = ReadVector3(reader);
                BiomeType biome = (BiomeType)reader.ReadInt32();

                int sizeX = reader.ReadInt32();
                int sizeY = reader.ReadInt32();
                int sizeZ = reader.ReadInt32();

                VoxelData voxels = new VoxelData(sizeX, sizeY, sizeZ);
                for (int x = 0; x < sizeX; x++)
                {
                    for (int y = 0; y < sizeY; y++)
                    {
                        for (int z = 0; z < sizeZ; z++)
                        {
                            voxels.SetDensity(x, y, z, reader.ReadSingle());
                        }
                    }
                }

                int spawnCount = reader.ReadInt32();
                SpawnData[] spawns = new SpawnData[spawnCount];
                for (int s = 0; s < spawnCount; s++)
                {
                    spawns[s] = new SpawnData
                    {
                        prefabId = reader.ReadInt32(),
                        position = ReadVector3(reader),
                        rotation = ReadQuaternion(reader),
                        scale = ReadVector3(reader)
                    };
                }

                chunks[i] = new ChunkData
                {
                    position = position,
                    biome = biome,
                    voxels = voxels,
                    spawns = spawns
                };
            }

            return chunks;
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
