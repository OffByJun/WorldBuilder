using UnityEngine;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.Environment
{
    /// <summary>
    /// Bakes cave darkness into terrain vertex colors: vertices under rock cover are
    /// lerped toward a cool shadow tone the deeper their ceiling sits above them. Uses a
    /// short coarse upward march so it stays cheap inside the parallel mesh-bake pass.
    /// </summary>
    public static class CaveAmbientTint
    {
        /// <summary>Cover thickness at which vertices reach full shadow.</summary>
        public const float FullShadeCover = 10f;

        /// <summary>How strongly a fully covered vertex is pulled toward the shadow tone.</summary>
        public const float MaxBlend = 0.65f;

        public static readonly Color ShadowColor = new Color(0.055f, 0.062f, 0.09f, 1f);

        /// <summary>
        /// Returns <paramref name="biomeColor"/> darkened when solid cover exists within
        /// <paramref name="maxRay"/> meters overhead; open-sky vertices pass through.
        /// </summary>
        public static Color Shade(VoxelWorldSampler sampler, Vector3 position, Color biomeColor,
            float maxRay = 20f, float step = 2f)
        {
            if (sampler == null) return biomeColor;
            if (maxRay <= 0f || step <= 0f) return biomeColor;

            const float iso = SurfaceNetsMesher.IsoLevel;
            int steps = Mathf.CeilToInt(maxRay / step);

            for (int i = 1; i <= steps; i++)
            {
                float y = position.y + i * step;
                if (sampler.Sample(position.x, y, position.z) >= iso)
                {
                    float t = Mathf.Clamp01(i * step / FullShadeCover);
                    return Color.Lerp(biomeColor, ShadowColor, t * MaxBlend);
                }
            }
            return biomeColor;
        }

        /// <summary>Pure tint math without sampling — useful for tests and previews.</summary>
        public static Color Apply(Color biomeColor, float coverThickness, bool enclosed)
        {
            if (!enclosed) return biomeColor;
            float t = Mathf.Clamp01(coverThickness / FullShadeCover);
            return Color.Lerp(biomeColor, ShadowColor, t * MaxBlend);
        }
    }
}
