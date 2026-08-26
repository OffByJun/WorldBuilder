using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.Gameplay
{
    public struct PoiCandidate
    {
        public Vector3 Position;
        public float Score;
        public string Reason;
    }

    /// <summary>
    /// Suggests points-of-interest placements from terrain shape alone: cave mouths (air
    /// pockets near the surface), lakeshores, and hilltops — each scored so designers get a
    /// ranked shortlist instead of an empty map.
    /// </summary>
    public static class PoiCandidateAnalyzer
    {
        public static List<PoiCandidate> Analyze(VoxelWorldSampler sampler,
            HighResBiomeMap biomeMap, Bounds area, float seaLevel,
            float step = 16f, int maxCandidates = 32)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            var results = new List<PoiCandidate>();
            const float iso = SurfaceNetsMesher.IsoLevel;

            for (float x = area.min.x; x < area.max.x; x += step)
            for (float z = area.min.z; z < area.max.z; z += step)
            {
                // Find surface height of this column.
                bool found = false;
                float surfaceY = 0f;
                for (float y = area.max.y; y >= area.min.y; y -= step * 0.5f)
                {
                    if (sampler.Sample(x, y, z) >= iso)
                    {
                        surfaceY = y;
                        found = true;
                        break;
                    }
                }
                if (!found) continue;

                float above = sampler.Sample(x, surfaceY + 2f, z);
                if (above >= iso) continue; // covered → skip interiors for POIs

                // Hilltop: local high point vs ring samples.
                float ringMax = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    float angle = MathF.PI * 2f * i / 8f;
                    float nx = x + MathF.Cos(angle) * step;
                    float nz = z + MathF.Sin(angle) * step;
                    for (float y = area.max.y; y >= area.min.y; y -= step * 0.5f)
                    {
                        if (sampler.Sample(nx, y, nz) >= iso)
                        {
                            ringMax = Mathf.Max(ringMax, y);
                            break;
                        }
                    }
                }
                if (surfaceY > ringMax && surfaceY > seaLevel + 12f)
                    results.Add(new PoiCandidate
                    {
                        Position = new Vector3(x, surfaceY + 1f, z),
                        Score = surfaceY - ringMax + surfaceY * 0.01f,
                        Reason = "hilltop"
                    });

                // Lakeshore: land column with water in a neighbour column.
                bool shore = false;
                for (int i = 0; i < 4 && !shore; i++)
                {
                    float angle = MathF.PI * 0.5f * i;
                    float nx = x + MathF.Cos(angle) * step;
                    float nz = z + MathF.Sin(angle) * step;
                    for (float y = seaLevel - 6f; y < seaLevel; y += 1.5f)
                    {
                        if (sampler.Sample(nx, y, nz) < iso && y < seaLevel - 0.5f)
                        { shore = true; break; }
                    }
                }
                if (shore && surfaceY >= seaLevel - 2f && surfaceY <= seaLevel + 4f)
                    results.Add(new PoiCandidate
                    {
                        Position = new Vector3(x, surfaceY + 1f, z),
                        Score = 5f - Mathf.Abs(surfaceY - seaLevel),
                        Reason = "lakeshore"
                    });

                // Cave mouth: enclosed air just under the surface shell.
                EnclosureSample enclosure =
                    UndergroundProbe.Probe(sampler, new Vector3(x, surfaceY - step * 0.75f, z),
                        24f, 1f);
                if (enclosure.IsEnclosed && enclosure.CoverThickness is > 1f and < 12f)
                    results.Add(new PoiCandidate
                    {
                        Position = new Vector3(x, surfaceY - step * 0.5f, z),
                        Score = 8f - enclosure.CoverThickness,
                        Reason = "cave-mouth"
                    });
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (results.Count > maxCandidates) results.RemoveRange(maxCandidates, results.Count - maxCandidates);
            return results;
        }
    }
}
