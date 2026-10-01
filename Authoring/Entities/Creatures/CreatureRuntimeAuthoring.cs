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
            SpeedMultiplier = definition.SpeedMultiplier,
            SizeMultiplier = definition.SizeMultiplier,
            ValueMultiplier = definition.ValueMultiplier,
            TameChanceMultiplier = definition.TameChanceMultiplier,
            SpawnWeight = definition.SpawnWeight
        };

        private static CreatureGradeDefinition ToDefinition(in CreatureGradeEntry entry) => new CreatureGradeDefinition
        {
            Grade = entry.Grade,
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
                AddBuffer<CreatureSettleRequest>(entity);
                AddBuffer<CreatureUnsettleRequest>(entity);
                AddBuffer<CreatureSettleResult>(entity);
                AddBuffer<CreatureWorkCompletedEvent>(entity);
                AddComponent(entity, new CreaturePlayerFocus());
                AddComponent(entity, new CreatureStorageIndex());
                AddBuffer<CreatureStorageIndexEntry>(entity);

                BakeGrades(authoring, entity);
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
