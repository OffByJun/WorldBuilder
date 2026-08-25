using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Editor.LODGeneratorTool;
using WorldBuilder.Editor.PrefabBrush;
using WorldBuilder.Editor.ScatterBakeTool;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Environment;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;
using Debug = UnityEngine.Debug;

namespace WorldBuilder.Editor.TerrainForgeTool
{
    /// <summary>
    /// The terrain core workbench: procedural generation, erosion, parallel mesh baking
    /// (with vertex-biome colors), high-res biome painting and rule-based ecology — all
    /// writing into the shared VoxelStoreAsset so sculpting, export and runtime
    /// deformation stay in sync.
    /// </summary>
    public sealed class TerrainForgeTool : IWorldBuilderTool
    {
        private readonly IChunkBiomeMap chunkBiomeMap;

        [SerializeField] private TerrainShapeParams shapeParams;
        [SerializeField] private float radius = 256f;
        [SerializeField] private int erosionDroplets = 20000;
        [SerializeField] private int erosionSeed = 42;
        [SerializeField] private bool bakeMeshes = true;
        [SerializeField] private HighResBiomeMap biomeMap;
        [SerializeField] private bool paintVertexBiomes = true;
        [SerializeField] private bool assembleSceneObjects = true;
        [SerializeField] private bool addColliders = true;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private ScatterRuleSet ruleSet;
        [SerializeField] private int ecologySeed = 7;
        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private string outputFolder = "Assets/WorldBuilderGenerated/Terrain";
        [SerializeField] private bool showCrossSection = true;
        [SerializeField] private CaveShapeParams caveParams;
        [SerializeField] private bool carveCavesDuringGenerate = true;
        [SerializeField] private bool darkenCaveVertices = true;

        // v0.8.0 — splatmaps, LOD chains, erosion maps.
        [SerializeField] private Texture2D splat0;
        [SerializeField] private Texture2D splat1;
        [SerializeField] private Texture2D splat2;
        [SerializeField] private Texture2D splat3;
        [SerializeField] private bool generateLodChain = true;
        [SerializeField] private float lod1Ratio = 0.5f;
        [SerializeField] private float lod2Ratio = 0.22f;
        [SerializeField] private bool exportErosionMap = true;
        [SerializeField] private SplatBaker.LayerMapping[] layerMapping =
            SplatBaker.DefaultMapping();

        private float[] lastErosionMap;
        private Vector2 lastErosionOrigin;
        private int lastErosionSize;
        private float lastErosionCellSize;

        private Label status;
        private VoxelStoreAsset store => VoxelStoreLocator.LoadOrCreate();

        public TerrainForgeTool(IChunkBiomeMap chunkBiomeMap)
        {
            this.chunkBiomeMap = chunkBiomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.terrainForge");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(InspectorHelp.Build(ToolName, "help.terrainForge"));

            // ---- Shape ----
            var shapeFoldout = new Foldout { text = "① Shape", value = true };
            ObjectField shapeField = new ObjectField("Shape Params")
            {
                objectType = typeof(TerrainShapeParams),
                value = shapeParams
            };
            shapeField.RegisterValueChangedCallback(evt => shapeParams = evt.newValue as TerrainShapeParams);
            shapeFoldout.Add(shapeField);

            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            foreach (TerrainPreset preset in Enum.GetValues(typeof(TerrainPreset)))
            {
                var presetButton = new Button(() => ApplyPreset(preset)) { text = preset.ToString() };
                presetButton.style.flexGrow = 1;
                presetRow.Add(presetButton);
            }
            shapeFoldout.Add(presetRow);

            shapeFoldout.Add(new Button(CreateShapeAsset)
            {
                text = WorldBuilderLocalization.Get("btn.createShapeAsset")
            });

            Slider radiusField = new Slider("Radius (m)", 32f, 2048f) { value = radius };
            radiusField.RegisterValueChangedCallback(evt =>
            {
                radius = evt.newValue;
                cachedSectionOrigin = null;
            });
            shapeFoldout.Add(radiusField);

            Button generateButton = new Button(Generate) { text = WorldBuilderLocalization.Get("btn.generateTerrain") };
            generateButton.style.marginTop = 4f;
            shapeFoldout.Add(generateButton);

            // ---- Erode (runs inside Generate) ----
            var erodeFoldout = new Foldout { text = "② Erode (during Generate)", value = false };
            IntegerField droplets = new IntegerField("Droplets") { value = erosionDroplets };
            droplets.RegisterValueChangedCallback(evt => erosionDroplets = Mathf.Max(0, evt.newValue));
            erodeFoldout.Add(droplets);

            IntegerField seed = new IntegerField("Erosion Seed") { value = erosionSeed };
            seed.RegisterValueChangedCallback(evt => erosionSeed = evt.newValue);
            erodeFoldout.Add(seed);
            shapeFoldout.Add(erodeFoldout);

            // ---- Mesh ----
            var meshFoldout = new Foldout { text = "③ Bake Meshes", value = true };
            Toggle bakeToggle = new Toggle("Bake Meshes With Generate") { value = bakeMeshes };
            bakeToggle.RegisterValueChangedCallback(evt => bakeMeshes = evt.newValue);
            meshFoldout.Add(bakeToggle);

            TextField folder = new TextField("Output Folder") { value = outputFolder };
            folder.RegisterValueChangedCallback(evt => outputFolder = evt.newValue);
            meshFoldout.Add(folder);

            Toggle vertexColors = new Toggle("Vertex Biome Colors") { value = paintVertexBiomes };
            vertexColors.SetEnabled(biomeMap != null);
            vertexColors.tooltip = "Requires a High-Res Biome Map. Paints per-vertex biome colors.";
            vertexColors.RegisterValueChangedCallback(evt => paintVertexBiomes = evt.newValue);
            meshFoldout.Add(vertexColors);

            Toggle caveTint = new Toggle("Darken Cave Vertices") { value = darkenCaveVertices };
            caveTint.tooltip = "Lerps vertices with rock cover overhead toward a cool shadow tone, so carved caves read dark without extra lighting.";
            caveTint.RegisterValueChangedCallback(evt => darkenCaveVertices = evt.newValue);
            meshFoldout.Add(caveTint);

            Toggle assemble = new Toggle("Assemble Scene Objects") { value = assembleSceneObjects };
            assemble.tooltip = "Creates TerrainRoot hierarchy with renderers, colliders and runtime deformer hooks.";
            assemble.RegisterValueChangedCallback(evt => assembleSceneObjects = evt.newValue);
            meshFoldout.Add(assemble);

            Toggle colliders = new Toggle("Add Mesh Colliders") { value = addColliders };
            colliders.SetEnabled(assembleSceneObjects);
            colliders.RegisterValueChangedCallback(evt => addColliders = evt.newValue);
            meshFoldout.Add(colliders);

            ObjectField materialField = new ObjectField("Material") { objectType = typeof(Material), value = terrainMaterial };
            materialField.RegisterValueChangedCallback(evt => terrainMaterial = evt.newValue as Material);
            meshFoldout.Add(materialField);

            // ---- Splat / LOD / Erosion (v0.8.0) ----
            var splatFoldout = new Foldout { text = "Splat Layers", value = false };
            ObjectField s0 = new ObjectField("Layer 0 (Sand)") { objectType = typeof(Texture2D), value = splat0 };
            ObjectField s1 = new ObjectField("Layer 1 (Grass)") { objectType = typeof(Texture2D), value = splat1 };
            ObjectField s2 = new ObjectField("Layer 2 (Rock)") { objectType = typeof(Texture2D), value = splat2 };
            ObjectField s3 = new ObjectField("Layer 3 (Seabed)") { objectType = typeof(Texture2D), value = splat3 };
            s0.RegisterValueChangedCallback(e => splat0 = e.newValue as Texture2D);
            s1.RegisterValueChangedCallback(e => splat1 = e.newValue as Texture2D);
            s2.RegisterValueChangedCallback(e => splat2 = e.newValue as Texture2D);
            s3.RegisterValueChangedCallback(e => splat3 = e.newValue as Texture2D);
            splatFoldout.Add(s0); splatFoldout.Add(s1); splatFoldout.Add(s2); splatFoldout.Add(s3);
            meshFoldout.Add(splatFoldout);

            Toggle lods = new Toggle("Generate LOD Chain") { value = generateLodChain };
            lods.tooltip = "LOD1/LOD2 meshes + LODGroup on assembled chunks.";
            lods.RegisterValueChangedCallback(evt => generateLodChain = evt.newValue);
            meshFoldout.Add(lods);

            Slider lod1 = new Slider("LOD1 Ratio", 0.15f, 0.9f) { value = lod1Ratio };
            lod1.RegisterValueChangedCallback(evt => lod1Ratio = evt.newValue);
            meshFoldout.Add(lod1);

            Slider lod2 = new Slider("LOD2 Ratio", 0.05f, 0.6f) { value = lod2Ratio };
            lod2.RegisterValueChangedCallback(evt => lod2Ratio = evt.newValue);
            meshFoldout.Add(lod2);

            Toggle erosionToggle = new Toggle("Export Erosion Map") { value = exportErosionMap };
            erosionToggle.tooltip = "R = eroded, G = deposited. Saved next to the meshes.";
            erosionToggle.RegisterValueChangedCallback(evt => exportErosionMap = evt.newValue);
            meshFoldout.Add(erosionToggle);

            meshFoldout.Add(new Button(BakeMeshesOnly) { text = WorldBuilderLocalization.Get("btn.bakeMeshes") });
            root.Add(meshFoldout);

            // ---- Biome ----
            var biomeFoldout = new Foldout { text = "④ Biomes", value = false };
            ObjectField biomeField = new ObjectField("High-Res Biome Map")
            {
                objectType = typeof(HighResBiomeMap),
                value = biomeMap
            };
            biomeField.RegisterValueChangedCallback(evt =>
            {
                biomeMap = evt.newValue as HighResBiomeMap;
                cachedSectionOrigin = null;
            });
            biomeFoldout.Add(biomeField);
            biomeFoldout.Add(new Button(ApplyBiomes) { text = WorldBuilderLocalization.Get("btn.applyBiomes") });
            root.Add(biomeFoldout);

            // ---- Ecology ----
            var ecologyFoldout = new Foldout { text = "⑤ Ecology (PCG)", value = false };
            ObjectField rulesField = new ObjectField("Scatter Rule Set")
            {
                objectType = typeof(ScatterRuleSet),
                value = ruleSet
            };
            rulesField.RegisterValueChangedCallback(evt => ruleSet = evt.newValue as ScatterRuleSet);
            ecologyFoldout.Add(rulesField);

            ObjectField waterField = new ObjectField("Water Runtime Data")
            {
                objectType = typeof(WaterWorldRuntimeData),
                value = waterData,
                tooltip = "Optional. Enables underwater depth/flow gates for surface scatter rules."
            };
            waterField.RegisterValueChangedCallback(evt => waterData = evt.newValue as WaterWorldRuntimeData);
            ecologyFoldout.Add(waterField);

            IntegerField ecoSeed = new IntegerField("Ecology Seed") { value = ecologySeed };
            ecoSeed.RegisterValueChangedCallback(evt => ecologySeed = evt.newValue);
            ecologyFoldout.Add(ecoSeed);

            ecologyFoldout.Add(new Button(ScatterEcology) { text = WorldBuilderLocalization.Get("btn.scatterEcology") });
            ecologyFoldout.Add(new Button(ScatterCaveInterior)
            {
                text = "Scatter Cave Interior"
            });
            root.Add(ecologyFoldout);

            // ---- Caves ----
            var caveFoldout = new Foldout { text = "⑥ Caves", value = false };
            ObjectField caveField = new ObjectField("Cave Shape Params")
            {
                objectType = typeof(CaveShapeParams),
                value = caveParams
            };
            caveField.RegisterValueChangedCallback(evt => caveParams = evt.newValue as CaveShapeParams);
            caveFoldout.Add(caveField);

            var cavePresetRow = new VisualElement();
            cavePresetRow.style.flexDirection = FlexDirection.Row;
            foreach (CavePreset preset in Enum.GetValues(typeof(CavePreset)))
            {
                var presetButton = new Button(() => ApplyCavePreset(preset)) { text = preset.ToString() };
                presetButton.style.flexGrow = 1;
                cavePresetRow.Add(presetButton);
            }
            caveFoldout.Add(cavePresetRow);

            caveFoldout.Add(new Button(CreateCaveAsset)
            {
                text = "Create Cave Params Asset"
            });

            Toggle carveDuring = new Toggle("Carve During Generate") { value = carveCavesDuringGenerate };
            carveDuring.tooltip = "Runs cave carving right after density generation, before mesh baking.";
            carveDuring.RegisterValueChangedCallback(evt => carveCavesDuringGenerate = evt.newValue);
            caveFoldout.Add(carveDuring);

            caveFoldout.Add(new Button(CarveCaves) { text = "Carve Caves Only" });
            root.Add(caveFoldout);

            // ---- Preview ----
            Toggle section = new Toggle("Show Cross-Section Preview") { value = showCrossSection };
            section.RegisterValueChangedCallback(evt => showCrossSection = evt.newValue);
            root.Add(section);

            status = new Label();
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 8f;
            status.style.color = new Color(0.6f, 0.9f, 1f);
            root.Add(status);

            return root;
        }

        public void OnSceneGUI()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;

            DrawRadiusRing();
            if (showCrossSection && shapeParams != null) DrawCrossSection();
        }

        private void DrawRadiusRing()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            Handles.color = new Color(0.4f, 1f, 0.8f, 0.9f);
            Handles.DrawWireDisc(view.pivot, Vector3.up, radius);
        }

        /// <summary>Live terrain profile along the camera forward axis.</summary>
        private void DrawCrossSection()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Vector3 pivot = view.pivot;
            Vector3 forward = view.camera != null ? view.camera.transform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            if (cachedSectionOrigin == null ||
                (cachedSectionOrigin.Value - pivot).sqrMagnitude > 1f ||
                cachedSectionForward != forward)
            {
                cachedSectionOrigin = pivot;
                cachedSectionForward = forward;
                cachedSectionPoints = BuildSectionPoints(pivot, forward);
            }

            Handles.color = new Color(1f, 0.7f, 0.2f, 0.95f);
            Handles.DrawPolyLine(cachedSectionPoints);
            Handles.Label(cachedSectionPoints[cachedSectionPoints.Length / 2] + Vector3.up * 4f,
                "cross-section");
        }

        private Vector3? cachedSectionOrigin;
        private Vector3 cachedSectionForward;
        private Vector3[] cachedSectionPoints = System.Array.Empty<Vector3>();

        private Vector3[] BuildSectionPoints(Vector3 pivot, Vector3 forward)
        {
            var noise = new FbmNoise(shapeParams.seed);
            const int samples = 96;
            float halfLength = Mathf.Min(radius, 800f);
            var points = new Vector3[samples];
            for (int i = 0; i < samples; i++)
            {
                float distance = (i / (float)(samples - 1) - 0.5f) * 2f * halfLength;
                Vector3 flat = pivot + forward * distance;
                float height = TerrainField.HeightAt(noise, shapeParams,
                    new Unity.Mathematics.float2(flat.x, flat.z));
                points[i] = new Vector3(flat.x, height, flat.z);
            }
            return points;
        }

        private void ApplyPreset(TerrainPreset preset)
        {
            if (shapeParams == null)
            {
                shapeParams = ScriptableObject.CreateInstance<TerrainShapeParams>();
            }
            else
            {
                Undo.RecordObject(shapeParams, $"Apply Preset {preset}");
            }

            TerrainPresets.Apply(shapeParams, preset);
            EditorUtility.SetDirty(shapeParams);
            cachedSectionOrigin = null;
            SetStatus($"Preset '{preset}' applied (seed {shapeParams.seed}).");
            UndoHistory.Push($"Apply Terrain Preset {preset}");
        }

        private void SetStatus(string message)
        {
            if (status != null) status.text = message;
        }

        private void CreateShapeAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Shape Params", "TerrainShapeParams", "asset",
                "Choose where to store the terrain shape parameters.");
            if (string.IsNullOrEmpty(path)) return;

            TerrainShapeParams asset = ScriptableObject.CreateInstance<TerrainShapeParams>();
            AssetDatabase.CreateAsset(asset, path);
            shapeParams = asset;
            UndoHistory.Push("Create Shape Params");
        }

        private bool Validate(out VoxelStoreAsset storeAsset, out TerrainShapeParams parameters)
        {
            storeAsset = store;
            parameters = shapeParams;
            if (parameters == null)
            {
                SetStatus("Assign or create Terrain Shape Params first.");
                return false;
            }
            return true;
        }

        private void Generate()
        {
            if (!Validate(out VoxelStoreAsset storeAsset, out TerrainShapeParams parameters)) return;
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            var watch = Stopwatch.StartNew();
            try
            {
                EditorUtility.DisplayProgressBar("Terrain Forge", "Generating heightfield…", 0.15f);
                const float cellSize = 2f;
                int size = Mathf.CeilToInt(radius * 2f / cellSize) + 1;
                Vector2 origin = new Vector2(pivot.x - size * cellSize * 0.5f, pivot.z - size * cellSize * 0.5f);

                TerrainField.HeightMap heights = TerrainField.BuildHeightMap(parameters, origin, size, cellSize);

                EditorUtility.DisplayProgressBar("Terrain Forge", "Simulating erosion…", 0.45f);
                ErosionSimulator.Apply(heights, new ErosionSimulator.Params
                {
                    DropletCount = erosionDroplets
                }, erosionSeed, out float[] erosionMap);
                lastErosionMap = erosionMap;
                lastErosionOrigin = origin;
                lastErosionSize = size;
                lastErosionCellSize = cellSize;

                if (exportErosionMap) ExportErosionMap(erosionMap);

                EditorUtility.DisplayProgressBar("Terrain Forge", "Writing voxel density…", 0.75f);
                Undo.RecordObject(storeAsset, "Generate Terrain");
                float chunkSize = ChunkSize;
                int chunks = TerrainField.WriteDensity(storeAsset, heights, parameters, chunkSize, storeAsset.Resolution);
                EditorUtility.SetDirty(storeAsset);

                int carved = 0;
                if (carveCavesDuringGenerate && caveParams != null)
                {
                    EditorUtility.DisplayProgressBar("Terrain Forge", "Carving caves…", 0.85f);
                    carved = CaveField.Carve(storeAsset, heights, parameters, caveParams, chunkSize);
                    EditorUtility.SetDirty(storeAsset);
                }

                int meshes = 0;
                long vertices = 0;
                if (bakeMeshes) (meshes, vertices) = BakeMeshesAround(pivot);

                watch.Stop();
                double seconds = Math.Max(0.001, watch.Elapsed.TotalSeconds);
                string carveInfo = carved > 0 ? $", {carved:N0} cave voxels" : "";
                SetStatus($"Generated {chunks} chunk(s), {meshes} mesh(es){carveInfo}, {vertices:N0} vertices " +
                          $"in {seconds:F1}s ({chunks / seconds:F1} chunks/s).");
                UndoHistory.Push(carved > 0
                    ? $"Generate Terrain ({chunks}, caves: {carved})"
                    : $"Generate Terrain ({chunks})");
            }
            catch (System.Exception exception)
            {
                SetStatus("Failed: " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
        }

        private void BakeMeshesOnly()
        {
            SceneView view = SceneView.lastActiveSceneView;
            (int meshes, long vertices) = BakeMeshesAround(view != null ? view.pivot : Vector3.zero);
            SetStatus($"Baked {meshes} mesh(es), {vertices:N0} vertices.");
        }

        private (int meshes, long vertices) BakeMeshesAround(Vector3 center)
        {
            VoxelStoreAsset storeAsset = store;
            float chunkSize = ChunkSize;
            int resolution = storeAsset.Resolution;
            int span = Mathf.CeilToInt(radius / chunkSize);
            Vector3Int centerChunk = new Vector3Int(
                Mathf.FloorToInt(center.x / chunkSize), 0, Mathf.FloorToInt(center.z / chunkSize));

            EnsureFolder(outputFolder);

            // Collect candidate chunks (those with data).
            var coords = new System.Collections.Generic.List<Vector3Int>();
            for (int cz = centerChunk.z - span; cz <= centerChunk.z + span; cz++)
            for (int cx = centerChunk.x - span; cx <= centerChunk.x + span; cx++)
            {
                bool anyLayer = false;
                for (int cy = 0; cy < 4 && !anyLayer; cy++)
                    anyLayer = storeAsset.TryGetEntry(new Vector3Int(cx, cy, cz), out _);
                if (anyLayer) coords.Add(new Vector3Int(cx, 0, cz));
            }

            Func<Vector3, Color> baseColorSampler =
                paintVertexBiomes && biomeMap != null
                    ? (Func<Vector3, Color>)(v => biomeMap.SampleColor(v.x, v.z, chunkSize))
                    : null;

            Func<Vector3, Color> colorSampler = baseColorSampler;
            if (baseColorSampler != null && darkenCaveVertices)
            {
                // Per-thread sampler: the shade march runs inside the Parallel.For pass.
                var threadShadeSamplers =
                    new ThreadLocal<VoxelWorldSampler>(() => new VoxelWorldSampler(storeAsset, chunkSize));
                colorSampler = v => CaveAmbientTint.Shade(threadShadeSamplers.Value, v, baseColorSampler(v));
            }

            var watch = Stopwatch.StartNew();

            // Pass A — parallel pure geometry (per-thread sampler; store is read-only here).
            var geometries = new SurfaceNetsMesher.MeshGeometry[coords.Count];
            object progressLock = new object();
            int completed = 0;
            Parallel.For(0, coords.Count, index =>
            {
                var threadSampler = new VoxelWorldSampler(storeAsset, chunkSize);
                geometries[index] = SurfaceNetsMesher.ComputeGeometry(
                    threadSampler, coords[index], resolution, chunkSize, colorSampler);
                lock (progressLock)
                {
                    completed++;
                    if (completed % 8 == 0)
                        EditorUtility.DisplayProgressBar("Terrain Forge",
                            $"Computing geometry {completed}/{coords.Count}…", completed / (float)coords.Count);
                }
            });

            // Pass B — main thread: build meshes, save assets, assemble objects.
            GameObject terrainRoot = null;
            if (assembleSceneObjects)
            {
                terrainRoot = GameObject.Find("__WB_Terrain");
                if (terrainRoot == null)
                {
                    terrainRoot = new GameObject("__WB_Terrain");
                    Undo.RegisterCreatedObjectUndo(terrainRoot, "Assemble Terrain");
                }
                Undo.RecordObject(terrainRoot, "Assemble Terrain");
            }

            long totalVertices = 0;
            int baked = 0;
            Shader splatShader = Shader.Find("WorldBuilder/TerrainSplat");

            for (int i = 0; i < coords.Count; i++)
            {
                SurfaceNetsMesher.Result result = SurfaceNetsMesher.BuildMesh(geometries[i]);
                if (result.Mesh == null) continue;

                Vector3Int coord = coords[i];
                result.Mesh.name = $"T_{coord.x}_{coord.y}_{coord.z}";
                string path = $"{outputFolder}/{result.Mesh.name}.asset";

                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(result.Mesh, path);

                // LOD chain.
                Mesh lod1Mesh = null;
                Mesh lod2Mesh = null;
                if (generateLodChain)
                {
                    lod1Mesh = LODGeneratorTool.LODMeshSimplifier.Simplify(
                        result.Mesh, lod1Ratio, result.Mesh.name + "_LOD1");
                    lod2Mesh = LODGeneratorTool.LODMeshSimplifier.Simplify(
                        result.Mesh, lod2Ratio, result.Mesh.name + "_LOD2");
                    AssetDatabase.CreateAsset(lod1Mesh, $"{outputFolder}/{lod1Mesh.name}.asset");
                    AssetDatabase.CreateAsset(lod2Mesh, $"{outputFolder}/{lod2Mesh.name}.asset");
                }

                // Splatmap + material.
                Material chunkMaterial = terrainMaterial;
                if (biomeMap != null && paintVertexBiomes && splatShader != null &&
                    (splat0 != null || splat1 != null || splat2 != null))
                {
                    const int splatSize = 128;
                    Color32[] splatPixels = SplatBaker.Bake(biomeMap, coord, splatSize, chunkSize, layerMapping);
                    var splatTexture = new Texture2D(splatSize, splatSize, TextureFormat.RGBA32, false, true)
                    {
                        name = $"{result.Mesh.name}_splat"
                    };
                    splatTexture.SetPixels32(splatPixels);
                    splatTexture.Apply(false, false);
                    string splatPath = $"{outputFolder}/{splatTexture.name}.asset";
                    Texture2D existingSplat = AssetDatabase.LoadAssetAtPath<Texture2D>(splatPath);
                    if (existingSplat != null) AssetDatabase.DeleteAsset(splatPath);
                    AssetDatabase.CreateAsset(splatTexture, splatPath);

                    chunkMaterial = new Material(splatShader) { name = result.Mesh.name + "_mat" };
                    if (terrainMaterial != null) chunkMaterial.CopyPropertiesFromMaterial(terrainMaterial);
                    chunkMaterial.shader = splatShader;
                    chunkMaterial.SetTexture("_Control", splatTexture);
                    if (splat0) chunkMaterial.SetTexture("_Splat0", splat0);
                    if (splat1) chunkMaterial.SetTexture("_Splat1", splat1);
                    if (splat2) chunkMaterial.SetTexture("_Splat2", splat2);
                    if (splat3) chunkMaterial.SetTexture("_Splat3", splat3);

                    string materialPath = $"{outputFolder}/{chunkMaterial.name}.mat";
                    Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (existingMat != null) AssetDatabase.DeleteAsset(materialPath);
                    AssetDatabase.CreateAsset(chunkMaterial, materialPath);
                }

                if (assembleSceneObjects)
                {
                    AssembleChunk(terrainRoot, coord, chunkSize, result.Mesh,
                        chunkMaterial, lod1Mesh, lod2Mesh);
                }

                baked++;
                totalVertices += result.VertexCount;
            }

            watch.Stop();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            UndoHistory.Push($"Bake Terrain Meshes ({baked})");
            return (baked, totalVertices);
        }

        private void AssembleChunk(GameObject root, Vector3Int coord, float chunkSize, Mesh mesh,
            Material materialOverride = null, Mesh lod1 = null, Mesh lod2 = null)
        {
            string name = $"Chunk_{coord.x}_{coord.y}_{coord.z}";
            Transform existing = root.transform.Find(name);
            GameObject chunkObject = existing != null ? existing.gameObject : null;

            if (chunkObject == null)
            {
                chunkObject = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(chunkObject, "Assemble Terrain Chunk");
                chunkObject.transform.SetParent(root.transform, false);
            }

            chunkObject.transform.position = new Vector3(coord.x * chunkSize, coord.y * chunkSize, coord.z * chunkSize);

            var filter = chunkObject.GetComponent<MeshFilter>();
            if (filter == null) filter = chunkObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var chunkRendererComponent = chunkObject.GetComponent<TerrainChunkRenderer>();
            if (chunkRendererComponent == null) chunkRendererComponent = chunkObject.AddComponent<TerrainChunkRenderer>();
            chunkRendererComponent.Configure(coord);

            var renderer = chunkObject.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = chunkObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materialOverride != null
                ? materialOverride
                : terrainMaterial != null
                    ? terrainMaterial
                    : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            if (addColliders)
            {
                var collider = chunkObject.GetComponent<MeshCollider>();
                if (collider == null) collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            // LOD group.
            var lodGroup = chunkObject.GetComponent<LODGroup>();
            if (lod1 != null && lod2 != null)
            {
                if (lodGroup == null) lodGroup = chunkObject.AddComponent<LODGroup>();
                var renderers = new[] { renderer };
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.6f, renderers),
                    new LOD(0.25f, CreateLodRenderers(chunkObject, lod1, renderer)),
                    new LOD(0.08f, CreateLodRenderers(chunkObject, lod2, renderer))
                });
            }
            else if (lodGroup != null)
            {
                UnityEngine.Object.DestroyImmediate(lodGroup, true);
            }
        }

        private Renderer[] CreateLodRenderers(GameObject owner, Mesh lodMesh, MeshRenderer template)
        {
            string childName = "LOD_" + lodMesh.name;
            Transform child = owner.transform.Find(childName);
            GameObject lodObject;
            if (child == null)
            {
                lodObject = new GameObject(childName);
                lodObject.transform.SetParent(owner.transform, false);
            }
            else
            {
                lodObject = child.gameObject;
            }

            var filter = lodObject.GetComponent<MeshFilter>();
            if (filter == null) filter = lodObject.AddComponent<MeshFilter>();
            filter.sharedMesh = lodMesh;

            var lodRenderer = lodObject.GetComponent<MeshRenderer>();
            if (lodRenderer == null) lodRenderer = lodObject.AddComponent<MeshRenderer>();
            lodRenderer.sharedMaterial = template != null ? template.sharedMaterial : null;
            return new Renderer[] { lodRenderer };
        }

        private void ExportErosionMap(float[] erosionMap)
        {
            EnsureFolder(outputFolder);
            int size = lastErosionSize;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);

            float maxAbs = 0.01f;
            for (int i = 0; i < erosionMap.Length; i++)
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(erosionMap[i]));

            Color32[] pixels = new Color32[size * size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float delta = erosionMap[z * size + x];
                    var eroded = (byte)(Mathf.Clamp01(-delta / maxAbs) * 255f);
                    var deposited = (byte)(Mathf.Clamp01(delta / maxAbs) * 255f);
                    pixels[z * size + x] = new Color32(eroded, deposited, 0, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes($"{outputFolder}/erosion_map.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();
            Debug.Log($"[WorldBuilder] Erosion map exported to {outputFolder}/erosion_map.png");
        }

        private void ApplyBiomes()
        {
            if (!Validate(out _, out TerrainShapeParams parameters)) return;
            if (biomeMap == null)
            {
                SetStatus("Assign a High-Res Biome Map first.");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            const int cellsPerChunk = 8;
            var classifierInputs = new BiomeClassifier.ClimateInputs { SeaLevel = 0f };
            var noise = new FbmNoise(parameters.seed ^ 0x51ed270b);

            var keys = new System.Collections.Generic.List<Vector3Int>();
            var ids = new System.Collections.Generic.List<byte[]>();

            int spanChunks = Mathf.CeilToInt(radius / 128f);
            Vector3Int centerChunk = new Vector3Int(Mathf.FloorToInt(pivot.x / 128f), 0, Mathf.FloorToInt(pivot.z / 128f));

            for (int cz = centerChunk.z - spanChunks; cz <= centerChunk.z + spanChunks; cz++)
            {
                for (int cx = centerChunk.x - spanChunks; cx <= centerChunk.x + spanChunks; cx++)
                {
                    var cells = new byte[cellsPerChunk * cellsPerChunk];
                    for (int lz = 0; lz < cellsPerChunk; lz++)
                    {
                        for (int lx = 0; lx < cellsPerChunk; lx++)
                        {
                            float wx = (cx + (lx + 0.5f) / cellsPerChunk) * 128f;
                            float wz = (cz + (lz + 0.5f) / cellsPerChunk) * 128f;
                            float elevation = TerrainField.HeightAt(noise, parameters, new Unity.Mathematics.float2(wx, wz));
                            var biome = BiomeClassifier.Classify(noise, classifierInputs,
                                new Unity.Mathematics.float2(wx, wz), elevation);
                            cells[lz * cellsPerChunk + lx] = (byte)biome;
                        }
                    }
                    keys.Add(new Vector3Int(cx, 0, cz));
                    ids.Add(cells);
                }
            }

            Undo.RecordObject(biomeMap, "Apply Biomes");
            biomeMap.Configure(cellsPerChunk, keys, ids);
            EditorUtility.SetDirty(biomeMap);
            SetStatus($"Applied biomes to {keys.Count} chunk(s).");
            UndoHistory.Push($"Apply Biomes ({keys.Count})");
        }

        private void ScatterEcology()
        {
            if (ruleSet == null)
            {
                SetStatus("Assign a Scatter Rule Set first.");
                return;
            }
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            if (bridge == null || bridge.WorldGrid == null)
            {
                SetStatus("BlenderBridgeSettings required to bake placements.");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;
            var bounds = new Rect(pivot.x - radius, pivot.z - radius, radius * 2f, radius * 2f);

            var query = new VoxelTerrainQuery(store, ChunkSize, biomeMap, waterData);
            System.Collections.Generic.List<PcgPlacement> placements =
                PcgScatterEngine.Generate(ruleSet, query, bounds, ecologySeed);

            BakePlacements(placements, bridge, "Ecology");
        }

        private void ScatterCaveInterior()
        {
            if (ruleSet == null)
            {
                SetStatus("Assign a Scatter Rule Set first.");
                return;
            }
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            if (bridge == null || bridge.WorldGrid == null)
            {
                SetStatus("BlenderBridgeSettings required to bake placements.");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            float chunkSize = ChunkSize;
            var sampler = new VoxelWorldSampler(store, chunkSize);
            var query = new VoxelVolumeQuery(sampler, chunkSize, biomeMap);
            var volume = new Bounds(new Vector3(pivot.x, caveParams != null
                ? (caveParams.minY + caveParams.maxY) * 0.5f : 0f, pivot.z),
                new Vector3(radius * 2f,
                    caveParams != null ? Mathf.Max(8f, caveParams.maxY - caveParams.minY) : 64f,
                    radius * 2f));

            System.Collections.Generic.List<PcgPlacement> placements =
                VoxelVolumeScatter.Generate(ruleSet, query, volume, ecologySeed);

            BakePlacements(placements, bridge, "Cave Ecology");
        }

        private void BakePlacements(System.Collections.Generic.List<PcgPlacement> placements,
            BlenderBridgeSettings bridge, string label)
        {
            var brushPlacements = new System.Collections.Generic.List<BrushPlacement>(placements.Count);
            for (int i = 0; i < placements.Count; i++)
            {
                brushPlacements.Add(new BrushPlacement
                {
                    prefab = placements[i].Prefab,
                    position = placements[i].Position,
                    rotation = placements[i].Rotation,
                    scale = placements[i].Scale
                });
            }

            ScatterChunkBaker.BakeSummary summary = ScatterChunkBaker.BakePlacements(brushPlacements, bridge);
            SetStatus($"{label}: {placements.Count} candidate(s), {summary.PlacementsAdded} baked into " +
                      $"{summary.ChunksUpdated} chunk(s). Skipped: {summary.Skipped.Count}.");
            UndoHistory.Push($"{label} ({summary.PlacementsAdded})");
        }

        private void ApplyCavePreset(CavePreset preset)
        {
            if (caveParams == null)
            {
                caveParams = ScriptableObject.CreateInstance<CaveShapeParams>();
            }
            else
            {
                Undo.RecordObject(caveParams, $"Apply Cave Preset {preset}");
            }

            CavePresets.Apply(caveParams, preset);
            EditorUtility.SetDirty(caveParams);
            SetStatus($"Cave preset '{preset}' applied.");
            UndoHistory.Push($"Apply Cave Preset {preset}");
        }

        private void CreateCaveAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Cave Params", "CaveShapeParams", "asset",
                "Choose where to store the cave shape parameters.");
            if (string.IsNullOrEmpty(path)) return;

            CaveShapeParams asset = ScriptableObject.CreateInstance<CaveShapeParams>();
            AssetDatabase.CreateAsset(asset, path);
            caveParams = asset;
            UndoHistory.Push("Create Cave Params");
        }

        private void CarveCaves()
        {
            if (!Validate(out VoxelStoreAsset storeAsset, out TerrainShapeParams parameters)) return;
            if (caveParams == null)
            {
                SetStatus("Assign or create Cave Shape Params first.");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            var watch = Stopwatch.StartNew();
            try
            {
                EditorUtility.DisplayProgressBar("Terrain Forge", "Carving caves…", 0.4f);

                // Rebuild the surface heightmap so surface-protection depth matches the
                // terrain that is currently in the store.
                const float cellSize = 2f;
                int size = Mathf.CeilToInt(radius * 2f / cellSize) + 1;
                Vector2 origin = new Vector2(pivot.x - size * cellSize * 0.5f,
                    pivot.z - size * cellSize * 0.5f);
                TerrainField.HeightMap heights =
                    TerrainField.BuildHeightMap(parameters, origin, size, cellSize);

                Undo.RecordObject(storeAsset, "Carve Caves");
                float chunkSize = ChunkSize;
                int carved = CaveField.Carve(storeAsset, heights, parameters, caveParams, chunkSize);
                EditorUtility.SetDirty(storeAsset);

                watch.Stop();
                double seconds = Math.Max(0.001, watch.Elapsed.TotalSeconds);

                int meshes = 0;
                long vertices = 0;
                if (bakeMeshes)
                {
                    EditorUtility.DisplayProgressBar("Terrain Forge", "Rebaking meshes…", 0.8f);
                    (meshes, vertices) = BakeMeshesAround(pivot);
                }

                SetStatus($"Carved {carved:N0} voxel(s) in {seconds:F1}s" +
                          (bakeMeshes ? $", rebaked {meshes} mesh(es)." : "."));
                UndoHistory.Push($"Carve Caves ({carved})");
            }
            catch (System.Exception exception)
            {
                SetStatus("Failed: " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
        }

        private sealed class VoxelTerrainQuery : ITerrainQuery, IWaterAwareTerrainQuery
        {
            private readonly VoxelStoreAsset store;
            private readonly float chunkSize;
            private readonly HighResBiomeMap biomes;
            private readonly VoxelWorldSampler sampler;
            private readonly WaterQueryService water;

            public VoxelTerrainQuery(VoxelStoreAsset store, float chunkSize, HighResBiomeMap biomes,
                WaterWorldRuntimeData waterData = null)
            {
                this.store = store;
                this.chunkSize = chunkSize;
                this.biomes = biomes;
                sampler = new VoxelWorldSampler(store, chunkSize);
                water = waterData != null ? new WaterQueryService(waterData) : null;
            }

            public bool TryHeight(Vector2 worldXz, out float height)
            {
                const float topY = 400f;
                const float bottomY = -200f;
                float previous = topY;
                for (float y = topY; y >= bottomY; y -= 1f)
                {
                    float density = sampler.Sample(worldXz.x, y, worldXz.y);
                    if (density < SurfaceNetsMesher.IsoLevel)
                    {
                        height = Mathf.Lerp(y + 1f, previous, 0.5f);
                        return true;
                    }
                    previous = y;
                }
                height = default;
                return false;
            }

            public bool TrySampleWater(Vector3 worldXzAtTerrainHeight,
                out WorldBuilder.Runtime.Water.WaterSample sample)
            {
                if (water == null)
                {
                    sample = default;
                    return false;
                }
                sample = water.Sample(worldXzAtTerrainHeight);
                return sample.IsInWater && sample.Depth > 0.05f;
            }

            public float Slope(Vector2 worldXz)
            {
                TryHeight(worldXz, out float center);
                TryHeight(worldXz + new Vector2(1f, 0f), out float right);
                TryHeight(worldXz + new Vector2(0f, 1f), out float up);
                float dx = right - center;
                float dz = up - center;
                return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
            }

            public BiomeType BiomeAt(Vector2 worldXz)
            {
                return biomes != null
                    ? biomes.SampleBiome(worldXz.x, worldXz.y, chunkSize)
                    : BiomeType.Forest;
            }
        }

        private float ChunkSize => 128f;

        private static void EnsureFolder(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
