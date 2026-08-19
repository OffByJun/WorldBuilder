using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public readonly struct CreatureRecord
    {
        public readonly Unity.Entities.Entity Entity;
        public readonly WorldEntityIdentity Identity;
        public readonly int PrefabId;
        public readonly string DisplayName;
        public readonly CreatureGrade Grade;
        public readonly CreatureSizeClass SizeClass;
        public readonly CreaturePersonality Personality;
        public readonly CreatureAppearance Appearance;
        public readonly float3 Position;
        public readonly int2 Region;
        public readonly float Affinity;
        public readonly float MaximumAffinity;
        public readonly byte TameAttempts;
        public readonly bool IsTamed;
        public readonly bool IsActive;

        public CreatureRecord(Unity.Entities.Entity entity, WorldEntityIdentity identity, int prefabId,
            string displayName, CreatureGrade grade, CreatureSizeClass sizeClass, CreaturePersonality personality,
            in CreatureAppearance appearance, float3 position, int2 region, float affinity, float maximumAffinity,
            byte tameAttempts, bool isTamed, bool isActive)
        {
            Entity = entity;
            Identity = identity;
            PrefabId = prefabId;
            DisplayName = displayName ?? string.Empty;
            Grade = grade;
            SizeClass = sizeClass;
            Personality = personality;
            Appearance = appearance;
            Position = position;
            Region = region;
            Affinity = affinity;
            MaximumAffinity = maximumAffinity;
            TameAttempts = tameAttempts;
            IsTamed = isTamed;
            IsActive = isActive;
        }
    }

    public readonly struct CreatureFilter
    {
        public const int AnyPrefab = -1;

        public readonly CreatureGradeMask Grades;
        public readonly int PrefabId;
        public readonly bool ActiveOnly;
        public readonly bool TamedOnly;

        public CreatureFilter(CreatureGradeMask grades, int prefabId = AnyPrefab, bool activeOnly = false,
            bool tamedOnly = false)
        {
            Grades = grades == CreatureGradeMask.None ? CreatureGradeMask.All : grades;
            PrefabId = prefabId;
            ActiveOnly = activeOnly;
            TamedOnly = tamedOnly;
        }

        public static CreatureFilter All => new CreatureFilter(CreatureGradeMask.All);
        public static CreatureFilter Active => new CreatureFilter(CreatureGradeMask.All, AnyPrefab, true);
        public static CreatureFilter Tamed => new CreatureFilter(CreatureGradeMask.All, AnyPrefab, false, true);
        public static CreatureFilter OfGrade(CreatureGrade grade) =>
            new CreatureFilter(CreatureGradeRules.ToMask(grade));
        public static CreatureFilter OfPrefab(int prefabId) =>
            new CreatureFilter(CreatureGradeMask.All, prefabId);

        public bool Matches(in CreatureRecord record)
        {
            if (ActiveOnly && !record.IsActive) return false;
            if (TamedOnly && !record.IsTamed) return false;
            if (PrefabId != AnyPrefab && PrefabId != record.PrefabId) return false;
            return CreatureGradeRules.Contains(Grades, record.Grade);
        }
    }
}
