using System;
using Unity.Collections;
using Unity.Entities;

namespace WorldBuilder.Entities.Resources
{
    [Flags]
    public enum HarvestMethod : byte
    {
        None = 0,
        Hand = 1 << 0,
        Axe = 1 << 1,
        Pickaxe = 1 << 2,
        Drill = 1 << 3,
        AnyTool = Axe | Pickaxe | Drill
    }

    public enum HarvestFailureReason : byte
    {
        None,
        InvalidTarget,
        Respawning,
        Cooldown,
        WrongMethod,
        RequiredToolMissing,
        ToolTierTooLow,
        ToolPowerTooLow
    }

    public struct ResourceNode : IComponentData
    {
        public FixedString64Bytes DisplayName;
        public float MaxHealth;
        public float Health;
        public float HitCooldownSeconds;
        public double NextHitTime;
        public float RespawnSeconds;
        public HarvestMethod AllowedMethods;
        public int RequiredToolItemId;
        public byte MinimumToolTier;
        public float MinimumToolPower;
        public int DroppedItemPrefabId;
        public uint RandomSeed;
        public uint DepletionCount;
    }

    [InternalBufferCapacity(4)]
    public struct ResourceDrop : IBufferElementData
    {
        public int ItemId;
        public int MinimumCount;
        public int MaximumCount;
        public float Probability;
    }

    public struct ResourceRespawnState : IComponentData
    {
        public float RemainingSeconds;
    }

    /// <summary>Optional world-wide regeneration rate; absent means the designed rate of 1.</summary>
    public struct ResourceRegenerationRate : IComponentData
    {
        public float Value;
    }

    public struct DroppedItem : IComponentData
    {
        public int ItemId;
        public int Count;
        public FixedString64Bytes DisplayName;
    }

    public struct DroppedItemPendingPickup : IComponentData, IEnableableComponent { }

    public enum ResourceFieldSpawnKind : byte
    {
        [UnityEngine.InspectorName("채집 자원")]
        ResourceNode,
        [UnityEngine.InspectorName("떨어진 아이템")]
        DroppedItem
    }

    public struct ResourceFieldSpawnZone : IComponentData
    {
        public ResourceFieldSpawnKind Kind;
        public int PrefabId;
        public int ItemId;
        public int MinimumItemCount;
        public int MaximumItemCount;
        public Unity.Mathematics.float3 HalfExtents;
        public float RaycastHeight;
        public float MinimumGroundNormalY;
        public float SpawnInterval;
        public double NextSpawnTime;
        public int MaximumAlive;
        public int SpawnPerTick;
        public uint RandomState;
    }

    [InternalBufferCapacity(4)]
    public struct ResourceFieldSpawnEntry : IBufferElementData
    {
        public int PrefabId;
        public int ItemId;
        public float Weight;
        public int MinimumCount;
        public int MaximumCount;
    }

    [InternalBufferCapacity(12)]
    public struct ResourceFieldSpawnPoint : IBufferElementData
    {
        public Unity.Mathematics.float2 Value;
    }

    public struct ResourceSpawnTerrain : IComponentData { }

    [InternalBufferCapacity(16)]
    public struct ResourceFieldSpawnedEntity : IBufferElementData
    {
        public Entity Value;
    }

    [InternalBufferCapacity(0)]
    public struct ResourceHarvestRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public HarvestMethod Method;
        public int ToolItemId;
        public byte ToolTier;
        public float ToolPower;
        public float Damage;
    }

    [InternalBufferCapacity(0)]
    public struct ResourceHarvestResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public HarvestFailureReason Failure;
        public float RemainingHealth;
        public byte Depleted;
    }

    [InternalBufferCapacity(0)]
    public struct ResourceDropSpawnRequest : IBufferElementData
    {
        public int PrefabId;
        public int ItemId;
        public int Count;
        public Unity.Mathematics.float3 Position;
        public uint Seed;
    }

    [InternalBufferCapacity(0)]
    public struct DroppedItemPickupRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
    }

    [InternalBufferCapacity(0)]
    public struct InventoryGrantRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public int ItemId;
        public int RequestedCount;
    }

    [InternalBufferCapacity(0)]
    public struct InventoryGrantResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public int AcceptedCount;
    }

    public readonly struct ResourceInteractionInfo
    {
        public readonly bool IsResource;
        public readonly bool IsDroppedItem;
        public readonly string DisplayName;
        public readonly HarvestMethod AllowedMethods;
        public readonly int RequiredToolItemId;
        public readonly byte MinimumToolTier;
        public readonly float MinimumToolPower;

        public ResourceInteractionInfo(ResourceNode node)
        {
            IsResource = true;
            IsDroppedItem = false;
            DisplayName = node.DisplayName.ToString();
            AllowedMethods = node.AllowedMethods;
            RequiredToolItemId = node.RequiredToolItemId;
            MinimumToolTier = node.MinimumToolTier;
            MinimumToolPower = node.MinimumToolPower;
        }

        public ResourceInteractionInfo(DroppedItem item)
        {
            IsResource = false;
            IsDroppedItem = true;
            DisplayName = item.DisplayName.ToString();
            AllowedMethods = HarvestMethod.None;
            RequiredToolItemId = -1;
            MinimumToolTier = 0;
            MinimumToolPower = 0f;
        }
    }
}
