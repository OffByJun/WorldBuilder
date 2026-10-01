using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public readonly struct CreatureWorkTraits
    {
        public readonly float WorkSpeed;
        public readonly int CarryCapacity;

        public CreatureWorkTraits(float workSpeed, int carryCapacity)
        {
            WorkSpeed = workSpeed;
            CarryCapacity = carryCapacity;
        }
    }

    public static class CreatureSettlementRules
    {
        public static CreatureSettleFailure Evaluate(in Creature creature, bool isTamed, bool alreadySettled, in CreatureEnvironmentNeeds needs, in CreatureHabitat habitat, int memberCount)
        {
            if (alreadySettled) return CreatureSettleFailure.AlreadySettled;
            if (!isTamed) return CreatureSettleFailure.NotTamed;
            if (creature.SizeClass == CreatureSizeClass.Large) return CreatureSettleFailure.SizeClassNotAllowed;
            if (creature.SizeClass == CreatureSizeClass.Medium && habitat.AllowMedium == 0)
                return CreatureSettleFailure.SizeClassNotAllowed;
            if (habitat.Capacity > 0 && memberCount >= habitat.Capacity) return CreatureSettleFailure.HabitatFull;
            if ((needs.Required & habitat.Provided) != needs.Required)
                return CreatureSettleFailure.EnvironmentMissing;
            return CreatureSettleFailure.None;
        }

        /// <summary>
        /// The species decides which roles are available and how fast and how much it works; a habitat that
        /// provides the creature's preferred environment speeds it up.
        /// </summary>
        public static CreatureWorkTraits Traits(in CreatureRoleAptitude aptitude, in CreatureEnvironmentNeeds needs,
            in CreatureHabitat habitat)
        {
            float speed = math.max(0.1f, aptitude.BaseWorkSpeed);
            int carry = math.max(1, aptitude.BaseCarryCapacity);

            bool preferredMet = needs.Preferred == CreatureEnvironmentMask.None ||
                                (needs.Preferred & habitat.Provided) == needs.Preferred;
            if (preferredMet) speed *= 1.2f;

            return new CreatureWorkTraits(speed, carry);
        }

        public static float3 LivingPosition(float3 habitatCenter, in CreatureHabitat habitat, int memberIndex,
            ref Random random)
        {
            float3 extents = habitat.HalfExtents * 0.75f;
            float3 offset = random.NextFloat3(-extents, extents);
            return habitatCenter + offset;
        }

        public static bool Contains(in CreatureHabitat habitat, float3 habitatCenter, float3 position)
        {
            float3 delta = math.abs(position - habitatCenter);
            return math.all(delta <= habitat.HalfExtents);
        }

    }
}
