using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    [RequireComponent(typeof(WorldBuilder.Entities.Authoring.WorldEntityAuthoring))]
    [AddComponentMenu("WorldBuilder/Entities/Creature")]
    public sealed class CreatureAuthoring : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Creature";
        [SerializeField] private CreatureGrade fallbackGrade = CreatureGrade.Common;
        [SerializeField] private CreatureSizeClass sizeClass = CreatureSizeClass.Small;
        [SerializeField] private CreaturePersonality personality = CreaturePersonality.Neutral;
        [SerializeField] private CreatureInteractionMask interactions =
            CreatureInteractionMask.Capture | CreatureInteractionMask.Scan | CreatureInteractionMask.Feed |
            CreatureInteractionMask.Recolor;
        [SerializeField] private uint randomSeed = 1;

        [Header("Appearance")]
        [Tooltip("Patterns this species model actually supports.")]
        [SerializeField] private CreaturePatternMask supportedPatterns = CreaturePatternMask.None;
        [Tooltip("Only used when a spawn explicitly overrides the grade palette.")]
        [SerializeField] private Color primaryColor = new Color(0.72f, 0.74f, 0.75f);
        [SerializeField] private Color secondaryColor = new Color(0.58f, 0.60f, 0.62f);
        [SerializeField] private Color accentColor = new Color(0.84f, 0.85f, 0.86f);
        [SerializeField] private Color patternColor = new Color(0.35f, 0.36f, 0.38f);

        [Header("Swim")]
        [Min(0f), SerializeField] private float cruiseSpeed = 1.5f;
        [Min(0f), SerializeField] private float turnSpeedDegrees = 120f;
        [Min(0f), SerializeField] private float wanderRadius = 12f;
        [Min(0f), SerializeField] private float verticalRadius = 4f;
        [Min(0.05f), SerializeField] private float arriveRadius = 0.75f;
        [Min(0.1f), SerializeField] private float repathIntervalSeconds = 4f;

        [Header("Streaming")]
        [SerializeField] private bool leashToHomeRegion = true;
        [Min(0f), SerializeField] private float regionMargin = 2f;
        [Tooltip("Destroy this creature after its region has been unloaded for this long. 0 disables despawning.")]
        [Min(0f), SerializeField] private float despawnGraceSeconds = 10f;

        [Header("Taming")]
        [Tooltip("Guaranteed success on this attempt number. The design target is 3 or 4.")]
        [Range(1, 8), SerializeField] private int guaranteedAfterAttempts = 4;
        [Tooltip("Added to the success chance when fed the preferred food.")]
        [Range(0f, 1f), SerializeField] private float preferredFoodBonus = 0.2f;
        [Tooltip("-1 accepts any food item as a taming attempt.")]
        [SerializeField] private int preferredFeedItemId = -1;
        [Min(0f), SerializeField] private float maximumAffinity = 100f;
        [Min(0f), SerializeField] private float affinityPerFeed = 25f;
        [Min(0f), SerializeField] private float feedCooldownSeconds = 2f;
        [Tooltip("Overrides the personality flee profile when greater than zero.")]
        [Min(0f), SerializeField] private float fleeDistanceOverride;

        [Header("Player Reaction")]
        [Tooltip("Leave at Ignore on both to derive the reaction from personality.")]
        [SerializeField] private CreatureReaction wildReaction = CreatureReaction.Ignore;
        [SerializeField] private CreatureReaction tamedReaction = CreatureReaction.Gather;
        [Tooltip("0 derives detection and spacing from the personality profile.")]
        [Min(0f), SerializeField] private float detectionRadius;
        [Min(0f), SerializeField] private float personalSpace;
        [Min(0.1f), SerializeField] private float approachDistance = 2.5f;

        [Header("Settlement")]
        [Tooltip("Roles this species can perform once settled. Colour and pattern only tune them.")]
        [SerializeField] private CreatureRole roles = CreatureRole.Gathering | CreatureRole.Hauling;
        [Min(0.1f), SerializeField] private float baseWorkSpeed = 1f;
        [Min(1), SerializeField] private int baseCarryCapacity = 1;
        [Tooltip("All of these must be provided by the habitat or the creature cannot settle.")]
        [SerializeField] private CreatureEnvironmentMask requiredEnvironment = CreatureEnvironmentMask.OpenWater;
        [Tooltip("Optional. Met preferences grant a work speed bonus but never block settling.")]
        [SerializeField] private CreatureEnvironmentMask preferredEnvironment = CreatureEnvironmentMask.None;

        [Header("Capture")]
        [SerializeField] private int captureItemId = -1;
        [Min(1), SerializeField] private int captureBaseCount = 1;
        [Tooltip("-1 accepts any held item.")]
        [SerializeField] private int requiredToolItemId = -1;
        [Min(0), SerializeField] private int minimumToolTier;

        private void OnValidate()
        {
            cruiseSpeed = Mathf.Max(0f, cruiseSpeed);
            turnSpeedDegrees = Mathf.Max(0f, turnSpeedDegrees);
            wanderRadius = Mathf.Max(0f, wanderRadius);
            verticalRadius = Mathf.Max(0f, verticalRadius);
            arriveRadius = Mathf.Max(0.05f, arriveRadius);
            repathIntervalSeconds = Mathf.Max(0.1f, repathIntervalSeconds);
            regionMargin = Mathf.Max(0f, regionMargin);
            despawnGraceSeconds = Mathf.Max(0f, despawnGraceSeconds);
            captureBaseCount = Mathf.Max(1, captureBaseCount);
            baseWorkSpeed = Mathf.Max(0.1f, baseWorkSpeed);
            baseCarryCapacity = Mathf.Max(1, baseCarryCapacity);
            minimumToolTier = Mathf.Clamp(minimumToolTier, 0, byte.MaxValue);
            maximumAffinity = Mathf.Max(0f, maximumAffinity);
            guaranteedAfterAttempts = Mathf.Clamp(guaranteedAfterAttempts, 1, 8);
            if (randomSeed == 0) randomSeed = 1;
            if (sizeClass == CreatureSizeClass.Large)
                interactions &= ~(CreatureInteractionMask.Feed | CreatureInteractionMask.Recolor);
        }

        private sealed class CreatureBaker : Baker<CreatureAuthoring>
        {
            public override void Bake(CreatureAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                CreatureAppearance appearance = new CreatureAppearance
                {
                    Primary = ToFloat4(authoring.primaryColor),
                    Secondary = ToFloat4(authoring.secondaryColor),
                    Accent = ToFloat4(authoring.accentColor),
                    PatternColor = ToFloat4(authoring.patternColor),
                    Pattern = CreaturePatternKind.None,
                    PatternStrength = 0f
                };

                CreatureInteractionMask interactions = authoring.interactions;
                if (authoring.sizeClass == CreatureSizeClass.Large)
                    interactions &= ~(CreatureInteractionMask.Feed | CreatureInteractionMask.Recolor);

                AddComponent(entity, new Creature
                {
                    DisplayName = new FixedString64Bytes(authoring.displayName ?? string.Empty),
                    Grade = authoring.fallbackGrade,
                    SizeClass = authoring.sizeClass,
                    Personality = authoring.personality,
                    Interactions = interactions
                });
                AddComponent(entity, new CreatureRandom
                {
                    State = CreatureGradeRules.SanitizeSeed(authoring.randomSeed)
                });
                AddComponent(entity, appearance);
                AddComponent(entity, new CreatureSupportedPatterns { Value = authoring.supportedPatterns });
                AddComponent<CreatureAppearanceDirty>(entity);
                SetComponentEnabled<CreatureAppearanceDirty>(entity, true);

                AddComponent(entity, new CreatureSwim
                {
                    CruiseSpeed = authoring.cruiseSpeed,
                    TurnSpeedRadians = math.radians(authoring.turnSpeedDegrees),
                    WanderRadius = authoring.wanderRadius,
                    VerticalRadius = authoring.verticalRadius,
                    ArriveRadius = authoring.arriveRadius,
                    RepathIntervalSeconds = authoring.repathIntervalSeconds,
                    RegionMargin = authoring.regionMargin,
                    LeashToHomeRegion = (byte)(authoring.leashToHomeRegion ? 1 : 0)
                });

                CreatureAlarm alarm = CreatureTamingRules.FromPersonality(authoring.personality);
                if (authoring.fleeDistanceOverride > 0f) alarm.FleeDistance = authoring.fleeDistanceOverride;
                AddComponent(entity, alarm);

                AddComponent(entity, CreatureTamingRules.FromPersonality(authoring.personality,
                    authoring.preferredFoodBonus, (byte)authoring.guaranteedAfterAttempts));
                AddComponent<CreatureTamed>(entity);
                SetComponentEnabled<CreatureTamed>(entity, false);

                AddComponent(entity, new CreatureAffinity
                {
                    Value = 0f,
                    MaximumValue = authoring.maximumAffinity,
                    GainPerFeed = authoring.affinityPerFeed,
                    FeedCooldownSeconds = authoring.feedCooldownSeconds,
                    PreferredItemId = authoring.preferredFeedItemId
                });

                AddComponent(entity, new CreatureCapture
                {
                    ItemId = authoring.captureItemId,
                    BaseCount = Mathf.Max(1, authoring.captureBaseCount),
                    RequiredToolItemId = authoring.requiredToolItemId,
                    MinimumToolTier = (byte)Mathf.Clamp(authoring.minimumToolTier, 0, byte.MaxValue)
                });
                AddComponent<CreatureCaptured>(entity);
                SetComponentEnabled<CreatureCaptured>(entity, false);

                CreaturePerception perception = CreaturePerceptionRules.FromPersonality(authoring.personality);
                if (authoring.wildReaction != CreatureReaction.Ignore)
                    perception.WildReaction = authoring.wildReaction;
                perception.TamedReaction = authoring.tamedReaction;
                if (authoring.detectionRadius > 0f) perception.DetectionRadius = authoring.detectionRadius;
                if (authoring.personalSpace > 0f) perception.PersonalSpace = authoring.personalSpace;
                perception.ApproachDistance = Mathf.Max(0.1f, authoring.approachDistance);
                AddComponent(entity, perception);

                AddComponent(entity, new CreatureRoleAptitude
                {
                    Roles = authoring.sizeClass == CreatureSizeClass.Large ? CreatureRole.None : authoring.roles,
                    BaseWorkSpeed = Mathf.Max(0.1f, authoring.baseWorkSpeed),
                    BaseCarryCapacity = Mathf.Max(1, authoring.baseCarryCapacity)
                });
                AddComponent(entity, new CreatureEnvironmentNeeds
                {
                    Required = authoring.requiredEnvironment,
                    Preferred = authoring.preferredEnvironment
                });
                AddComponent<CreatureCustomized>(entity);
                SetComponentEnabled<CreatureCustomized>(entity, false);
                AddComponent(entity, new CreatureSettlement());
                AddComponent<CreatureSettled>(entity);
                SetComponentEnabled<CreatureSettled>(entity, false);
                AddComponent(entity, new CreatureWorkState { CarriedItemId = -1 });
                AddComponent(entity, new CreatureMoveOrder { SpeedMultiplier = 1f, ArriveRadius = 1f });
                SetComponentEnabled<CreatureMoveOrder>(entity, false);

                if (authoring.despawnGraceSeconds > 0f)
                    AddComponent(entity, new CreatureStreaming
                    {
                        DespawnGraceSeconds = authoring.despawnGraceSeconds
                    });

                BakeAppearanceTargets(authoring, entity, appearance);
            }

            private void BakeAppearanceTargets(CreatureAuthoring authoring, Entity root,
                in CreatureAppearance appearance)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>();
                DynamicBuffer<CreatureAppearanceTarget> targets = AddBuffer<CreatureAppearanceTarget>(root);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null) continue;
                    Entity target = GetEntity(renderer.gameObject, TransformUsageFlags.Renderable);
                    if (target == Entity.Null) continue;

                    AddComponent(target, new CreaturePrimaryColor { Value = appearance.Primary });
                    AddComponent(target, new CreatureSecondaryColor { Value = appearance.Secondary });
                    AddComponent(target, new CreatureAccentColor { Value = appearance.Accent });
                    AddComponent(target, new CreaturePatternColor { Value = appearance.PatternColor });
                    AddComponent(target, new CreaturePatternParams
                    {
                        Value = CreatureAppearanceRules.PatternParameters(appearance)
                    });

                    if (target == root) continue;
                    AddComponent(target, appearance);
                    AddComponent<CreatureAppearanceDirty>(target);
                    SetComponentEnabled<CreatureAppearanceDirty>(target, true);
                    targets.Add(new CreatureAppearanceTarget { Value = target });
                }
            }

            private static float4 ToFloat4(Color color) => new float4(color.r, color.g, color.b, color.a);
        }
    }
}
