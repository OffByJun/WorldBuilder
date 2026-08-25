using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.MinimapBakerTool
{
    public sealed class MinimapBakerTool : IWorldBuilderTool
    {
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.35f);

        private static readonly Color[] BiomeColors =
        {
            new Color(0.05f, 0.25f, 0.55f), // Ocean
            new Color(0.90f, 0.82f, 0.50f), // Beach
            new Color(0.20f, 0.55f, 0.22f), // Forest
            new Color(0.55f, 0.52f, 0.48f), // Rocky
            new Color(0.02f, 0.10f, 0.30f)  // DeepSea
        };

        private readonly IChunkBiomeMap biomeMap;

        [SerializeField] private Vector2Int resolution = new Vector2Int(1024, 1024);
        [SerializeField] private float worldExtent = 256f;
        [SerializeField] private float captureHeight = 300f;
        [SerializeField] private float farPlane = 2000f;
        [SerializeField] private bool autoCenterOnScene = true;
        [SerializeField] private bool transparentBackground;
        [SerializeField] private LayerMask layerMask = ~0;

        [Header("Overlay Layers")]
        [SerializeField] private bool includeBiomeLayer = true;
        [SerializeField] private bool includeWaterLayer = true;
        [SerializeField] private bool includeGridLayer;
        [SerializeField] private float chunkSizeForOverlays = 128f;
        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private bool saveIndividualLayers;
        [SerializeField] private LayerMask terrainProbeMask = ~0;

        public MinimapBakerTool(IChunkBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.minimap");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.minimap"));

            Toggle autoCenter = new Toggle("Auto Center On Scene Pivot") { value = autoCenterOnScene };
            autoCenter.RegisterValueChangedCallback(evt => autoCenterOnScene = evt.newValue);
            root.Add(autoCenter);

            Vector3Field centerField = new Vector3Field("Center") { value = Center() };
            centerField.SetEnabled(!autoCenterOnScene);
            centerField.RegisterValueChangedCallback(evt =>
            {
                SceneView view = SceneView.lastActiveSceneView;
                if (view != null) view.pivot = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(centerField);

            FloatField extent = new FloatField("World Extent (XZ)") { value = worldExtent };
            extent.RegisterValueChangedCallback(evt => worldExtent = Mathf.Max(1f, evt.newValue));
            root.Add(extent);

            FloatField height = new FloatField("Capture Height") { value = captureHeight };
            height.RegisterValueChangedCallback(evt => captureHeight = Mathf.Max(1f, evt.newValue));
            root.Add(height);

            Vector2IntField res = new Vector2IntField("Resolution") { value = resolution };
            res.RegisterValueChangedCallback(evt =>
            {
                resolution = new Vector2Int(
                    Mathf.Clamp(evt.newValue.x, 64, 8192),
                    Mathf.Clamp(evt.newValue.y, 64, 8192));
            });
            root.Add(res);

            FloatField far = new FloatField("Far Plane") { value = farPlane };
            far.RegisterValueChangedCallback(evt => farPlane = Mathf.Max(10f, evt.newValue));
            root.Add(far);

            Toggle transparent = new Toggle("Transparent Background") { value = transparentBackground };
            transparent.RegisterValueChangedCallback(evt => transparentBackground = evt.newValue);
            root.Add(transparent);

            LayerMaskField mask = new LayerMaskField("Layer Mask", layerMask);
            mask.RegisterValueChangedCallback(evt => layerMask = evt.newValue);
            root.Add(mask);

            Foldout layers = new Foldout { text = "Overlay Layers", value = true };

            Toggle biomes = new Toggle("Biome Layer") { value = includeBiomeLayer };
            biomes.SetEnabled(biomeMap != null);
            biomes.RegisterValueChangedCallback(evt => includeBiomeLayer = evt.newValue);
            layers.Add(biomes);

            FloatField overlayChunk = new FloatField("Chunk Size (Overlays)") { value = chunkSizeForOverlays };
            overlayChunk.RegisterValueChangedCallback(evt => chunkSizeForOverlays = Mathf.Max(1f, evt.newValue));
            layers.Add(overlayChunk);

            Toggle water = new Toggle("Water Layer") { value = includeWaterLayer };
            water.RegisterValueChangedCallback(evt => includeWaterLayer = evt.newValue);
            layers.Add(water);

            ObjectField waterDataField = new ObjectField("Water Runtime Data")
            {
                objectType = typeof(WaterWorldRuntimeData),
                value = waterData
            };
            waterDataField.RegisterValueChangedCallback(evt => waterData = evt.newValue as WaterWorldRuntimeData);
            layers.Add(waterDataField);

            Toggle grid = new Toggle("Chunk Grid Layer") { value = includeGridLayer };
            grid.RegisterValueChangedCallback(evt => includeGridLayer = evt.newValue);
            layers.Add(grid);

            Toggle individual = new Toggle("Save Layers Separately") { value = saveIndividualLayers };
            individual.RegisterValueChangedCallback(evt => saveIndividualLayers = evt.newValue);
            layers.Add(individual);

            root.Add(layers);

            Button bake = new Button(Bake) { text = WorldBuilderLocalization.Get("btn.bakeMinimap") };
            bake.style.marginTop = 8f;
            root.Add(bake);

            return root;
        }

        public void OnSceneGUI()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Vector3 center = Center();
            float half = worldExtent * 0.5f;

            Handles.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Vector3[] corners =
            {
                center + new Vector3(-half, 0f, -half),
                center + new Vector3(half, 0f, -half),
                center + new Vector3(half, 0f, half),
                center + new Vector3(-half, 0f, half),
                center + new Vector3(-half, 0f, -half)
            };
            Handles.DrawPolyLine(corners);
            Handles.Label(center + new Vector3(0f, 0f, -half), "Minimap North");
        }

        private Vector3 Center()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return autoCenterOnScene && view != null ? view.pivot : Vector3.zero;
        }

        private void Bake()
        {
            string path = EditorUtility.SaveFilePanel(
                WorldBuilderLocalization.Get("btn.bakeMinimap"),
                Application.dataPath,
                "minimap",
                "png");
            if (string.IsNullOrEmpty(path)) return;

            int width = resolution.x;
            int height = resolution.y;
            Vector3 center = Center();

            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24);
            Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            GameObject cameraObject = new GameObject("WB_MinimapBakerCamera");

            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = center + Vector3.up * captureHeight;
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                camera.orthographic = true;
                camera.orthographicSize = worldExtent * 0.5f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = farPlane;
                camera.cullingMask = layerMask;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, transparentBackground ? 0f : 1f);
                camera.enabled = false;

                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                output.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                RenderTexture.active = previous;

                Color32[] basePixels = output.GetPixels32();
                Color32[] biomePixels = includeBiomeLayer && biomeMap != null
                    ? BuildBiomeLayer(width, height, center)
                    : null;
                Color32[] waterPixels = includeWaterLayer && waterData != null
                    ? BuildWaterLayer(width, height, center)
                    : null;
                Color32[] gridPixels = includeGridLayer
                    ? BuildGridLayer(width, height, center)
                    : null;

                Blend(basePixels, biomePixels);
                Blend(basePixels, waterPixels);
                Blend(basePixels, gridPixels);
                output.SetPixels32(basePixels);
                output.Apply();

                File.WriteAllBytes(path, output.EncodeToPNG());
                if (saveIndividualLayers) SaveIndividualLayers(path, width, height, biomePixels, waterPixels, gridPixels);
                AssetDatabase.Refresh();

                Debug.Log($"[WorldBuilder] Minimap baked to {path} ({width}x{height}, {worldExtent}m, " +
                          $"biome={biomePixels != null}, water={waterPixels != null}, grid={gridPixels != null}).");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                RenderTexture.ReleaseTemporary(renderTexture);
                Object.DestroyImmediate(output);
            }

            UndoHistory.Push(WorldBuilderLocalization.Get("undo.minimap"));
        }

        /// <summary>Pixel (0,0) is bottom-left; world +Z maps up, +X maps right.</summary>
        private Vector3 PixelToWorld(int x, int y, int width, int height, Vector3 center)
        {
            float u = x / (float)width - 0.5f;
            float v = y / (float)height - 0.5f;
            return center + new Vector3(u * worldExtent, 0f, v * worldExtent);
        }

        private Color32[] BuildBiomeLayer(int width, int height, Vector3 center)
        {
            Color32[] pixels = new Color32[width * height];
            var calculator = new ChunkCoordCalculator();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 world = PixelToWorld(x, y, width, height, center);
                    Vector3Int chunk = calculator.ToChunkCoord(world, Mathf.RoundToInt(chunkSizeForOverlays));
                    if (!biomeMap.TryGet(chunk, out BiomeType biome)) continue;
                    Color color = BiomeColors[Mathf.Clamp((int)biome, 0, BiomeColors.Length - 1)];
                    color.a = 0.45f;
                    pixels[y * width + x] = color;
                }
            }
            return pixels;
        }

        private Color32[] BuildWaterLayer(int width, int height, Vector3 center)
        {
            const int probeCells = 192;
            int cellWidth = Mathf.Max(1, width / probeCells);
            int cellHeight = Mathf.Max(1, height / probeCells);
            Color32[] pixels = new Color32[width * height];
            WaterQueryService service = new WaterQueryService(waterData);
            LayerMask mask = terrainProbeMask;

            for (int cy = 0; cy < probeCells; cy++)
            {
                for (int cx = 0; cx < probeCells; cx++)
                {
                    int px = cx * cellWidth + cellWidth / 2;
                    int py = cy * cellHeight + cellHeight / 2;
                    if (px >= width || py >= height) continue;

                    Vector3 world = PixelToWorld(px, py, width, height, center);
                    Ray ray = new Ray(world + Vector3.up * 1000f, Vector3.down);
                    if (!Physics.Raycast(ray, out RaycastHit hit, 4000f, mask)) continue;

                    WaterSample sample = service.Sample(hit.point + Vector3.down * 0.01f);
                    if (!sample.IsInWater) continue;

                    float t = Mathf.Clamp01(sample.Depth / 30f);
                    Color color = Color.Lerp(new Color(0.30f, 0.75f, 0.95f), new Color(0.02f, 0.12f, 0.42f), t);
                    color.a = 0.5f;

                    int startX = cx * cellWidth;
                    int startY = cy * cellHeight;
                    for (int y = startY; y < Mathf.Min(startY + cellHeight, height); y++)
                    for (int x = startX; x < Mathf.Min(startX + cellWidth, width); x++)
                        pixels[y * width + x] = color;
                }
            }
            return pixels;
        }

        private Color32[] BuildGridLayer(int width, int height, Vector3 center)
        {
            Color32[] pixels = new Color32[width * height];
            float pixelsPerMeter = width / worldExtent;
            float step = chunkSizeForOverlays * pixelsPerMeter;
            if (step < 2f) return pixels;

            byte r = (byte)(GridColor.r * 255f);
            byte g = (byte)(GridColor.g * 255f);
            byte b = (byte)(GridColor.b * 255f);
            byte a = (byte)(GridColor.a * 255f);

            for (float gx = 0f; gx <= width; gx += step)
            {
                int column = Mathf.RoundToInt(gx);
                if (column >= width) continue;
                for (int y = 0; y < height; y++) pixels[y * width + column] = new Color32(r, g, b, a);
            }
            for (float gy = 0f; gy <= height; gy += step)
            {
                int row = Mathf.RoundToInt(gy);
                if (row >= height) continue;
                for (int x = 0; x < width; x++) pixels[row * width + x] = new Color32(r, g, b, a);
            }
            return pixels;
        }

        private static void Blend(Color32[] target, Color32[] overlay)
        {
            if (overlay == null) return;
            for (int i = 0; i < target.Length; i++)
            {
                Color over = overlay[i];
                if (over.a <= 0.001f) continue;
                Color under = target[i];
                float a = over.a;
                Color blended = over * a + under * (1f - a);
                blended.a = Mathf.Max(under.a, a);
                target[i] = blended;
            }
        }

        private static void SaveIndividualLayers(string path, int width, int height,
            Color32[] biomePixels, Color32[] waterPixels, Color32[] gridPixels)
        {
            string directory = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            SaveLayer(Path.Combine(directory, name + "_biome.png"), width, height, biomePixels);
            SaveLayer(Path.Combine(directory, name + "_water.png"), width, height, waterPixels);
            SaveLayer(Path.Combine(directory, name + "_grid.png"), width, height, gridPixels);
        }

        private static void SaveLayer(string filePath, int width, int height, Color32[] pixels)
        {
            if (pixels == null) return;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(filePath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
