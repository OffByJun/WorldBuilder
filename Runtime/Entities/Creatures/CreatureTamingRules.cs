using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public readonly struct CreatureTamingOutcome
    {
        public readonly bool Success;
        public readonly float Chance;
        public readonly bool Guaranteed;

        public CreatureTamingOutcome(bool success, float chance, bool guaranteed)
        {
            Success = success;
            Chance = chance;
            Guaranteed = guaranteed;
        }
    }

    public readonly struct CreaturePersonalityProfile
    {
        public readonly float BaseSuccessChance;
        public readonly float FailureBonus;
        public readonly float FleeDistance;
        public readonly float FleeSpeedMultiplier;
        public readonly float AlarmDurationSeconds;

        public CreaturePersonalityProfile(float baseSuccessChance, float failureBonus, float fleeDistance,
            float fleeSpeedMultiplier, float alarmDurationSeconds)
        {
            BaseSuccessChance = baseSuccessChance;
            FailureBonus = failureBonus;
            FleeDistance = fleeDistance;
            FleeSpeedMultiplier = fleeSpeedMultiplier;
            AlarmDurationSeconds = alarmDurationSeconds;
        }
    }

    public static class CreatureTamingRules
    {
        public const byte DefaultGuaranteedAfterAttempts = 4;

        public static CreaturePersonalityProfile Profile(CreaturePersonality personality)
        {
            switch (personality)
            {
                case CreaturePersonality.Friendly:
                    return new CreaturePersonalityProfile(0.65f, 0.20f, 2f, 1.15f, 1.5f);
                case CreaturePersonality.Neutral:
                    return new CreaturePersonalityProfile(0.45f, 0.22f, 5f, 1.4f, 3f);
                case CreaturePersonality.Wary:
                    return new CreaturePersonalityProfile(0.32f, 0.24f, 9f, 1.7f, 5f);
                case CreaturePersonality.Timid:
                    return new CreaturePersonalityProfile(0.25f, 0.26f, 14f, 2.1f, 7f);
                default:
                    return new CreaturePersonalityProfile(0.45f, 0.22f, 5f, 1.4f, 3f);
            }
        }

        public static CreatureTaming FromPersonality(CreaturePersonality personality, float preferredFoodBonus,
            byte guaranteedAfterAttempts)
        {
            CreaturePersonalityProfile profile = Profile(personality);
            return new CreatureTaming
            {
                BaseSuccessChance = profile.BaseSuccessChance,
                FailureBonus = profile.FailureBonus,
                PreferredFoodBonus = math.max(0f, preferredFoodBonus),
                GuaranteedAfterAttempts = guaranteedAfterAttempts == 0
                    ? DefaultGuaranteedAfterAttempts
                    : guaranteedAfterAttempts,
                AttemptCount = 0,
                IsTamed = 0
            };
        }

        public static CreatureAlarm FromPersonality(CreaturePersonality personality)
        {
            CreaturePersonalityProfile profile = Profile(personality);
            return new CreatureAlarm
            {
                FleeDistance = profile.FleeDistance,
                FleeSpeedMultiplier = profile.FleeSpeedMultiplier,
                AlarmDurationSeconds = profile.AlarmDurationSeconds,
                AlarmedUntil = 0d
            };
        }

        public static bool IsGuaranteed(in CreatureTaming taming)
        {
            byte cap = taming.GuaranteedAfterAttempts == 0
                ? DefaultGuaranteedAfterAttempts
                : taming.GuaranteedAfterAttempts;
            return taming.AttemptCount + 1 >= cap;
        }

        public static float SuccessChance(in CreatureTaming taming, bool preferredFood, float gradeMultiplier)
        {
            if (IsGuaranteed(taming)) return 1f;
            float chance = taming.BaseSuccessChance + taming.FailureBonus * taming.AttemptCount;
            if (preferredFood) chance += taming.PreferredFoodBonus;
            return math.saturate(chance * math.max(0f, gradeMultiplier));
        }

        public static CreatureTamingOutcome Attempt(in CreatureTaming taming, bool preferredFood,
            float gradeMultiplier, ref Random random)
        {
            bool guaranteed = IsGuaranteed(taming);
            float chance = SuccessChance(taming, preferredFood, gradeMultiplier);
            bool success = guaranteed || random.NextFloat() < chance;
            return new CreatureTamingOutcome(success, chance, guaranteed);
        }

        public static CreatureTaming ApplyOutcome(CreatureTaming taming, in CreatureTamingOutcome outcome)
        {
            if (outcome.Success)
            {
                taming.IsTamed = 1;
                return taming;
            }

            if (taming.AttemptCount < byte.MaxValue) taming.AttemptCount++;
            return taming;
        }

        public static CreatureAlarm Alarm(CreatureAlarm alarm, float3 source, double elapsedTime)
        {
            alarm.FleeFrom = source;
            alarm.AlarmedUntil = elapsedTime + math.max(0f, alarm.AlarmDurationSeconds);
            return alarm;
        }

        public static bool IsAlarmed(in CreatureAlarm alarm, double elapsedTime)
            => elapsedTime < alarm.AlarmedUntil;

        public static float3 FleeTarget(float3 position, in CreatureAlarm alarm)
        {
            float3 away = position - alarm.FleeFrom;
            float3 direction = math.normalizesafe(new float3(away.x, away.y * 0.35f, away.z),
                new float3(0f, 0f, 1f));
            return position + direction * math.max(0.1f, alarm.FleeDistance);
        }
    }
}
