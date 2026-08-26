using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Bakes minimap data layers straight from the voxel store: a depth gradient for water
    /// maps and an enclosed-cave overlay. Pure functions — the Minimap Baker tool and game
    /// code both call into these.
    /// </summary>
    public static class MinimapDepthBaker
    {
        /// <summary>
        /// Depth map over [origin .. origin+sizeMeters]: white = dry land, darker blue =
        /// deeper water below seaLevel.
        /// </summary>
        public static Color32[] BakeDepth(VoxelStoreAsset store, float chunkSize, Vector2 origin,
            int resolutionPx, float sizeMeters, float seaLevel)
        {
            var sampler = new VoxelWorldSampler(store, chunkSize);
            var pixels = new Color32[resolutionPx * resolutionPx];
            float step = sizeMeters / resolutionPx;

            for (int pz = 0; pz < resolutionPx; pz++)
            {
                for (int px = 0; px < resolutionPx; px++)
                {
                    float wx = origin.x + px * step;
                    float wz = origin.y + pz * step;
                    pixels[pz * resolutionPx + px] = SampleDepthPixel(sampler, seaLevel,
                        wx, wz, step);
                }
            }
            return pixels;
        }

        private static Color32 SampleDepthPixel(VoxelWorldSampler sampler, float seaLevel,
            float x, float z, float step)
        {
            const float iso = SurfaceNetsMesher.IsoLevel;

            // March down from well above sea level to find the terrain surface.
            float top = Mathf.Max(seaLevel, 40f) + 10f;
            float surfaceY = top;
            bool foundSurface = false;
            for (float y = top; y >= -80f; y -= 1f)
            {
                if (sampler.Sample(x, y, z) >= iso)
                {
                    // Refine within the meter band.
                    for (float fine = y + 1f; fine >= y - 1f; fine -= 0.25f)
                    {
                        if (sampler.Sample(x, fine, z) < iso) { surfaceY = fine + 0.125f; foundSurface = true; break; }
                    }
                    if (!foundSurface) { surfaceY = y; foundSurface = true; }
                    break;
                }
            }

            if (!foundSurface || surfaceY >= seaLevel)
                return new Color32(238, 232, 214, 255); // dry land

            float depth = Mathf.Min(seaLevel - surfaceY, 60f);
            byte shade = (byte)Mathf.Lerp(210f, 24f, depth / 60f);
            return new Color32((byte)(shade * 0.35f), (byte)(shade * 0.62f), shade, 255);
        }

        /// <summary>
        /// Cave overlay: red where the point is enclosed under rock cover at ground level
        /// (cave systems), transparent in open air. Composite over any base layer.
        /// </summary>
        public static Color32[] BakeCaveOverlay(VoxelStoreAsset store, float chunkSize,
            Vector2 origin, int resolutionPx, float sizeMeters, float maxCoverRay = 48f)
        {
            var sampler = new VoxelWorldSampler(store, chunkSize);
            var pixels = new Color32[resolutionPx * resolutionPx];
            float step = sizeMeters / resolutionPx;

            for (int pz = 0; pz < resolutionPx; pz++)
            {
                for (int px = 0; px < resolutionPx; px++)
                {
                    float wx = origin.x + px * step;
                    float wz = origin.y + pz * step;

                    // Probe from just below the local surface so entrances stay visible.
                    EnclosureSample enclosure =
                        UndergroundProbe.Probe(sampler, new Vector3(wx, GroundY(sampler, wx, wz), wz), maxCoverRay, 1f);
                    byte alpha = enclosure.IsEnclosed ? (byte)200 : (byte)0;
                    pixels[pz * resolutionPx + px] = new Color32(120, 40, 30, alpha);
                }
            }
            return pixels;
        }

        private static float GroundY(VoxelWorldSampler sampler, float x, float z)
        {
            // Start high enough for mountainous terrain; the march is cheap.
            for (float y = 120f; y >= -70f; y -= 1f)
            {
                if (sampler.Sample(x, y, z) >= SurfaceNetsMesher.IsoLevel) return y - 1.5f;
            }
            return 20f;
        }
    }
}
