using System;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures
{
    [Serializable]
    public struct CreaturePaletteSwatch
    {
        public int PaletteId;
        public string DisplayName;
        public Color Color;
    }

    [CreateAssetMenu(menuName = "WorldBuilder/Creatures/Palette", fileName = "CreaturePalette")]
    public sealed class CreaturePaletteAsset : ScriptableObject
    {
        [SerializeField] private CreaturePaletteSwatch[] swatches = Array.Empty<CreaturePaletteSwatch>();

        public int Count => swatches?.Length ?? 0;

        public CreaturePaletteSwatch Get(int index) => swatches[index];

        public bool TryGet(int paletteId, out CreaturePaletteSwatch swatch)
        {
            CreaturePaletteSwatch[] entries = swatches ?? Array.Empty<CreaturePaletteSwatch>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].PaletteId != paletteId) continue;
                swatch = entries[i];
                return true;
            }
            swatch = default;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (swatches == null) return;
            for (int i = 0; i < swatches.Length; i++)
            {
                Color color = swatches[i].Color;
                color.a = 1f;
                swatches[i].Color = color;
            }
        }
#endif
    }
}
