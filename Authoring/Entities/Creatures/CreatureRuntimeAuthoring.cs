using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    [Serializable]
    public struct CreatureGradeEntry
    {
        public CreatureGrade Grade;
        [Tooltip("Random hue range, 0..1.")] public Vector2 HueRange;
        [Tooltip("Random saturation range, 0..1.")] public Vector2 SaturationRange;
        [Tooltip("Random brightness range, 0..1.")] public Vector2 BrightnessRange;
        [Range(0f, 1f)] public float Alpha;
        [Tooltip("Saturation multiplier applied at spawn so wild creatures look washed out.")]
        [Range(0f, 1f)] public float SpawnSaturationScale;
        [Min(0.01f)] public float SpeedMultiplier;
        [Min(0.01f)] public float SizeMultiplier;
        [Min(0f)] public float ValueMultiplier;
        [Tooltip("Scales taming success chance. Higher grades are usually harder to tame.")]
        [Min(0f)] public float TameChanceMultiplier;
        [Min(0f)] public float SpawnWeight;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldBuilder.Entities.Authoring.WorldEntityRuntimeAuthoring))]
    [AddComponentMenu("WorldBuilder/Entities/Creature Runtime")]
    public sealed class CreatureRuntimeAuthoring : MonoBehaviour
    {
        [Tooltip("Leave empty to fall back to the built-in grade curve.")]
        [SerializeField] private CreatureGradeEntry[] grades = Array.Empty<CreatureGradeEntry>();
        [Tooltip("Palette every colour food resolves into. Required for recolouring.")]
        [SerializeField] private CreaturePaletteAsset palette;

        [ContextMenu("Reset To Default Grades")]
        private void ResetToDefaultGrades()
        {
            grades = new CreatureGradeEntry[CreatureGradeRules.GradeCount];
            for (int i = 0; i < grades.Length; i++)
                grades[i] = ToEntry(CreatureGradeRules.Fallback(CreatureGradeRules.GradeAt(i)));
        }

        private static CreatureGradeEntry ToEntry(in CreatureGradeDefinition definition) => new CreatureGradeEntry
        {
            Grade = definition.Grade,
            HueRange = new Vector2(definition.HsvMinimum.x, definition.HsvMaximum.x),
            SaturationRange = new Vector2(definition.HsvMinimum.y, definition.HsvMaximum.y),
            BrightnessRange = new Vector2(definition.HsvMinimum.z, definition.HsvMaximum.z),
            Alpha = definition.HsvMaximum.w,
            SpawnSaturationScale = definition.SpawnSaturationScale,
            SpeedMultiplier = definition.SpeedMultiplier,
            SizeMultiplier = definition.SizeMultiplier,
            ValueMultiplier = definition.ValueMultiplier,
            TameChanceMultiplier = definition.TameChanceMultiplier,
            SpawnWeight = definition.SpawnWeight
        };

        private static CreatureGradeDefinition ToDefinition(in CreatureGradeEntry entry) => new CreatureGradeDefinition
        {
            Grade = entry.Grade,
            HsvMinimum = new float4(entry.HueRange.x, entry.SaturationRange.x, entry.BrightnessRange.x, entry.Alpha),
            HsvMaximum = new float4(entry.HueRange.y, entry.SaturationRange.y, entry.BrightnessRange.y, entry.Alpha),
            SpawnSaturationScale = entry.SpawnSaturationScale,
            SpeedMultiplier = entry.SpeedMultiplier,
            SizeMultiplier = entry.SizeMultiplier,
            ValueMultiplier = entry.ValueMultiplier,
            TameChanceMultiplier = entry.TameChanceMultiplier,
            SpawnWeight = entry.SpawnWeight
        };

        private sealed class CreatureRuntimeBaker : Baker<CreatureRuntimeAuthoring>
        {
            public override void Bake(CreatureRuntimeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddBuffer<CreatureSpawnRequest>(entity);
                AddBuffer<CreatureDespawnRequest>(entity);
                AddBuffer<CreatureCaptureRequest>(entity);
                AddBuffer<CreatureCaptureResult>(entity);
                AddBuffer<CreatureFeedRequest>(entity);
                AddBuffer<CreatureFeedResult>(entity);
                AddBuffer<CreatureRecolorRequest>(entity);
                AddBuffer<CreaturePatternRequest>(entity);
                AddBuffer<CreatureRecolorResult>(entity);
                AddBuffer<CreatureSettleRequest>(entity);
                AddBuffer<CreatureUnsettleRequest>(entity);
                AddBuffer<CreatureSettleResult>(entity);
                AddBuffer<CreatureWorkCompletedEvent>(entity);
                AddComponent(entity, new CreaturePlayerFocus());
                AddComponent(entity, new CreatureStorageIndex());
                AddBuffer<CreatureStorageIndexEntry>(entity);

                BakePalette(authoring, entity);
                BakeGrades(authoring, entity);
            }

            private void BakePalette(CreatureRuntimeAuthoring authoring, Entity entity)
            {
                DynamicBuffer<CreaturePaletteEntry> buffer = AddBuffer<CreaturePaletteEntry>(entity);
                if (authoring.palette == null)
                {
                    Debug.LogWarning("Creature Runtime has no palette assigned; recolouring will fail.", authoring);
                    return;
                }

                DependsOn(authoring.palette);
                for (int i = 0; i < authoring.palette.Count; i++)
                {
                    CreaturePaletteSwatch swatch = authoring.palette.Get(i);
                    buffer.Add(new CreaturePaletteEntry
                    {
                        PaletteId = swatch.PaletteId,
                        Color = new float4(swatch.Color.r, swatch.Color.g, swatch.Color.b, 1f)
                    });
                }
            }

            private void BakeGrades(CreatureRuntimeAuthoring authoring, Entity entity)
            {
                DynamicBuffer<CreatureGradeDefinition> definitions = AddBuffer<CreatureGradeDefinition>(entity);
                CreatureGradeEntry[] entries = authoring.grades ?? Array.Empty<CreatureGradeEntry>();
                if (entries.Length == 0)
                {
                    for (int i = 0; i < CreatureGradeRules.GradeCount; i++)
                        definitions.Add(CreatureGradeRules.Fallback(CreatureGradeRules.GradeAt(i)));
                    return;
                }

                for (int i = 0; i < entries.Length; i++)
                    definitions.Add(CreatureGradeRules.Normalize(ToDefinition(entries[i])));
            }
        }
    }
}
