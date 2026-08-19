using System;

namespace WorldBuilder.Baking.BlenderBridge
{
    [Serializable]
    public sealed class BakeManifest
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string worldId = string.Empty;
        public ChunkManifestCoord chunk = new ChunkManifestCoord();
        public string profileHash = string.Empty;
        public BakeManifestObject[] objects = Array.Empty<BakeManifestObject>();
        public VertexAttributeContract[] vertexAttributes = Array.Empty<VertexAttributeContract>();
    }

    [Serializable]
    public sealed class BakeManifestObject
    {
        public string stableId = string.Empty;
        public string name = string.Empty;
        public string role = string.Empty;
        public string assetId = string.Empty;
        public BakeManifestLod[] lods = Array.Empty<BakeManifestLod>();
        public BakeManifestCollider collider;
    }

    [Serializable]
    public sealed class BakeManifestLod
    {
        public int level;
        public string fileObject = string.Empty;
        public int triangles;
    }

    [Serializable]
    public sealed class BakeManifestCollider
    {
        public string type = string.Empty;
        public string fileObject = string.Empty;
    }

    [Serializable]
    public sealed class VertexAttributeContract
    {
        public string name = string.Empty;
        public string domain = string.Empty;
        public VertexAttributeChannels channels = new VertexAttributeChannels();
    }

    [Serializable]
    public sealed class VertexAttributeChannels
    {
        public string R = string.Empty;
        public string G = string.Empty;
        public string B = string.Empty;
        public string A = string.Empty;
    }
}
