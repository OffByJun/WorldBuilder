using System.IO;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.Export
{
    public sealed class ChunkSerializer
    {
        private readonly IChunkSerializeStrategy strategy;

        public ChunkSerializer(IChunkSerializeStrategy strategy)
        {
            this.strategy = strategy;
        }

        public void Write(string path, ChunkData[] chunks)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                strategy.Serialize(writer, chunks);
            }
        }
    }
}
