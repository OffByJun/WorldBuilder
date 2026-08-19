using System.IO;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.Export
{
    public sealed class BinaryChunkSerializeStrategy : IChunkSerializeStrategy
    {
        public void Serialize(BinaryWriter writer, ChunkData[] chunks)
        {
            int chunkCount = chunks?.Length ?? 0;
            writer.Write(chunkCount);

            for (int i = 0; i < chunkCount; i++)
            {
                ChunkData chunk = chunks[i];
                WriteVector3(writer, chunk.position);
                writer.Write((int)chunk.biome);

                VoxelData voxels = chunk.voxels;
                writer.Write(voxels.sizeX);
                writer.Write(voxels.sizeY);
                writer.Write(voxels.sizeZ);

                for (int x = 0; x < voxels.sizeX; x++)
                {
                    for (int y = 0; y < voxels.sizeY; y++)
                    {
                        for (int z = 0; z < voxels.sizeZ; z++)
                        {
                            writer.Write(voxels.GetDensity(x, y, z));
                        }
                    }
                }

                int spawnCount = chunk.spawns?.Length ?? 0;
                writer.Write(spawnCount);

                for (int s = 0; s < spawnCount; s++)
                {
                    SpawnData spawn = chunk.spawns[s];
                    writer.Write(spawn.prefabId);
                    WriteVector3(writer, spawn.position);
                    WriteQuaternion(writer, spawn.rotation);
                    WriteVector3(writer, spawn.scale);
                }
            }
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }
    }
}
