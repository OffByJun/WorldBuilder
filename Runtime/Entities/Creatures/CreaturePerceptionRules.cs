using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public enum CreatureReaction : byte
    {
        Ignore = 0,
        Flee = 1,
        Approach = 2,
        Gather = 3
    }

    public struct CreaturePerception : IComponentData
    {
        public CreatureReaction WildReaction;
        public CreatureReaction TamedReaction;
        public float DetectionRadius;
        public float PersonalSpace;
        public float ApproachDistance;
        public float ReactionSpeedMultiplier;
        public CreatureReaction CurrentReaction;
        public byte PlayerDetected;
        public uint AlertVersion;
        public double AlertUntil;
    }

    public struct CreaturePlayerFocus : IComponentData
    {
        public float3 Position;
        public byte IsValid;
    }

    /// <summary>동행 생물이 주는 플레이어 주변 감지·탐색 보조다.</summary>
    public struct CreaturePlayerSupport : IComponentData
    {
        public float DetectionMultiplier;
        public float DiscoveryRange;
    }

    public static class CreaturePerceptionRules
    {
        public const float MaximumFleeSpeed = 4f;
        public static CreatureReaction DefaultWildReaction(CreaturePersonality personality)
        {
            switch (personality)
            {
                case CreaturePersonality.Friendly: return CreatureReaction.Gather;
                case CreaturePersonality.Neutral: return CreatureReaction.Ignore;
                case CreaturePersonality.Wary: return CreatureReaction.Flee;
                case CreaturePersonality.Timid: return CreatureReaction.Flee;
                default: return CreatureReaction.Ignore;
            }
        }

        public static CreaturePerception FromPersonality(CreaturePersonality personality)
        {
            CreaturePersonalityProfile profile = CreatureTamingRules.Profile(personality);
            return new CreaturePerception
            {
                WildReaction = DefaultWildReaction(personality),
                TamedReaction = CreatureReaction.Gather,
                DetectionRadius = math.max(6f, profile.FleeDistance * 1.6f),
                PersonalSpace = math.max(1.5f, profile.FleeDistance * 0.6f),
                ApproachDistance = 2.5f,
                ReactionSpeedMultiplier = profile.FleeSpeedMultiplier
            };
        }

        /// <summary>Taming flips a fearful reaction into a friendly one; the creature no longer avoids the player.</summary>
        public static CreatureReaction Resolve(in CreaturePerception perception, bool isTamed, float distance)
        {
            float detection = perception.DetectionRadius + (perception.CurrentReaction != CreatureReaction.Ignore ? 2f : 0f);
            if (perception.DetectionRadius <= 0f || distance > detection)
                return CreatureReaction.Ignore;

            CreatureReaction reaction = isTamed ? perception.TamedReaction : perception.WildReaction;
            switch (reaction)
            {
                case CreatureReaction.Flee:
                    float safeDistance = perception.CurrentReaction == CreatureReaction.Flee
                        ? math.max(perception.PersonalSpace + 2f, perception.DetectionRadius * .85f) : perception.PersonalSpace;
                    return distance <= safeDistance ? CreatureReaction.Flee : CreatureReaction.Ignore;
                case CreatureReaction.Approach:
                case CreatureReaction.Gather:
                    return reaction;
                default:
                    return CreatureReaction.Ignore;
            }
        }

        public static float3 ReactionTarget(float3 position, float3 playerPosition, CreatureReaction reaction,
            in CreaturePerception perception, uint seed)
        {
            switch (reaction)
            {
                case CreatureReaction.Flee:
                {
                    float3 away = position - playerPosition;
                    float3 direction = math.normalizesafe(new float3(away.x, away.y * 0.35f, away.z),
                        new float3(0f, 0f, 1f));
                    return position + direction * math.max(1f, perception.DetectionRadius);
                }
                case CreatureReaction.Approach:
                {
                    float3 toward = position - playerPosition;
                    float3 direction = math.normalizesafe(toward, new float3(0f, 0f, 1f));
                    return playerPosition + direction * math.max(0.1f, perception.ApproachDistance);
                }
                case CreatureReaction.Gather:
                {
                    Random random = Random.CreateFromIndex(CreatureGradeRules.SanitizeSeed(seed));
                    float angle = random.NextFloat(0f, 2f * math.PI);
                    float radius = math.max(0.1f, perception.ApproachDistance) *
                                   random.NextFloat(0.75f, 1.35f);
                    float height = random.NextFloat(-0.6f, 0.6f) * radius;
                    return playerPosition +
                           new float3(math.cos(angle) * radius, height, math.sin(angle) * radius);
                }
                default:
                    return position;
            }
        }

        public static float SpeedMultiplier(in CreaturePerception perception, CreatureReaction reaction)
        {
            switch (reaction)
            {
                case CreatureReaction.Flee: return math.clamp(perception.ReactionSpeedMultiplier, 1f, 1.35f);
                case CreatureReaction.Approach:
                case CreatureReaction.Gather: return 1f;
                default: return 1f;
            }
        }
    }
}
