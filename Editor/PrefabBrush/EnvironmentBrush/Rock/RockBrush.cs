using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Rock
{
    public sealed class RockBrush : IEnvironmentBrush
    {
        private const int FaceResolution = 14;

        private readonly RockBrushSettings settings = new RockBrushSettings();

        public string DisplayName => "Rock";

        public VisualElement BuildUI()
        {
            VisualElement root = new VisualElement();

            EnvironmentUI.SliderInt(root, "Variant Count", 1, 12, settings.variantCount, v => settings.variantCount = v);
            EnvironmentUI.Int(root, "Seed", settings.seed, v => settings.seed = v);

            EnvironmentUI.Header(root, "Base Shape");
            EnvironmentUI.Enum(root, "Base Shape", settings.baseShape, v => settings.baseShape = (RockBaseShape)v);

            EnvironmentUI.Header(root, "Deformation");
            EnvironmentUI.Slider(root, "Noise Frequency", 0.2f, 6f, settings.noiseFrequency, v => settings.noiseFrequency = v);
            EnvironmentUI.Slider(root, "Noise Strength", 0f, 1f, settings.noiseStrength, v => settings.noiseStrength = v);
            EnvironmentUI.SliderInt(root, "Noise Octaves", 1, 8, settings.noiseOctaves, v => settings.noiseOctaves = v);
            EnvironmentUI.Bool(root, "Layered Noise", settings.layeredNoise, v => settings.layeredNoise = v);

            EnvironmentUI.Header(root, "Shape");
            EnvironmentUI.Slider(root, "Squash X", 0.2f, 2f, settings.squashX, v => settings.squashX = v);
            EnvironmentUI.Slider(root, "Squash Y", 0.2f, 2f, settings.squashY, v => settings.squashY = v);
            EnvironmentUI.Slider(root, "Squash Z", 0.2f, 2f, settings.squashZ, v => settings.squashZ = v);
            EnvironmentUI.Slider(root, "Sharpness", 0f, 1f, settings.sharpness, v => settings.sharpness = v);
            EnvironmentUI.Vec2(root, "Erosion Direction (XZ)", new Vector2(settings.erosionDirection.x, settings.erosionDirection.z), v => settings.erosionDirection = new Vector3(v.x, settings.erosionDirection.y, v.y));
            EnvironmentUI.Slider(root, "Erosion Y", -1f, 1f, settings.erosionDirection.y, v => settings.erosionDirection = new Vector3(settings.erosionDirection.x, v, settings.erosionDirection.z));
            EnvironmentUI.Slider(root, "Erosion Strength", 0f, 1f, settings.erosionStrength, v => settings.erosionStrength = v);

            EnvironmentUI.Header(root, "Surface");
            EnvironmentUI.Slider(root, "Surface Roughness", 0f, 0.5f, settings.surfaceRoughness, v => settings.surfaceRoughness = v);
            EnvironmentUI.Slider(root, "Crack Depth", 0f, 0.5f, settings.crackDepth, v => settings.crackDepth = v);
            EnvironmentUI.Slider(root, "Sharp Edge Ratio", 0f, 1f, settings.sharpEdgeRatio, v => settings.sharpEdgeRatio = v);

            EnvironmentUI.Header(root, "Color Masks (UV2)");
            EnvironmentUI.Slider(root, "Moss Height Threshold", 0f, 1f, settings.mossHeightThreshold, v => settings.mossHeightThreshold = v);
            EnvironmentUI.Slider(root, "Sediment Height Threshold", 0f, 1f, settings.sedimentHeightThreshold, v => settings.sedimentHeightThreshold = v);

            EnvironmentUI.Header(root, "Size");
            EnvironmentUI.Slider(root, "Min Scale", 0.1f, 3f, settings.minScale, v => settings.minScale = v);
            EnvironmentUI.Slider(root, "Max Scale", 0.1f, 3f, settings.maxScale, v => settings.maxScale = v);
            EnvironmentUI.Slider(root, "Cluster Variance", 0f, 1f, settings.clusterVariance, v => settings.clusterVariance = v);

            EnvironmentUI.AccentButton(root, "Generate Variants", () =>
            {
                PrefabBrushSettings target = PrefabBrushSettingsLocator.LoadOrCreate();
                GenerateVariants(target);
            });

            return root;
        }

        public void GenerateVariants(PrefabBrushSettings target)
        {
            Material material = EnvironmentAssetWriter.GetOrCreateMaterial(EnvironmentType.Rock, new Color(0.45f, 0.44f, 0.42f));

            for (int i = 0; i < settings.variantCount; i++)
            {
                Mesh mesh = BuildMesh(settings.seed + i * 211);
                GameObject prefab = EnvironmentAssetWriter.CreateVariant(mesh, material, EnvironmentType.Rock, "Rock", i);
                EnvironmentAssetWriter.Register(target, prefab, EnvironmentType.Rock);
            }

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
        }

        private Mesh BuildMesh(int seed)
        {
            System.Random rng = new System.Random(seed);
            Vector3 noiseOffset = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f);

            List<Vector3> basePositions = new List<Vector3>();
            List<int> baseTriangles = new List<int>();
            BuildCubeSphere(basePositions, baseTriangles);

            Vector3[] displaced = new Vector3[basePositions.Count];
            Vector3 erosion = settings.erosionDirection.sqrMagnitude > 0.0001f ? settings.erosionDirection.normalized : Vector3.zero;

            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = 0; i < basePositions.Count; i++)
            {
                Vector3 dir = basePositions[i].normalized;
                Vector3 unit = ToBaseShape(basePositions[i], dir);

                float d = (EnvironmentNoise.Fbm(unit + noiseOffset, settings.noiseFrequency, settings.noiseOctaves, settings.layeredNoise) - 0.5f) * 2f;
                d = Mathf.Sign(d) * Mathf.Pow(Mathf.Abs(d), 1f + settings.sharpness * 1.5f);
                float disp = d * settings.noiseStrength;

                float rough = (EnvironmentNoise.Sample((unit + noiseOffset) * (settings.noiseFrequency * 4f)) - 0.5f) * 2f * settings.surfaceRoughness;

                float crackNoise = EnvironmentNoise.Sample((unit - noiseOffset) * (settings.noiseFrequency * 1.5f));
                float crack = crackNoise > 0.45f && crackNoise < 0.55f ? -settings.crackDepth : 0f;

                Vector3 pos = unit + dir * (disp + rough + crack);
                pos = new Vector3(pos.x * settings.squashX, pos.y * settings.squashY, pos.z * settings.squashZ);

                float erosionFactor = Mathf.Max(0f, Vector3.Dot(dir, erosion));
                pos -= erosion * (erosionFactor * settings.erosionStrength);

                displaced[i] = pos;
                minY = Mathf.Min(minY, pos.y);
                maxY = Mathf.Max(maxY, pos.y);
            }

            float scale = Mathf.Lerp(Mathf.Min(settings.minScale, settings.maxScale), Mathf.Max(settings.minScale, settings.maxScale), (float)rng.NextDouble());
            Vector3 clusterScale = new Vector3(
                scale * (1f + ((float)rng.NextDouble() * 2f - 1f) * settings.clusterVariance),
                scale * (1f + ((float)rng.NextDouble() * 2f - 1f) * settings.clusterVariance),
                scale * (1f + ((float)rng.NextDouble() * 2f - 1f) * settings.clusterVariance));

            float range = Mathf.Max(0.0001f, maxY - minY);

            MeshAccumulator mesh = new MeshAccumulator();
            Dictionary<int, int> sharedMap = new Dictionary<int, int>();

            Color rockColor = new Color(0.45f, 0.44f, 0.42f);

            for (int t = 0; t < baseTriangles.Count; t += 3)
            {
                int ia = baseTriangles[t];
                int ib = baseTriangles[t + 1];
                int ic = baseTriangles[t + 2];

                bool flat = rng.NextDouble() < settings.sharpEdgeRatio;

                int a = EmitVertex(mesh, sharedMap, ia, displaced, clusterScale, minY, range, rockColor, flat);
                int b = EmitVertex(mesh, sharedMap, ib, displaced, clusterScale, minY, range, rockColor, flat);
                int c = EmitVertex(mesh, sharedMap, ic, displaced, clusterScale, minY, range, rockColor, flat);

                mesh.Triangle(a, b, c);
            }

            return mesh.ToMesh("RockMesh");
        }

        private int EmitVertex(MeshAccumulator mesh, Dictionary<int, int> sharedMap, int baseIndex, Vector3[] displaced, Vector3 clusterScale, float minY, float range, Color color, bool flat)
        {
            Vector3 pos = Vector3.Scale(displaced[baseIndex], clusterScale);
            float heightT = Mathf.Clamp01((Vector3.Scale(displaced[baseIndex], clusterScale).y - minY * clusterScale.y) / (range * clusterScale.y));

            float moss = Mathf.Clamp01((heightT - settings.mossHeightThreshold) / Mathf.Max(0.01f, 1f - settings.mossHeightThreshold));
            float sediment = Mathf.Clamp01((settings.sedimentHeightThreshold - heightT) / Mathf.Max(0.01f, settings.sedimentHeightThreshold));
            Vector2 uv2 = new Vector2(moss, sediment);

            Color tinted = color;
            tinted = Color.Lerp(tinted, new Color(0.3f, 0.45f, 0.25f), moss * 0.6f);
            tinted = Color.Lerp(tinted, new Color(0.55f, 0.5f, 0.4f), sediment * 0.5f);

            Vector2 uv = new Vector2(pos.x * 0.5f + 0.5f, pos.z * 0.5f + 0.5f);

            if (flat)
            {
                return mesh.Add(pos, uv, uv2, tinted);
            }

            if (sharedMap.TryGetValue(baseIndex, out int existing))
            {
                return existing;
            }

            int index = mesh.Add(pos, uv, uv2, tinted);
            sharedMap[baseIndex] = index;
            return index;
        }

        private Vector3 ToBaseShape(Vector3 cubePoint, Vector3 sphereDir)
        {
            switch (settings.baseShape)
            {
                case RockBaseShape.Box:
                    return Vector3.Lerp(cubePoint, sphereDir, 0.25f);
                case RockBaseShape.Capsule:
                    Vector3 capsule = sphereDir;
                    capsule.y *= 1.7f;
                    return capsule;
                default:
                    return sphereDir;
            }
        }

        private static void BuildCubeSphere(List<Vector3> positions, List<int> triangles)
        {
            Vector3[] faceNormals =
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };

            int res = FaceResolution;

            for (int f = 0; f < faceNormals.Length; f++)
            {
                Vector3 normal = faceNormals[f];
                Vector3 axisA = new Vector3(normal.y, normal.z, normal.x);
                Vector3 axisB = Vector3.Cross(normal, axisA);
                int start = positions.Count;

                for (int y = 0; y <= res; y++)
                {
                    for (int x = 0; x <= res; x++)
                    {
                        Vector2 percent = new Vector2((float)x / res, (float)y / res);
                        Vector3 point = normal + axisA * (2f * percent.x - 1f) + axisB * (2f * percent.y - 1f);
                        positions.Add(point);
                    }
                }

                int stride = res + 1;
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        int i = start + y * stride + x;
                        triangles.Add(i);
                        triangles.Add(i + stride + 1);
                        triangles.Add(i + stride);
                        triangles.Add(i);
                        triangles.Add(i + 1);
                        triangles.Add(i + stride + 1);
                    }
                }
            }
        }
    }
}
