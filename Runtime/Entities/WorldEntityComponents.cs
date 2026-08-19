using System;
using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities
{
    public enum WorldEntityKind : byte
    {
        Generic,
        Creature,
        Resource,
        DroppedItem,
        Projectile,
        Effect
    }

    [Flags]
    public enum WorldEntityFlags : ushort
    {
        None = 0,
        Persistent = 1 << 0,
        RegionStreamed = 1 << 1,
        Replicated = 1 << 2
    }

    public struct WorldEntityIdentity : IComponentData, IEquatable<WorldEntityIdentity>
    {
        public ulong High;
        public ulong Low;

        public bool IsValid => High != 0 || Low != 0;
        public bool Equals(WorldEntityIdentity other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is WorldEntityIdentity other && Equals(other);
        public override int GetHashCode() => unchecked(((int)High * 397) ^ (int)Low);
    }

    public struct WorldEntityDescriptor : IComponentData
    {
        public int PrefabId;
        public WorldEntityKind Kind;
        public WorldEntityFlags Flags;
    }

    public struct WorldEntityChunk : IComponentData
    {
        public int2 Chunk;
        public int2 Region;
    }

    public struct WorldEntityVelocity : IComponentData
    {
        public float3 Value;
    }

    public struct WorldEntityLifetime : IComponentData
    {
        public float RemainingSeconds;
    }

    public struct WorldEntityTrackChunk : IComponentData { }

    public struct WorldEntityActive : IComponentData, IEnableableComponent { }

    public struct WorldEntityRuntimeConfig : IComponentData
    {
        public float ChunkSize;
        public int ChunksPerRegion;
        public float3 WorldOrigin;
        public uint RegionRevision;
        public ulong NextRuntimeId;
    }

    [InternalBufferCapacity(32)]
    public struct WorldEntityPrefabElement : IBufferElementData
    {
        public int PrefabId;
        public Entity Prefab;
    }

    [InternalBufferCapacity(0)]
    public struct WorldEntitySpawnRequest : IBufferElementData
    {
        public int PrefabId;
        public WorldEntityIdentity Identity;
        public float3 Position;
        public quaternion Rotation;
        public float UniformScale;
        public float3 Velocity;
        public float RemainingLifetime;
        public float ResourceHealth;
        public float ResourceRespawnRemaining;
        public int DroppedItemId;
        public int DroppedItemCount;
        public WorldEntitySpawnRequestFlags StateFlags;
    }

    [Flags]
    public enum WorldEntitySpawnRequestFlags : byte
    {
        None = 0,
        RestoreVelocity = 1 << 0,
        RestoreLifetime = 1 << 1,
        RestoreResourceHealth = 1 << 2,
        RestoreResourceRespawn = 1 << 3,
        RestoreDroppedItem = 1 << 4
    }

    [InternalBufferCapacity(16)]
    public struct WorldEntityLoadedRegion : IBufferElementData
    {
        public int2 Coordinate;
    }
}
