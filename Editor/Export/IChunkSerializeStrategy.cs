using System.IO;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.Export
{
    public interface IChunkSerializeStrategy
    {
        void Serialize(BinaryWriter writer, ChunkData[] chunks);
    }
}
