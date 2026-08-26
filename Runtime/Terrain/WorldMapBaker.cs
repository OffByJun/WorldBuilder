using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    /// <summary>
    /// Composes a single strategic world-map texture: water-depth shading under the sea,
    /// height-tinted land above it, cave overlay on top. Pure functions over the voxel
    /// store — the editor menu and runtime minimaps both feed off this.
    /// </summary>
    public static class WorldMapBaker
    {
        public static Color32[] BakeOverview(VoxelStoreAsset store, float chunkSize,
            HighResBiomeMap biomeMap, Vector2 originXz, int resolutionPx, float sizeMeters,
            float seaLevel, bool includeCaveOverlay = true)
        {
            Color32[] depth = MinimapDepthBaker.BakeDepth(store, chunkSize, originXz,
                resolutionPx, sizeMeters, seaLevel);

            Color32[] caves = includeCaveOverlay
                ? MinimapDepthBaker.BakeCaveOverlay(store, chunkSize, originXz,
                    resolutionPx, sizeMeters)
                : null;

            var sampler = new VoxelWorldSampler(store, chunkSize);
            float step = sizeMeters / resolutionPx;

            var output = new Color32[resolutionPx * resolutionPx];
            for (int i = 0; i < output.Length; i++)
            {
                int px = i % resolutionPx;
                int pz = i / resolutionPx;
                Color32 pixel = depth[i];

                // Land pixels take a subtle biome tint when a map exists.
                bool isLand = pixel.r > 200 && pixel.g > 200 && pixel.b > 180;
                if (isLand && biomeMap != null)
                {
                    float wx = originXz.x + px * step;
                    float wz = originXz.y + pz * step;
                    Color tint = biomeMap.SampleColor(wx, wz, chunkSize);
                    pixel = new Color32(
                        (byte)(pixel.r * 0.55f + tint.r * 255f * 0.45f),
                        (byte)(pixel.g * 0.55f + tint.g * 255f * 0.45f),
                        (byte)(pixel.b * 0.55f + tint.b * 255f * 0.45f), 255);
                }

                if (caves != null && caves[i].a > 0)
                    pixel = Color32.Lerp(pixel, caves[i], caves[i].a / 255f * 0.8f);

                output[i] = pixel;
            }
            return output;
        }

        public static Texture2D BakeOverviewTexture(VoxelStoreAsset store, float chunkSize,
            HighResBiomeMap biomeMap, Vector2 originXz, int resolutionPx, float sizeMeters,
            float seaLevel, bool includeCaveOverlay = true)
        {
            Color32[] pixels = BakeOverview(store, chunkSize, biomeMap, originXz,
                resolutionPx, sizeMeters, seaLevel, includeCaveOverlay);
            var texture = new Texture2D(resolutionPx, resolutionPx, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
