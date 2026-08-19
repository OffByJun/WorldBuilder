using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Coral
{
    public sealed class CoralBrush : IEnvironmentBrush
    {
        private readonly CoralBrushSettings settings = new CoralBrushSettings();

        public string DisplayName => "Coral";

        public VisualElement BuildUI()
        {
            VisualElement root = new VisualElement();

            EnvironmentUI.SliderInt(root, "Variant Count", 1, 12, settings.variantCount, v => settings.variantCount = v);
            EnvironmentUI.Int(root, "Seed", settings.seed, v => settings.seed = v);

            EnvironmentUI.Header(root, "Form");
            EnvironmentUI.Enum(root, "Coral Type", settings.coralType, v => settings.coralType = (CoralType)v);

            EnvironmentUI.Header(root, "Branching");
            EnvironmentUI.SliderInt(root, "Branch Count", 1, 8, settings.branchCount, v => settings.branchCount = v);
            EnvironmentUI.Vec2(root, "Branch Angle Range", settings.branchAngleRange, v => settings.branchAngleRange = v);
            EnvironmentUI.Slider(root, "Branch Length Decay", 0.3f, 0.95f, settings.branchLengthDecay, v => settings.branchLengthDecay = v);
            EnvironmentUI.SliderInt(root, "Max Branch Depth", 1, 7, settings.maxBranchDepth, v => settings.maxBranchDepth = v);
            EnvironmentUI.Slider(root, "Min Branch Distance", 0.02f, 1f, settings.minBranchDistance, v => settings.minBranchDistance = v);

            EnvironmentUI.Header(root, "Thickness");
            EnvironmentUI.Slider(root, "Root Thickness", 0.02f, 0.6f, settings.rootThickness, v => settings.rootThickness = v);
            EnvironmentUI.Slider(root, "Tip Thickness", 0.005f, 0.3f, settings.tipThickness, v => settings.tipThickness = v);

            EnvironmentUI.Header(root, "Surface");
            EnvironmentUI.Slider(root, "Knob Density", 0f, 1f, settings.knobDensity, v => settings.knobDensity = v);
            EnvironmentUI.Slider(root, "Knob Size", 0.005f, 0.2f, settings.knobSize, v => settings.knobSize = v);
            EnvironmentUI.Slider(root, "Polyp Density", 0f, 1f, settings.polypDensity, v => settings.polypDensity = v);

            EnvironmentUI.Header(root, "Color");
            EnvironmentUI.Color(root, "Base Color", settings.baseColor, v => settings.baseColor = v);
            EnvironmentUI.Color(root, "Tip Color", settings.tipColor, v => settings.tipColor = v);

            EnvironmentUI.Header(root, "Variation");
            EnvironmentUI.Slider(root, "Asymmetry Strength", 0f, 1f, settings.asymmetryStrength, v => settings.asymmetryStrength = v);
            EnvironmentUI.Int(root, "Asymmetry Seed", settings.asymmetrySeed, v => settings.asymmetrySeed = v);
            EnvironmentUI.Slider(root, "Global Tilt", 0f, 1f, settings.globalTilt, v => settings.globalTilt = v);

            EnvironmentUI.AccentButton(root, "Generate Variants", () =>
            {
                PrefabBrushSettings target = PrefabBrushSettingsLocator.LoadOrCreate();
                GenerateVariants(target);
            });

            return root;
        }

        public void GenerateVariants(PrefabBrushSettings target)
        {
            Material material = EnvironmentAssetWriter.GetOrCreateMaterial(EnvironmentType.Coral, Color.Lerp(settings.baseColor, settings.tipColor, 0.5f));

            for (int i = 0; i < settings.variantCount; i++)
            {
                Mesh mesh = BuildMesh(settings.seed + i * 131);
                GameObject prefab = EnvironmentAssetWriter.CreateVariant(mesh, material, EnvironmentType.Coral, "Coral", i);
                EnvironmentAssetWriter.Register(target, prefab, EnvironmentType.Coral);
            }

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
        }

        private Mesh BuildMesh(int seed)
        {
            System.Random rng = new System.Random(seed);
            System.Random asymRng = new System.Random(settings.asymmetrySeed);
            MeshAccumulator mesh = new MeshAccumulator();

            Vector3 tilt = new Vector3(
                ((float)asymRng.NextDouble() * 2f - 1f) * settings.globalTilt,
                1f,
                ((float)asymRng.NextDouble() * 2f - 1f) * settings.globalTilt).normalized;

            float trunkLength = settings.coralType == CoralType.Pillar ? 1.4f : 0.9f;

            switch (settings.coralType)
            {
                case CoralType.Fan:
                    BuildFan(mesh, rng, asymRng);
                    break;
                case CoralType.Pillar:
                    GrowBranch(mesh, rng, asymRng, Vector3.zero, tilt, trunkLength, settings.rootThickness, 0, 1);
                    break;
                default:
                    GrowBranch(mesh, rng, asymRng, Vector3.zero, tilt, trunkLength, settings.rootThickness, 0, settings.branchCount);
                    break;
            }

            return mesh.ToMesh("CoralMesh");
        }

        private void GrowBranch(MeshAccumulator mesh, System.Random rng, System.Random asymRng, Vector3 origin, Vector3 direction, float length, float thickness, int depth, int childCount)
        {
            int rings = Mathf.Clamp(Mathf.RoundToInt(length / Mathf.Max(0.05f, settings.minBranchDistance)) + 2, 3, 24);
            float depthT = settings.maxBranchDepth > 0 ? (float)depth / settings.maxBranchDepth : 0f;
            float tipThickness = Mathf.Lerp(thickness, settings.tipThickness, 0.6f);

            List<Vector3> spine = new List<Vector3>(rings);
            List<float> radii = new List<float>(rings);
            List<float> twists = new List<float>(rings);

            Vector3 dir = direction.normalized;
            Vector3 pos = origin;
            Vector3 bend = new Vector3(
                ((float)asymRng.NextDouble() * 2f - 1f),
                0f,
                ((float)asymRng.NextDouble() * 2f - 1f)).normalized * settings.asymmetryStrength;

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1);
                spine.Add(pos);
                radii.Add(Mathf.Lerp(thickness, tipThickness, t));
                twists.Add(0f);

                Vector3 step = (dir + bend * t).normalized * (length / (rings - 1));
                pos += step;
            }

            Color rootColor = Color.Lerp(settings.baseColor, settings.tipColor, depthT);
            Color tipColor = Color.Lerp(settings.baseColor, settings.tipColor, Mathf.Clamp01(depthT + 0.2f));
            EnvironmentTube.Build(mesh, spine, radii, twists, 6, rootColor, tipColor, depthT, Mathf.Clamp01(depthT + 0.25f));

            AddKnobs(mesh, spine, radii, rng, Color.Lerp(rootColor, tipColor, 0.5f));

            if (depth >= settings.maxBranchDepth)
            {
                return;
            }

            Vector3 branchOrigin = spine[spine.Count - 1];
            int children = settings.coralType == CoralType.Pillar ? 1 : childCount;
            float nextLength = length * settings.branchLengthDecay;

            for (int c = 0; c < children; c++)
            {
                float angle = Mathf.Lerp(settings.branchAngleRange.x, settings.branchAngleRange.y, (float)rng.NextDouble()) * Mathf.Deg2Rad;
                float yaw = (children > 1 ? (float)c / children * Mathf.PI * 2f : 0f) + (float)rng.NextDouble() * 0.6f;

                Vector3 reference = Vector3.Cross(dir, Vector3.up);
                if (reference.sqrMagnitude < 0.0001f)
                {
                    reference = Vector3.right;
                }
                reference.Normalize();

                Quaternion rot = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, dir) * Quaternion.AngleAxis(angle * Mathf.Rad2Deg, reference);
                Vector3 childDir = rot * dir;

                GrowBranch(mesh, rng, asymRng, branchOrigin, childDir, nextLength, tipThickness, depth + 1, settings.branchCount);
            }
        }

        private void BuildFan(MeshAccumulator mesh, System.Random rng, System.Random asymRng)
        {
            Vector3 planeNormal = new Vector3(1f, 0f, 0.1f).normalized;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(planeNormal, up).normalized;

            int ribs = Mathf.Max(2, settings.branchCount * 2 + 1);
            float spread = 70f;

            for (int i = 0; i < ribs; i++)
            {
                float t = ribs > 1 ? (float)i / (ribs - 1) : 0.5f;
                float angle = Mathf.Lerp(-spread, spread, t) * Mathf.Deg2Rad;
                Vector3 dir = (up * Mathf.Cos(angle) + side * Mathf.Sin(angle)).normalized;
                dir += planeNormal * settings.asymmetryStrength * ((float)asymRng.NextDouble() * 2f - 1f) * 0.2f;
                dir.Normalize();

                float length = 1f * Mathf.Lerp(0.6f, 1f, 1f - Mathf.Abs(t - 0.5f) * 2f);
                GrowBranch(mesh, rng, asymRng, Vector3.zero, dir, length, settings.rootThickness * 0.6f, settings.maxBranchDepth, 0);
            }
        }

        private void AddKnobs(MeshAccumulator mesh, List<Vector3> spine, List<float> radii, System.Random rng, Color color)
        {
            float density = settings.knobDensity + settings.polypDensity * 0.5f;
            if (density <= 0f || settings.knobSize <= 0f)
            {
                return;
            }

            for (int i = 1; i < spine.Count; i++)
            {
                if (rng.NextDouble() > density)
                {
                    continue;
                }

                Vector3 dir = (spine[i] - spine[i - 1]).normalized;
                Vector3 normal = Vector3.Cross(dir, Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.0001f)
                {
                    normal = Vector3.right;
                }

                float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 outward = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, dir) * normal;
                Vector3 center = spine[i] + outward * radii[i];
                float size = settings.knobSize * Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble());

                AddOctahedron(mesh, center, size, color);
            }
        }

        private void AddOctahedron(MeshAccumulator mesh, Vector3 center, float size, Color color)
        {
            int top = mesh.Add(center + Vector3.up * size, new Vector2(0.5f, 1f), color);
            int bottom = mesh.Add(center - Vector3.up * size, new Vector2(0.5f, 0f), color);
            int px = mesh.Add(center + Vector3.right * size, new Vector2(1f, 0.5f), color);
            int nx = mesh.Add(center - Vector3.right * size, new Vector2(0f, 0.5f), color);
            int pz = mesh.Add(center + Vector3.forward * size, new Vector2(0.75f, 0.5f), color);
            int nz = mesh.Add(center - Vector3.forward * size, new Vector2(0.25f, 0.5f), color);

            mesh.Triangle(top, pz, px);
            mesh.Triangle(top, nx, pz);
            mesh.Triangle(top, nz, nx);
            mesh.Triangle(top, px, nz);
            mesh.Triangle(bottom, px, pz);
            mesh.Triangle(bottom, pz, nx);
            mesh.Triangle(bottom, nx, nz);
            mesh.Triangle(bottom, nz, px);
        }
    }
}
