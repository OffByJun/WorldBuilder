namespace WorldBuilder.Entities.Creatures
{
    public static class CreatureInteractionRules
    {
        public const int AnyItemId = -1;

        public static CreatureCaptureFailure EvaluateCapture(in Creature creature, in CreatureCapture capture,
            bool alreadyCaptured, bool isTamed, int toolItemId, byte toolTier)
        {
            if ((creature.Interactions & CreatureInteractionMask.Capture) == 0)
                return CreatureCaptureFailure.NotCapturable;
            if (creature.SizeClass == CreatureSizeClass.Large) return CreatureCaptureFailure.NotCapturable;
            if (isTamed) return CreatureCaptureFailure.Tamed;
            if (alreadyCaptured) return CreatureCaptureFailure.AlreadyCaptured;
            if (capture.ItemId < 0 || capture.BaseCount <= 0) return CreatureCaptureFailure.NotCapturable;
            if (capture.RequiredToolItemId != AnyItemId && capture.RequiredToolItemId != toolItemId)
                return CreatureCaptureFailure.RequiredToolMissing;
            if (toolTier < capture.MinimumToolTier) return CreatureCaptureFailure.ToolTierTooLow;
            return CreatureCaptureFailure.None;
        }
    }
}
