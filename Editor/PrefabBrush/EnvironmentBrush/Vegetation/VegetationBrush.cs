using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Vegetation
{
    public sealed class VegetationBrush : IEnvironmentBrush
    {
        private readonly VegetationBrushSettings settings = new VegetationBrushSettings();

        public string DisplayName => "Vegetation";

        public VisualElement BuildUI()
        {
            VisualElement root = new VisualElement();

            EnvironmentUI.SliderInt(root, "Variant Count", 1, 12, settings.variantCount, v => settings.variantCount = v);
            EnvironmentUI.Int(root, "Seed", settings.seed, v => settings.seed = v);

            EnvironmentUI.Header(root, "Stem");
            EnvironmentUI.Slider(root, "Height", 0.2f, 8f, settings.height, v => settings.height = v);
            EnvironmentUI.SliderInt(root, "Segment Count", 2, 32, settings.segmentCount, v => settings.segmentCount = v);
            EnvironmentUI.Slider(root, "Curviness", 0f, 1f, settings.curviness, v => settings.curviness = v);
            EnvironmentUI.Slider(root, "Stem Width", 0.005f, 0.5f, settings.stemWidth, v => settings.stemWidth = v);
            EnvironmentUI.Slider(root, "Taper", 0f, 1f, settings.taper, v => settings.taper = v);
            EnvironmentUI.Slider(root, "Twist", 0f, 6.28f, settings.twist, v => settings.twist = v);
            EnvironmentUI.Vec2(root, "Lean Direction", settings.leanDirection, v => settings.leanDirection = v);
            EnvironmentUI.Slider(root, "Lean Strength", 0f, 2f, settings.leanStrength, v => settings.leanStrength = v);

            EnvironmentUI.Header(root, "Leaves");
            EnvironmentUI.SliderInt(root, "Leaf Count", 0, 200, settings.leafCount, v => settings.leafCount = v);
            EnvironmentUI.Slider(root, "Leaf Size", 0.02f, 2f, settings.leafSize, v => settings.leafSize = v);
            EnvironmentUI.Slider(root, "Leaf Spacing", 0.02f, 1f, settings.leafSpacing, v => settings.leafSpacing = v);
            EnvironmentUI.Vec2(root, "Leaf Angle Range", settings.leafAngleRange, v => settings.leafAngleRange = v);
            EnvironmentUI.Enum(root, "Leaf Shape", settings.leafShape, v => settings.leafShape = (LeafShape)v);
            EnvironmentUI.Slider(root, "Rib Strength", 0f, 1f, settings.ribStrength, v => settings.ribStrength = v);
            EnvironmentUI.Slider(root, "Alpha Gradient", 0f, 1f, settings.alphaGradient, v => settings.alphaGradient = v);

            EnvironmentUI.Header(root, "Color");
            EnvironmentUI.Color(root, "Gradient Root", settings.colorGradientRoot, v => settings.colorGradientRoot = v);
            EnvironmentUI.Color(root, "Gradient Tip", settings.colorGradientTip, v => settings.colorGradientTip = v);

            EnvironmentUI.Header(root, "Current");
            EnvironmentUI.Vec2(root, "Current Direction", settings.currentDirection, v => settings.currentDirection = v);
            EnvironmentUI.Slider(root, "Current Strength", 0f, 2f, settings.currentStrength, v => settings.currentStrength = v);

            EnvironmentUI.AccentButton(root, "Generate Variants", () =>
            {
                PrefabBrushSettings target = PrefabBrushSettingsLocator.LoadOrCreate();
                GenerateVariants(target);
            });

            return root;
        }

        public void GenerateVariants(PrefabBrushSettings target)
        {
            Material material = EnvironmentAssetWriter.GetOrCreateMaterial(EnvironmentType.Vegetation, Color.Lerp(settings.colorGradientRoot, settings.colorGradientTip, 0.5f));

            for (int i = 0; i < settings.variantCount; i++)
            {
                Mesh mesh = BuildMesh(settings.seed + i * 97);
                GameObject prefab = EnvironmentAssetWriter.CreateVariant(mesh, material, EnvironmentType.Vegetation, "Vegetation", i);
                EnvironmentAssetWriter.Register(target, prefab, EnvironmentType.Vegetation);
            }

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
        }

        private Mesh BuildMesh(int seed)
        {
            System.Random rng = new System.Random(seed);
            MeshAccumulator mesh = new MeshAccumulator();

            int rings = Mathf.Max(2, settings.segmentCount) + 1;
            Vector3 lean = new Vector3(settings.leanDirection.x, 0f, settings.leanDirection.y);
            if (lean.sqrMagnitude > 0.0001f)
            {
                lean.Normalize();
            }
            Vector3 current = new Vector3(settings.currentDirection.x, 0f, settings.currentDirection.y);
            if (current.sqrMagnitude > 0.0001f)
            {
                current.Normalize();
            }

            float phase = (float)rng.NextDouble() * Mathf.PI * 2f;
            float curvePhase = (float)rng.NextDouble() * Mathf.PI * 2f;

            List<Vector3> spine = new List<Vector3>(rings);
            List<float> radii = new List<float>(rings);
            List<float> twists = new List<float>(rings);

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1);
                float y = t * settings.height;

                Vector3 curveOffset = new Vector3(
                    Mathf.Sin(curvePhase + t * 3.1f) * settings.curviness,
                    0f,
                    Mathf.Cos(curvePhase + t * 2.3f) * settings.curviness) * (t * settings.height * 0.25f);

                Vector3 leanOffset = lean * (settings.leanStrength * t * t * settings.height * 0.3f);
                Vector3 currentOffset = current * (settings.currentStrength * t * t * settings.height * 0.25f);

                spine.Add(new Vector3(curveOffset.x + leanOffset.x + currentOffset.x, y, curveOffset.z + leanOffset.z + currentOffset.z));
                radii.Add(Mathf.Lerp(settings.stemWidth, settings.stemWidth * (1f - settings.taper), t));
                twists.Add(settings.twist * t + phase);
            }

            EnvironmentTube.Build(mesh, spine, radii, twists, 6, settings.colorGradientRoot, settings.colorGradientTip, 0f, 1f);

            AddLeaves(mesh, spine, rng, lean, current);

            return mesh.ToMesh("VegetationMesh");
        }

        private void AddLeaves(MeshAccumulator mesh, List<Vector3> spine, System.Random rng, Vector3 lean, Vector3 current)
        {
            if (settings.leafCount <= 0)
            {
                return;
            }

            float widthFactor = settings.leafShape == LeafShape.Wide ? 0.6f : 0.22f;

            for (int i = 0; i < settings.leafCount; i++)
            {
                float along = Mathf.Clamp01((float)i / Mathf.Max(1, settings.leafCount - 1));
                along = Mathf.Clamp01(along * (1f - settings.leafSpacing) + settings.leafSpacing * (float)rng.NextDouble());

                int segment = Mathf.Clamp(Mathf.RoundToInt(along * (spine.Count - 1)), 0, spine.Count - 1);
                Vector3 attach = spine[segment];

                float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                float pitch = Mathf.Lerp(settings.leafAngleRange.x, settings.leafAngleRange.y, (float)rng.NextDouble()) * Mathf.Deg2Rad;

                Vector3 outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                Vector3 dir = (outward * Mathf.Sin(pitch) + Vector3.up * Mathf.Cos(pitch)).normalized;
                dir += current * settings.currentStrength * 0.3f;
                dir.Normalize();

                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.0001f)
                {
                    side = outward;
                }

                float size = settings.leafSize * Mathf.Lerp(0.7f, 1.1f, (float)rng.NextDouble());
                float width = size * widthFactor;

                float t = along;
                Color baseColor = Color.Lerp(settings.colorGradientRoot, settings.colorGradientTip, t);

                Vector3 mid = attach + dir * size * 0.5f + Vector3.Cross(dir, side) * settings.ribStrength * size * 0.3f;
                Vector3 tip = attach + dir * size;

                Color rootColor = baseColor;
                rootColor.a = 1f;
                Color tipColor = baseColor;
                tipColor.a = Mathf.Lerp(1f, 1f - settings.alphaGradient, 1f);

                int b0 = mesh.Add(attach - side * width * 0.5f, new Vector2(0f, 0f), rootColor);
                int b1 = mesh.Add(attach + side * width * 0.5f, new Vector2(1f, 0f), rootColor);
                int m0 = mesh.Add(mid - side * width * 0.3f, new Vector2(0.1f, 0.5f), Color.Lerp(rootColor, tipColor, 0.5f));
                int m1 = mesh.Add(mid + side * width * 0.3f, new Vector2(0.9f, 0.5f), Color.Lerp(rootColor, tipColor, 0.5f));
                int tp = mesh.Add(tip, new Vector2(0.5f, 1f), tipColor);

                mesh.Quad(b0, m0, m1, b1);
                mesh.Triangle(m0, tp, m1);
                mesh.Quad(b1, m1, m0, b0);
                mesh.Triangle(m1, tp, m0);
            }
        }
    }
}
