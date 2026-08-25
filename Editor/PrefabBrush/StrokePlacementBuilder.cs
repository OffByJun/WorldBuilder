using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.PrefabBrush
{
    /// <summary>
    /// Deterministic placement math shared by the interactive brush and the
    /// scatter bake pipeline. Same stroke seed always yields the same placements.
    /// </summary>
    public static class StrokePlacementBuilder
    {
        public static List<BrushPlacement> Build(PrefabBrushSettings settings, BrushStroke stroke,
            IBiomeMap biomeMap, ChunkCoordCalculator calculator)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            List<BrushPlacement> placements = new List<BrushPlacement>();
            List<PrefabEntry> valid = CollectValidEntries(settings);
            if (valid.Count == 0) return placements;

            float totalWeight = 0f;
            for (int i = 0; i < valid.Count; i++) totalWeight += valid[i].weight;

            System.Random rng = new System.Random(stroke.seed);

            for (int i = 0; i < stroke.density; i++)
            {
                GameObject prefab = PickWeighted(valid, totalWeight, rng);

                double angle = rng.NextDouble() * Math.PI * 2.0;
                double distance = Math.Sqrt(rng.NextDouble()) * stroke.radius;
                Vector3 position = stroke.center +
                    new Vector3((float)(Math.Cos(angle) * distance), 0f, (float)(Math.Sin(angle) * distance));

                Vector3 normal = Vector3.up;
                if (SceneRaycaster.TryRaycastDown(position, out RaycastHit down))
                {
                    position = down.point;
                    normal = down.normal;
                }

                float yaw = (float)(rng.NextDouble() * 360.0);
                float scale = Mathf.Lerp(settings.scaleRange.x, settings.scaleRange.y, (float)rng.NextDouble());

                WorldBuilder.Runtime.Data.BiomeType biome = biomeMap != null
                    ? biomeMap.GetBiome(calculator.ToChunkCoord(position, settings.chunkSize))
                    : WorldBuilder.Runtime.Data.BiomeType.Forest;
                if (!PassesMask(settings.mask, position, normal, biome)) continue;

                Quaternion rotation = settings.alignToNormal
                    ? Quaternion.FromToRotation(Vector3.up, normal)
                    : Quaternion.identity;
                if (settings.randomYaw) rotation *= Quaternion.AngleAxis(yaw, Vector3.up);

                BrushContext context = new BrushContext
                {
                    position = position,
                    normal = normal,
                    rotation = rotation,
                    scale = Vector3.one * scale
                };

                ModifierContext modifierContext = new ModifierContext
                {
                    worldPosition = context.position,
                    brushCenter = stroke.center,
                    brushRadius = stroke.radius,
                    surfaceNormal = context.normal,
                    biome = biome,
                    seed = stroke.seed
                };

                WaterQueryService waterService = BuildWaterService(settings);
                if (waterService != null)
                {
                    WaterSample sample = waterService.Sample(context.position);
                    modifierContext.inWater = sample.IsInWater;
                    modifierContext.waterDepth = sample.Depth;
                }

                context.position += ModifierGraphEvaluator.EvaluatePositionOffset(settings.modifierGraph, modifierContext);
                context.rotation *= Quaternion.Euler(ModifierGraphEvaluator.EvaluateRotation(settings.modifierGraph, modifierContext));
                context.scale = Vector3.Scale(context.scale, ModifierGraphEvaluator.EvaluateScale(settings.modifierGraph, modifierContext));

                if (context.scale.sqrMagnitude < 1e-8f) continue;

                placements.Add(new BrushPlacement
                {
                    prefab = prefab,
                    position = context.position,
                    rotation = context.rotation,
                    scale = context.scale
                });
            }

            return placements;
        }

        private static WaterQueryService waterService;
        private static WaterWorldRuntimeData waterCache;

        private static WaterQueryService BuildWaterService(PrefabBrushSettings settings)
        {
            if (settings.waterData == null) return null;
            if (waterService == null || !ReferenceEquals(waterCache, settings.waterData))
            {
                waterCache = settings.waterData;
                waterService = new WaterQueryService(settings.waterData);
            }
            return waterService;
        }

        private static bool PassesMask(BrushMask mask, Vector3 position, Vector3 normal,
            WorldBuilder.Runtime.Data.BiomeType biome)
        {
            if (mask == null) return true;
            if (mask.useHeightMask && (position.y < mask.minHeight || position.y > mask.maxHeight)) return false;
            if (mask.useSlopeMask && Vector3.Angle(normal, Vector3.up) > mask.maxSlopeAngle) return false;
            if (mask.useBiomeMask && biome != mask.allowedBiome) return false;
            return true;
        }

        private static List<PrefabEntry> CollectValidEntries(PrefabBrushSettings settings)
        {
            List<PrefabEntry> valid = new List<PrefabEntry>();
            for (int i = 0; i < settings.prefabEntries.Count; i++)
            {
                PrefabEntry entry = settings.prefabEntries[i];
                if (entry.prefab != null && entry.weight > 0f && entry.envType == EnvironmentType.None)
                    valid.Add(entry);
            }
            return valid;
        }

        private static GameObject PickWeighted(List<PrefabEntry> valid, float totalWeight, System.Random rng)
        {
            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0.0;
            for (int i = 0; i < valid.Count; i++)
            {
                cumulative += valid[i].weight;
                if (roll <= cumulative) return valid[i].prefab;
            }
            return valid[valid.Count - 1].prefab;
        }
    }
}
