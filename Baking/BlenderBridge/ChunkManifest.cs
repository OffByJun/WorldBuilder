using System;
using UnityEngine;

namespace WorldBuilder.Baking.BlenderBridge
{
    [Serializable]
    public sealed class ChunkManifest
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public string worldId = string.Empty;
        public ChunkManifestCoord chunk = new ChunkManifestCoord();
        public ChunkManifestCoord region = new ChunkManifestCoord();
        public float chunkSize = 128f;
        public Vector3 localOrigin = Vector3.zero;
        public ChunkCoordinateSystem coordinateSystem = new ChunkCoordinateSystem();
        public ChunkExporterInfo exporter = new ChunkExporterInfo();
        public ChunkSourceInfo source = new ChunkSourceInfo();
        public string contentHash = string.Empty;
        public ChunkManifestContent content = new ChunkManifestContent();
    }

    [Serializable]
    public sealed class ChunkManifestCoord
    {
        public int x;
        public int z;
    }

    [Serializable]
    public sealed class ChunkCoordinateSystem
    {
        public string unit = "meter";
        public float unitsPerMeter = 1f;
        public string blenderUpAxis = "Z";
        public string blenderForwardAxis = "Y";
        public string unityUpAxis = "Y";
        public string unityForwardAxis = "Z";
        public string vectorMapping = "XZY";
    }

    [Serializable]
    public sealed class ChunkExporterInfo
    {
        public string name = "WorldBuilder Blender Add-on";
        public string version = "1.0.0";
    }

    [Serializable]
    public sealed class ChunkSourceInfo
    {
        public string blendFile = string.Empty;
        public string collection = string.Empty;
        public string authoringHash = string.Empty;
    }

    [Serializable]
    public sealed class ChunkFileReference
    {
        public string path = string.Empty;
        public string sha256 = string.Empty;
        public long bytes;
        public bool IsPresent => !string.IsNullOrWhiteSpace(path);
    }

    [Serializable]
    public sealed class ChunkBoundsRecord
    {
        public Vector3 min;
        public Vector3 max;
    }

    [Serializable]
    public sealed class ChunkManifestContent
    {
        public ChunkFileReference geometry = new ChunkFileReference();
        public ChunkFileReference collision = new ChunkFileReference();
        public ChunkFileReference placements = new ChunkFileReference();
        public int geometryObjectCount;
        public int collisionObjectCount;
        public int placementCount;
        public ChunkBoundsRecord localBounds = new ChunkBoundsRecord();
    }

    [Serializable]
    public sealed class ChunkPlacementDocument
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string worldId = string.Empty;
        public ChunkManifestCoord chunk = new ChunkManifestCoord();
        public ChunkPlacementRecord[] objects = Array.Empty<ChunkPlacementRecord>();
    }

    [Serializable]
    public sealed class ChunkPlacementRecord
    {
        public const string InstanceRole = "INSTANCE";
        public const string MarkerRole = "MARKER";
        public const string EntityRole = "ENTITY";

        public string stableId = string.Empty;
        public string name = string.Empty;
        public string role = string.Empty;
        public string assetId = string.Empty;
        public int layer;
        public float[] matrix = Array.Empty<float>();
        public ChunkPropertyRecord[] properties = Array.Empty<ChunkPropertyRecord>();
        public ChunkEntityRecord entity;

        public bool IsInstance => string.Equals(role, InstanceRole, StringComparison.Ordinal);
        public bool IsEntity => string.Equals(role, EntityRole, StringComparison.Ordinal);
        public bool UsesAsset => IsInstance || IsEntity;
    }

    [Serializable]
    public sealed class ChunkEntityRecord
    {
        public static readonly string[] KindNames =
        {
            "Generic", "Creature", "Resource", "DroppedItem", "Projectile", "Effect"
        };

        public int prefabId;
        public string kind = "Generic";
        public string[] flags = Array.Empty<string>();
        public float lifetimeSeconds;

        public bool HasFlag(string value)
        {
            if (flags == null) return false;
            for (int i = 0; i < flags.Length; i++)
                if (string.Equals(flags[i], value, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    [Serializable]
    public sealed class ChunkPropertyRecord
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }
}
