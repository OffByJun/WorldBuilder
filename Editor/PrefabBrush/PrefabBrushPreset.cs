using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    /// <summary>
    /// Reusable brush configuration: prefab set, placement options, mask and modifier graph.
    /// Save the current brush as a preset to swap environments (forest floor, reef, ruins...).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldBuilder/Prefab Brush Preset", fileName = "PrefabBrushPreset")]
    public sealed class PrefabBrushPreset : ScriptableObject
    {
        public float brushRadius = 3f;
        public int brushDensity = 10;
        public bool paintOnDrag = true;
        public float dragSpacing = 1f;
        public bool alignToNormal = true;
        public bool randomYaw = true;
        public Vector2 scaleRange = new Vector2(1f, 1f);
        public float chunkSize = 16f;

        public List<PrefabEntry> prefabEntries = new List<PrefabEntry>();
        public BrushMask mask = new BrushMask();
        public ModifierGraph modifierGraph;

        public void CaptureFrom(PrefabBrushSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            brushRadius = settings.brushRadius;
            brushDensity = settings.brushDensity;
            paintOnDrag = settings.paintOnDrag;
            dragSpacing = settings.dragSpacing;
            alignToNormal = settings.alignToNormal;
            randomYaw = settings.randomYaw;
            scaleRange = settings.scaleRange;
            chunkSize = settings.chunkSize;
            prefabEntries = new List<PrefabEntry>(settings.prefabEntries);
            mask = Clone(settings.mask);
            modifierGraph = settings.modifierGraph;
        }

        public void ApplyTo(PrefabBrushSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.brushRadius = brushRadius;
            settings.brushDensity = brushDensity;
            settings.paintOnDrag = paintOnDrag;
            settings.dragSpacing = dragSpacing;
            settings.alignToNormal = alignToNormal;
            settings.randomYaw = randomYaw;
            settings.scaleRange = scaleRange;
            settings.chunkSize = chunkSize;
            settings.prefabEntries = new List<PrefabEntry>(prefabEntries);
            settings.mask = Clone(mask);
            settings.modifierGraph = modifierGraph;
        }

        private static BrushMask Clone(BrushMask source)
        {
            return new BrushMask
            {
                useHeightMask = source.useHeightMask,
                minHeight = source.minHeight,
                maxHeight = source.maxHeight,
                useSlopeMask = source.useSlopeMask,
                maxSlopeAngle = source.maxSlopeAngle,
                useBiomeMask = source.useBiomeMask,
                allowedBiome = source.allowedBiome
            };
        }
    }
}
