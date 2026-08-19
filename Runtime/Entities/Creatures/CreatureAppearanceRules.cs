using Unity.Entities;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public static class CreatureAppearanceRules
    {
        public const int UnknownPaletteId = -1;

        public static bool TryResolvePalette(in DynamicBuffer<CreaturePaletteEntry> palette, int paletteId,
            out float4 color)
        {
            color = new float4(1f);
            if (paletteId == UnknownPaletteId) return false;
            for (int i = 0; i < palette.Length; i++)
            {
                if (palette[i].PaletteId != paletteId) continue;
                color = math.saturate(palette[i].Color);
                return true;
            }
            return false;
        }

        public static CreaturePatternMask ToMask(CreaturePatternKind pattern)
        {
            switch (pattern)
            {
                case CreaturePatternKind.Stripes: return CreaturePatternMask.Stripes;
                case CreaturePatternKind.Spots: return CreaturePatternMask.Spots;
                case CreaturePatternKind.TwoTone: return CreaturePatternMask.TwoTone;
                case CreaturePatternKind.Gradient: return CreaturePatternMask.Gradient;
                case CreaturePatternKind.Special: return CreaturePatternMask.Special;
                default: return CreaturePatternMask.None;
            }
        }

        public static bool Supports(CreaturePatternMask supported, CreaturePatternKind pattern)
            => pattern == CreaturePatternKind.None || (supported & ToMask(pattern)) != 0;

        public static CreatureAppearance WithSlot(CreatureAppearance appearance, CreatureColorSlot slot,
            float4 color)
        {
            switch (slot)
            {
                case CreatureColorSlot.Primary:
                    appearance.Primary = color;
                    break;
                case CreatureColorSlot.Secondary:
                    appearance.Secondary = color;
                    break;
                case CreatureColorSlot.Accent:
                    appearance.Accent = color;
                    break;
                case CreatureColorSlot.Pattern:
                    appearance.PatternColor = color;
                    break;
            }
            return appearance;
        }

        public static float4 ReadSlot(in CreatureAppearance appearance, CreatureColorSlot slot)
        {
            switch (slot)
            {
                case CreatureColorSlot.Primary: return appearance.Primary;
                case CreatureColorSlot.Secondary: return appearance.Secondary;
                case CreatureColorSlot.Accent: return appearance.Accent;
                default: return appearance.PatternColor;
            }
        }

        public static CreatureAppearance WithPattern(CreatureAppearance appearance, CreaturePatternKind pattern,
            float strength)
        {
            appearance.Pattern = pattern;
            appearance.PatternStrength = pattern == CreaturePatternKind.None
                ? 0f
                : math.saturate(strength <= 0f ? 1f : strength);
            return appearance;
        }

        public static float4 PatternParameters(in CreatureAppearance appearance)
            => new float4((float)appearance.Pattern, appearance.PatternStrength, 0f, 0f);
    }
}
