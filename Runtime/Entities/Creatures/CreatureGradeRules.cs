using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public static class CreatureGradeRules
    {
        public const int GradeCount = 3;

        public static CreatureGrade GradeAt(int index)
        {
            switch (index)
            {
                case 0: return CreatureGrade.Common;
                case 1: return CreatureGrade.Rare;
                default: return CreatureGrade.Legendary;
            }
        }

        public static int GradeIndex(CreatureGrade grade)
        {
            switch (grade)
            {
                case CreatureGrade.Common: return 0;
                case CreatureGrade.Rare: return 1;
                case CreatureGrade.Legendary: return 2;
                default: return 0;
            }
        }

        public static CreatureGradeMask ToMask(CreatureGrade grade) =>
            (CreatureGradeMask)(1 << GradeIndex(grade));

        public static bool Contains(CreatureGradeMask mask, CreatureGrade grade) => (mask & ToMask(grade)) != 0;

        public static CreatureGradeDefinition Fallback(CreatureGrade grade)
        {
            float tier = GradeIndex(grade) / (float)(GradeCount - 1);
            return new CreatureGradeDefinition
            {
                Grade = grade,
                SpeedMultiplier = 1f + 0.2f * tier,
                SizeMultiplier = 1f + 0.35f * tier,
                ValueMultiplier = 1f + 3f * tier,
                TameChanceMultiplier = 1f - 0.35f * tier,
                SpawnWeight = math.max(0.01f, 1f - 0.4f * GradeIndex(grade))
            };
        }

        public static CreatureGradeDefinition Resolve(in DynamicBuffer<CreatureGradeDefinition> definitions,
            CreatureGrade grade)
        {
            for (int i = 0; i < definitions.Length; i++)
                if (definitions[i].Grade == grade) return Normalize(definitions[i]);
            return Fallback(grade);
        }

        public static CreatureGradeDefinition Normalize(CreatureGradeDefinition definition)
        {
            definition.SpeedMultiplier = math.max(0.01f, definition.SpeedMultiplier);
            definition.SizeMultiplier = math.max(0.01f, definition.SizeMultiplier);
            definition.ValueMultiplier = math.max(0f, definition.ValueMultiplier);
            definition.TameChanceMultiplier = math.max(0f, definition.TameChanceMultiplier);
            definition.SpawnWeight = math.max(0f, definition.SpawnWeight);
            return definition;
        }

        public static CreatureGrade SelectGrade(in DynamicBuffer<CreatureGradeDefinition> definitions,
            CreatureGradeMask allowed, ref Random random)
        {
            if (allowed == CreatureGradeMask.None) allowed = CreatureGradeMask.All;

            float total = 0f;
            for (int i = 0; i < GradeCount; i++)
            {
                CreatureGrade grade = GradeAt(i);
                if (Contains(allowed, grade)) total += Resolve(definitions, grade).SpawnWeight;
            }
            if (total <= 0f) return LowestAllowed(allowed);

            float roll = random.NextFloat(0f, total);
            for (int i = 0; i < GradeCount; i++)
            {
                CreatureGrade grade = GradeAt(i);
                if (!Contains(allowed, grade)) continue;
                roll -= Resolve(definitions, grade).SpawnWeight;
                if (roll <= 0f) return grade;
            }
            return LowestAllowed(allowed);
        }

        public static CreatureGrade LowestAllowed(CreatureGradeMask allowed)
        {
            for (int i = 0; i < GradeCount; i++)
                if (Contains(allowed, GradeAt(i))) return GradeAt(i);
            return CreatureGrade.Common;
        }

        public static int RewardCount(int baseCount, float valueMultiplier)
            => math.max(1, (int)math.round(math.max(0, baseCount) * math.max(0f, valueMultiplier)));

        public static uint SanitizeSeed(uint seed) => seed == 0u ? 1u : seed;
    }
}
