using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    [Flags]
    public enum CreatureRole : byte
    {
        None = 0,
        Farming = 1 << 0,
        Gathering = 1 << 1,
        Hauling = 1 << 2,
        CraftAssist = 1 << 3,
        Feeding = 1 << 4,
        All = Farming | Gathering | Hauling | CraftAssist | Feeding
    }

    [Flags]
    public enum CreatureEnvironmentMask : ushort
    {
        None = 0,
        Sand = 1 << 0,
        Rock = 1 << 1,
        Coral = 1 << 2,
        Kelp = 1 << 3,
        OpenWater = 1 << 4,
        Cave = 1 << 5,
        Warm = 1 << 6,
        Cold = 1 << 7,
        Shallow = 1 << 8,
        Deep = 1 << 9
    }

    public enum CreatureSettleFailure : byte
    {
        None,
        InvalidTarget,
        NotTamed,
        NotCustomized,
        InvalidHabitat,
        HabitatFull,
        EnvironmentMissing,
        AlreadySettled,
        SizeClassNotAllowed
    }

    public enum CreatureWorkPhase : byte
    {
        Idle = 0,
        MoveToSite = 1,
        Interact = 2,
        MoveToDelivery = 3,
        Deposit = 4,
        Return = 5
    }

    public enum CreatureWorkSiteState : byte
    {
        Growing = 0,
        Ready = 1,
        Claimed = 2,
        Spent = 3
    }

    public struct CreatureEnvironmentNeeds : IComponentData
    {
        public CreatureEnvironmentMask Required;
        public CreatureEnvironmentMask Preferred;
    }

    public struct CreatureRoleAptitude : IComponentData
    {
        public CreatureRole Roles;
        public float BaseWorkSpeed;
        public int BaseCarryCapacity;
    }

    public struct CreatureCustomized : IComponentData, IEnableableComponent { }

    public struct CreatureSettlement : IComponentData
    {
        public Entity Habitat;
        public float3 LivingPosition;
        public float ActivityRadius;
        public float WorkSpeed;
        public int CarryCapacity;
    }

    public struct CreatureSettled : IComponentData, IEnableableComponent { }

    public struct CreatureWorkState : IComponentData
    {
        public CreatureWorkPhase Phase;
        public Entity Site;
        public Entity Delivery;
        public int CarriedItemId;
        public int CarriedCount;
        public double PhaseEndTime;
    }

    public struct CreatureMoveOrder : IComponentData, IEnableableComponent
    {
        public float3 Target;
        public float SpeedMultiplier;
        public float ArriveRadius;
    }

    public struct CreatureHabitat : IComponentData
    {
        public FixedString64Bytes DisplayName;
        public float3 HalfExtents;
        public CreatureEnvironmentMask Provided;
        public int Capacity;
        public byte AllowMedium;
    }

    [InternalBufferCapacity(16)]
    public struct CreatureHabitatMember : IBufferElementData
    {
        public Entity Value;
    }

    public struct CreatureWorkSite : IComponentData
    {
        public CreatureRole RequiredRole;
        public CreatureWorkSiteState State;
        public Entity Claimant;
        public Entity Habitat;
        public int OutputItemId;
        public int OutputCount;
        public float WorkSeconds;
        public float InteractRadius;
    }

    public struct CreatureWorkSiteReady : IComponentData, IEnableableComponent { }

    public struct CreatureWorkSiteRefresh : IComponentData
    {
        public float RefreshSeconds;
        public double NextReadyTime;
    }

    public struct CropPlot : IComponentData
    {
        public int SeedItemId;
        public float GrowSeconds;
        public double ReadyTime;
        public byte AutoReplant;
    }

    public struct CreatureStorage : IComponentData
    {
        public FixedString64Bytes DisplayName;
        public int SlotCapacity;
        public int StackCapacity;
        public Entity Habitat;
    }

    [InternalBufferCapacity(16)]
    public struct CreatureStorageSlot : IBufferElementData
    {
        public int ItemId;
        public int Count;
    }

    public struct CreatureStorageIndex : IComponentData
    {
        public uint OrderVersion;
    }

    [InternalBufferCapacity(8)]
    public struct CreatureStorageIndexEntry : IBufferElementData
    {
        public Entity Storage;
        public Entity Habitat;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureSettleRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public Entity Habitat;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureUnsettleRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureSettleResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public Entity Habitat;
        public CreatureSettleFailure Failure;
        public CreatureRole Roles;
        public float WorkSpeed;
        public int CarryCapacity;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureWorkCompletedEvent : IBufferElementData
    {
        public Entity Worker;
        public Entity Site;
        public Entity Storage;
        public CreatureRole Role;
        public int ItemId;
        public int Count;
        public int Accepted;
    }
}
