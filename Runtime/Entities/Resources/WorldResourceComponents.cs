using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

    public struct ResourceSpawnExclusion : IComponentData
    {
        public Unity.Mathematics.float3 Minimum;
        public Unity.Mathematics.float3 Maximum;

        public bool Contains(Unity.Mathematics.float3 position)
            => Unity.Mathematics.math.all(position >= Minimum) && Unity.Mathematics.math.all(position <= Maximum);
    }

    /// <summary>Shares the procedural layout's piece count with harvesting, including restored nodes.</summary>
    public struct ResourceNodePieces : IComponentData
    {
        public const byte SeaweedKind = 255;
        public byte Kind;
        public int MinimumDropMultiplier;
        public int MaximumDropMultiplier;
        public int LayoutSeed;
        public byte HasLayoutSeed;

        public int GetSeed(Unity.Mathematics.float3 position)
            => HasLayoutSeed != 0 ? LayoutSeed : PositionSeed(position, Kind);

        public static int PositionSeed(Unity.Mathematics.float3 position, byte kind)
        {
            unchecked
            {
                int seed = (int)Unity.Mathematics.math.round(position.x * 100f) * 73856093;
                seed ^= (int)Unity.Mathematics.math.round(position.y * 100f) * 19349663;
                seed ^= (int)Unity.Mathematics.math.round(position.z * 100f) * 83492791;
                seed ^= ((kind == SeaweedKind ? 0 : kind) + 1) * 1640531513;
                return kind == SeaweedKind ? seed ^ 192791 : seed;
            }
        }

        public static int RemainingCount(byte kind, int seed, float healthFraction)
        {
            var random = new System.Random(seed);
            int count;
            if (kind == SeaweedKind) count = random.Next(3, 6);
            else
            {
                random.NextDouble();
                count = kind == 1 ? random.Next(6, 10) : random.Next(5, 9);
            }
            return (int)Unity.Mathematics.math.ceil(count * Unity.Mathematics.math.saturate(healthFraction) - .0001f);
        }
    }

    /// <summary>
    /// Where each harvested piece was cut, as a fraction of its height (seaweed stalks leave a stump at the hit point).
    /// Only meaningful for pieces whose <see cref="ResourcePieceState"/> health is zero.
    /// </summary>
    public struct ResourcePieceCut : IComponentData
    {
        public FixedList32Bytes<byte> Height;

        public float Get(int piece) => piece >= 0 && piece < Height.Length ? Height[piece] / 255f : 0f;

        public void Set(int piece, float height)
        {
            if (piece < 0 || piece >= Height.Capacity) return;
            while (Height.Length <= piece) Height.Add(0);
            Height[piece] = (byte)Unity.Mathematics.math.round(Unity.Mathematics.math.saturate(height) * 255f);
        }
    }

    public struct ResourceMineralChip
    {
        public float3 Point;
        public float Damage;
        public int Piece;
    }

    public struct ResourcePieceState : IComponentData
    {
        public FixedList64Bytes<float> Health;
        public FixedList128Bytes<float3> Impacts;
        public int ImpactMask;
        public FixedList512Bytes<ResourceMineralChip> Chips;

        public void AddChip(int piece, float3 point, float damage)
        {
            if (damage <= 0f) return;
            if (Chips.Length < Chips.Capacity)
                Chips.Add(new ResourceMineralChip { Piece = piece, Point = point, Damage = damage });
        }

        public void SetImpact(int piece, float3 point)
        {
            if (piece < 0 || piece >= Impacts.Capacity || (ImpactMask & (1 << piece)) != 0) return;
            while (Impacts.Length <= piece) Impacts.Add(default);
            Impacts[piece] = point;
            ImpactMask |= 1 << piece;
        }
        public int BrokenMask
        {
            get
            {
                int mask = 0;
                for (int i = 0; i < Health.Length; i++) if (Health[i] <= .0001f) mask |= 1 << i;
                return mask;
            }
        }

        public void Initialize(int count, float healthFraction)
        {
            Health.Clear();
            Impacts.Clear();
            ImpactMask = 0;
            Chips.Clear();
            float available = count * Unity.Mathematics.math.saturate(healthFraction);
            for (int i = 0; i < count; i++) Health.Add(Unity.Mathematics.math.saturate(available - i));
        }
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
        public float MinimumSpacing;
        public float SpawnInterval;
        public double NextSpawnTime;
        public int MaximumAlive;
        public int SpawnPerTick;
        public int PendingSpawnAttempts;
        public byte InitialPopulationComplete;
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
        public int PieceIndex;
        public byte HasTargetPiece;
        public byte GrantToInventory;
        /// <summary>Hit height along the targeted piece (0 = base, 1 = tip); used when <see cref="HasCut"/> is set.</summary>
        public float CutHeight;
        public byte HasCut;
        public float3 HitPoint;
        public byte HasHitPoint;
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
        public byte IsHarvest;
        public ResourceDropSpawnRequest HarvestDrop;
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
