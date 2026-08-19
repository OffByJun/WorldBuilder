using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public enum CreatureGrade : byte
    {
        Common = 0,
        Rare = 10,
        Legendary = 20
    }

    [Flags]
    public enum CreatureGradeMask : byte
    {
        None = 0,
        Common = 1 << 0,
        Rare = 1 << 1,
        Legendary = 1 << 2,
        All = Common | Rare | Legendary
    }

    public enum CreatureSizeClass : byte
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    public enum CreaturePersonality : byte
    {
        Friendly = 0,
        Neutral = 1,
        Wary = 2,
        Timid = 3
    }

    [Flags]
    public enum CreatureInteractionMask : byte
    {
        None = 0,
        Capture = 1 << 0,
        Scan = 1 << 1,
        Feed = 1 << 2,
        Recolor = 1 << 3
    }

    public enum CreatureColorSlot : byte
    {
        Primary = 0,
        Secondary = 1,
        Accent = 2,
        Pattern = 3
    }

    public enum CreaturePatternKind : byte
    {
        None = 0,
        Stripes = 1,
        Spots = 2,
        TwoTone = 3,
        Gradient = 4,
        Special = 5
    }

    [Flags]
    public enum CreaturePatternMask : byte
    {
        None = 0,
        Stripes = 1 << 0,
        Spots = 1 << 1,
        TwoTone = 1 << 2,
        Gradient = 1 << 3,
        Special = 1 << 4,
        All = Stripes | Spots | TwoTone | Gradient | Special
    }

    public enum CreatureCaptureFailure : byte
    {
        None,
        InvalidTarget,
        NotCapturable,
        AlreadyCaptured,
        Tamed,
        RequiredToolMissing,
        ToolTierTooLow
    }

    public enum CreatureFeedFailure : byte
    {
        None,
        InvalidTarget,
        NotFeedable,
        WrongItem,
        Cooldown,
        Alarmed,
        AlreadyTamed
    }

    public enum CreatureRecolorFailure : byte
    {
        None,
        InvalidTarget,
        NotRecolorable,
        NotTamed,
        UnknownPalette,
        UnsupportedPattern
    }

    public struct Creature : IComponentData
    {
        public FixedString64Bytes DisplayName;
        public CreatureGrade Grade;
        public CreatureSizeClass SizeClass;
        public CreaturePersonality Personality;
        public CreatureInteractionMask Interactions;
    }

    public struct CreatureRandom : IComponentData
    {
        public uint State;
    }

    public struct CreatureAppearance : IComponentData
    {
        public float4 Primary;
        public float4 Secondary;
        public float4 Accent;
        public float4 PatternColor;
        public CreaturePatternKind Pattern;
        public float PatternStrength;
    }

    public struct CreatureAppearanceDirty : IComponentData, IEnableableComponent { }

    public struct CreatureSupportedPatterns : IComponentData
    {
        public CreaturePatternMask Value;
    }

    [InternalBufferCapacity(4)]
    public struct CreatureAppearanceTarget : IBufferElementData
    {
        public Entity Value;
    }

    public struct CreatureSwim : IComponentData
    {
        public float3 Home;
        public float3 TargetPoint;
        public int2 HomeRegion;
        public float CruiseSpeed;
        public float TurnSpeedRadians;
        public float WanderRadius;
        public float VerticalRadius;
        public float ArriveRadius;
        public float RepathIntervalSeconds;
        public double NextRepathTime;
        public float RegionMargin;
        public byte LeashToHomeRegion;
    }

    public struct CreatureAlarm : IComponentData
    {
        public float3 FleeFrom;
        public float FleeDistance;
        public float FleeSpeedMultiplier;
        public float AlarmDurationSeconds;
        public double AlarmedUntil;
    }

    public struct CreatureStreaming : IComponentData
    {
        public float DespawnGraceSeconds;
        public double UnloadedSince;
    }

    public struct CreatureCapture : IComponentData
    {
        public int ItemId;
        public int BaseCount;
        public int RequiredToolItemId;
        public byte MinimumToolTier;
    }

    public struct CreatureCaptured : IComponentData, IEnableableComponent { }

    public struct CreatureAffinity : IComponentData
    {
        public float Value;
        public float MaximumValue;
        public float GainPerFeed;
        public float FeedCooldownSeconds;
        public double NextFeedTime;
        public int PreferredItemId;
    }

    public struct CreatureTaming : IComponentData
    {
        public float BaseSuccessChance;
        public float FailureBonus;
        public float PreferredFoodBonus;
        public byte GuaranteedAfterAttempts;
        public byte AttemptCount;
        public byte IsTamed;
    }

    public struct CreatureTamed : IComponentData, IEnableableComponent { }

    [InternalBufferCapacity(8)]
    public struct CreatureGradeDefinition : IBufferElementData
    {
        public CreatureGrade Grade;
        public float4 HsvMinimum;
        public float4 HsvMaximum;
        public float SpawnSaturationScale;
        public float SpeedMultiplier;
        public float SizeMultiplier;
        public float ValueMultiplier;
        public float TameChanceMultiplier;
        public float SpawnWeight;
    }

    [InternalBufferCapacity(32)]
    public struct CreaturePaletteEntry : IBufferElementData
    {
        public int PaletteId;
        public float4 Color;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureSpawnRequest : IBufferElementData
    {
        public int PrefabId;
        public Entity Owner;
        public WorldEntityIdentity Identity;
        public float3 Position;
        public quaternion Rotation;
        public float3 Home;
        public CreatureGrade Grade;
        public CreatureAppearance Appearance;
        public float Affinity;
        public byte TameAttempts;
        public byte IsTamed;
        public uint Seed;
        public CreatureSpawnRequestFlags StateFlags;
    }

    [Flags]
    public enum CreatureSpawnRequestFlags : byte
    {
        None = 0,
        ExplicitGrade = 1 << 0,
        ExplicitAppearance = 1 << 1,
        ExplicitHome = 1 << 2,
        ExplicitIdentity = 1 << 3,
        ExplicitAffinity = 1 << 4,
        ExplicitTaming = 1 << 5
    }

    public struct CreatureSpawnZone : IComponentData
    {
        public int PrefabId;
        public float3 HalfExtents;
        public CreatureGradeMask AllowedGrades;
        public float SpawnInterval;
        public double NextSpawnTime;
        public int MaximumAlive;
        public int SpawnPerTick;
        public uint RandomState;
    }

    [InternalBufferCapacity(16)]
    public struct CreatureSpawnedEntity : IBufferElementData
    {
        public Entity Value;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureDespawnRequest : IBufferElementData
    {
        public Entity Target;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureCaptureRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public int ToolItemId;
        public byte ToolTier;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureCaptureResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public CreatureCaptureFailure Failure;
        public int ItemId;
        public int Count;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureFeedRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public int ItemId;
        public float3 SourcePosition;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureFeedResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public CreatureFeedFailure Failure;
        public float Affinity;
        public float MaximumAffinity;
        public float SuccessChance;
        public byte AttemptCount;
        public byte TamedNow;
        public byte IsTamed;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureRecolorRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public CreatureColorSlot Slot;
        public int PaletteId;
    }

    [InternalBufferCapacity(0)]
    public struct CreaturePatternRequest : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public CreaturePatternKind Pattern;
        public int PaletteId;
        public float Strength;
    }

    [InternalBufferCapacity(0)]
    public struct CreatureRecolorResult : IBufferElementData
    {
        public uint RequestId;
        public Entity Target;
        public CreatureRecolorFailure Failure;
        public CreatureAppearance Appearance;
    }

    public readonly struct CreatureInteractionInfo
    {
        public readonly string DisplayName;
        public readonly CreatureGrade Grade;
        public readonly CreatureSizeClass SizeClass;
        public readonly CreaturePersonality Personality;
        public readonly CreatureInteractionMask Interactions;
        public readonly CreatureAppearance Appearance;
        public readonly CreaturePatternMask SupportedPatterns;
        public readonly int CaptureItemId;
        public readonly int RequiredToolItemId;
        public readonly byte MinimumToolTier;
        public readonly int PreferredFoodItemId;
        public readonly float Affinity;
        public readonly float MaximumAffinity;
        public readonly byte TameAttempts;
        public readonly bool IsTamed;
        public readonly bool IsAlarmed;

        public CreatureInteractionInfo(in Creature creature, in CreatureAppearance appearance,
            CreaturePatternMask supportedPatterns, in CreatureCapture capture, in CreatureAffinity affinity,
            in CreatureTaming taming, bool isAlarmed)
        {
            DisplayName = creature.DisplayName.ToString();
            Grade = creature.Grade;
            SizeClass = creature.SizeClass;
            Personality = creature.Personality;
            Interactions = creature.Interactions;
            Appearance = appearance;
            SupportedPatterns = supportedPatterns;
            CaptureItemId = capture.ItemId;
            RequiredToolItemId = capture.RequiredToolItemId;
            MinimumToolTier = capture.MinimumToolTier;
            PreferredFoodItemId = affinity.PreferredItemId;
            Affinity = affinity.Value;
            MaximumAffinity = affinity.MaximumValue;
            TameAttempts = taming.AttemptCount;
            IsTamed = taming.IsTamed != 0;
            IsAlarmed = isAlarmed;
        }

        public bool CanCapture => (Interactions & CreatureInteractionMask.Capture) != 0;
        public bool CanScan => (Interactions & CreatureInteractionMask.Scan) != 0;
        public bool CanFeed => (Interactions & CreatureInteractionMask.Feed) != 0;
        public bool CanRecolor => (Interactions & CreatureInteractionMask.Recolor) != 0;
    }
}
