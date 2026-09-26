using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Coral;
using WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Rock;
using WorldBuilder.Editor.PrefabBrush.EnvironmentBrush.Vegetation;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Editor.PrefabBrush
{
    public sealed class PrefabBrushTool : IWorldBuilderTool, IRaycastConsumer
    {
        private const float SpatialCellSize = 4f;

        private readonly IBiomeMap biomeMap;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();
        private readonly Dictionary<Mesh, List<Matrix4x4>> previewBatches = new Dictionary<Mesh, List<Matrix4x4>>();
        private readonly Dictionary<GameObject, MeshFilter[]> prefabMeshCache = new Dictionary<GameObject, MeshFilter[]>();

        private PrefabBrushSettings settings;
        private SpatialHash<GameObject> spatialHash;
        private Material previewMaterial;
        private MaterialPropertyBlock previewBlock;
        private List<IEnvironmentBrush> environmentBrushes;
        private Vector3 lastPaintPosition;
        private bool hasLastPaintPosition;
        private WaterQueryService waterService;
        private WaterWorldRuntimeData waterDataCache;
        private PrefabBrushPreset loadedPreset;
        private PrefabPaletteView palette;

        public PrefabBrushTool(IBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => "Prefab Brush Pro++";
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            prefabMeshCache.Clear();
            hasLastPaintPosition = false;
            EnsureInit();
            RebuildSpatialHash();
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public VisualElement CreateInspectorGUI()
        {
            EnsureInit();

            VisualElement root = new VisualElement();
            PrefabBrushUI.ApplyStyle(root);

            root.Add(InspectorHelp.Build(ToolName, "help.prefabBrush"));
            root.Add(BuildModeSection());
            root.Add(BuildBrushSection());
            root.Add(BuildPrefabSection());
            root.Add(BuildPlacementSection());
            root.Add(BuildMaskSection());
            root.Add(BuildEnvironmentSection());
            root.Add(BuildModifierGraphSection());
            root.Add(BuildPresetSection());
            root.Add(BuildStrokeSection());

            return root;
        }

        public void OnSceneGUI()
        {
            EnsureInit();

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (!settings.eraseMode && !HasValidPrefabs())
            {
                return;
            }

            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            Handles.color = settings.eraseMode ? Color.red : Color.green;
            Handles.DrawWireDisc(hit.point, hit.normal, settings.brushRadius);

            if (!settings.eraseMode)
            {
                int strokeSeed = CombineSeed(settings.seed, hit.point);
                List<BrushPlacement> placements = BuildPlacements(hit.point, strokeSeed, settings.brushRadius, settings.brushDensity);
                DrawPreview(placements);
            }

            SceneView.RepaintAll();

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (settings.eraseMode)
                {
                    Erase(hit.point);
                }
                else
                {
                    Paint(hit.point);
                }

                hasLastPaintPosition = true;
                lastPaintPosition = hit.point;
                e.Use();
            }
            else if (settings.paintOnDrag && !settings.eraseMode &&
                     e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            {
                float spacing = Mathf.Max(0.05f, settings.dragSpacing);
                if (!hasLastPaintPosition || Vector3.Distance(lastPaintPosition, hit.point) >= spacing)
                {
                    Paint(hit.point);
                    hasLastPaintPosition = true;
                    lastPaintPosition = hit.point;
                }

                e.Use();
            }
        }

        private void EnsureInit()
        {
            if (settings == null)
            {
                settings = PrefabBrushSettingsLocator.LoadOrCreate();
            }

            if (previewMaterial == null)
            {
                BuildPreviewMaterial();
            }

            if (spatialHash == null)
            {
                RebuildSpatialHash();
            }

            if (environmentBrushes == null)
            {
                environmentBrushes = new List<IEnvironmentBrush>
                {
                    new VegetationBrush(),
                    new CoralBrush(),
                    new RockBrush()
                };
            }

            if (settings.waterData != null && (waterService == null || !ReferenceEquals(waterDataCache, settings.waterData)))
            {
                waterDataCache = settings.waterData;
                waterService = new WaterQueryService(settings.waterData);
            }
            else if (settings.waterData == null)
            {
                waterService = null;
                waterDataCache = null;
            }
        }

        private void BuildPreviewMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            previewMaterial.enableInstancing = true;

            Color color = new Color(0.2f, 1f, 0.4f, 0.35f);
            if (previewMaterial.HasProperty("_Surface"))
            {
                previewMaterial.SetFloat("_Surface", 1f);
                previewMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                previewMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                previewMaterial.SetInt("_ZWrite", 0);
                previewMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                previewMaterial.renderQueue = (int)RenderQueue.Transparent;
            }

            if (previewMaterial.HasProperty("_BaseColor"))
            {
                previewMaterial.SetColor("_BaseColor", color);
            }

            if (previewMaterial.HasProperty("_Color"))
            {
                previewMaterial.SetColor("_Color", color);
            }

            previewBlock = new MaterialPropertyBlock();
            previewBlock.SetColor("_BaseColor", color);
        }

        private void RebuildSpatialHash()
        {
            spatialHash = new SpatialHash<GameObject>(SpatialCellSize);
            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(all[i]))
                {
                    spatialHash.Add(all[i].transform.position, all[i]);
                }
            }
        }

        private bool HasValidPrefabs()
        {
            for (int i = 0; i < settings.prefabEntries.Count; i++)
            {
                if (settings.prefabEntries[i].prefab != null && settings.prefabEntries[i].weight > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private List<PrefabEntry> CollectValidEntries()
        {
            List<PrefabEntry> valid = new List<PrefabEntry>();
            for (int i = 0; i < settings.prefabEntries.Count; i++)
            {
                PrefabEntry entry = settings.prefabEntries[i];
                if (entry.prefab != null && entry.weight > 0f)
                {
                    valid.Add(entry);
                }
            }

            return valid;
        }

        private List<BrushPlacement> BuildPlacements(Vector3 center, int strokeSeed, float radius, int density)
        {
            List<BrushPlacement> placements = new List<BrushPlacement>();
            List<PrefabEntry> valid = CollectValidEntries();
            if (valid.Count == 0)
            {
                return placements;
            }

            float totalWeight = 0f;
            for (int i = 0; i < valid.Count; i++)
            {
                totalWeight += valid[i].weight;
            }

            System.Random rng = new System.Random(strokeSeed);

            for (int i = 0; i < density; i++)
            {
                PrefabEntry entry = PickWeighted(valid, totalWeight, rng);

                double angle = rng.NextDouble() * Math.PI * 2.0;
                double distance = Math.Sqrt(rng.NextDouble()) * radius;
                Vector3 position = center + new Vector3((float)(Math.Cos(angle) * distance), 0f, (float)(Math.Sin(angle) * distance));

                Vector3 normal = Vector3.up;
                if (SceneRaycaster.TryRaycastDown(position, out RaycastHit down))
                {
                    position = down.point;
                    normal = down.normal;
                }

                float yaw = (float)(rng.NextDouble() * 360.0);
                Vector2 scaleRange = entry.ScaleRange(settings);
                float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());

                if (!PassesMask(position, normal))
                {
                    continue;
                }

                Quaternion rotation = entry.AlignToNormal(settings) ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
                if (entry.RandomYaw(settings))
                {
                    rotation *= Quaternion.AngleAxis(yaw, Vector3.up);
                }

                BrushContext context = new BrushContext
                {
                    position = position + rotation * Vector3.up * entry.heightOffset,
                    normal = normal,
                    rotation = rotation,
                    scale = Vector3.one * scale
                };

                ModifierContext modifierContext = new ModifierContext
                {
                    worldPosition = context.position,
                    brushCenter = center,
                    brushRadius = radius,
                    surfaceNormal = context.normal,
                    biome = biomeMap.GetBiome(calculator.ToChunkCoord(context.position, settings.chunkSize)),
                    seed = strokeSeed
                };

                if (waterService != null)
                {
                    WaterSample sample = waterService.Sample(context.position);
                    modifierContext.inWater = sample.IsInWater;
                    modifierContext.waterDepth = sample.Depth;
                }

                context.position += ModifierGraphEvaluator.EvaluatePositionOffset(settings.modifierGraph, modifierContext);
                context.rotation *= Quaternion.Euler(ModifierGraphEvaluator.EvaluateRotation(settings.modifierGraph, modifierContext));
                context.scale = Vector3.Scale(context.scale, ModifierGraphEvaluator.EvaluateScale(settings.modifierGraph, modifierContext));

                if (context.scale.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                placements.Add(new BrushPlacement
                {
                    prefab = entry.prefab,
                    position = context.position,
                    rotation = context.rotation,
                    scale = context.scale
                });
            }

            return placements;
        }

        private PrefabEntry PickWeighted(List<PrefabEntry> valid, float totalWeight, System.Random rng)
        {
            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0.0;
            for (int i = 0; i < valid.Count; i++)
            {
                cumulative += valid[i].weight;
                if (roll <= cumulative)
                {
                    return valid[i];
                }
            }

            return valid[valid.Count - 1];
        }

        private bool PassesMask(Vector3 position, Vector3 normal)
        {
            BrushMask mask = settings.mask;

            if (mask.useHeightMask && (position.y < mask.minHeight || position.y > mask.maxHeight))
            {
                return false;
            }

            if (mask.useSlopeMask && Vector3.Angle(normal, Vector3.up) > mask.maxSlopeAngle)
            {
                return false;
            }

            if (mask.useBiomeMask)
            {
                Vector3Int coord = calculator.ToChunkCoord(position, settings.chunkSize);
                if (biomeMap.GetBiome(coord) != mask.allowedBiome)
                {
                    return false;
                }
            }

            return true;
        }

        private void Paint(Vector3 center)
        {
            int strokeSeed = CombineSeed(settings.seed, center);
            List<BrushPlacement> placements = BuildPlacements(center, strokeSeed, settings.brushRadius, settings.brushDensity);

            int group = Undo.GetCurrentGroup();

            Undo.RecordObject(settings, "Paint Prefabs");
            settings.strokes.Add(new BrushStroke
            {
                seed = strokeSeed,
                center = center,
                radius = settings.brushRadius,
                density = settings.brushDensity
            });
            EditorUtility.SetDirty(settings);

            PlaceInstances(placements);

            Undo.SetCurrentGroupName("Paint Prefabs");
            Undo.CollapseUndoOperations(group);
        }

        private void PlaceInstances(List<BrushPlacement> placements)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                BrushPlacement placement = placements[i];
                if (placement.prefab == null)
                {
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(placement.prefab);
                if (instance == null)
                {
                    continue;
                }

                instance.transform.position = placement.position;
                instance.transform.rotation = placement.rotation;
                instance.transform.localScale = placement.scale;

                if (settings.duplicateSharedScriptableObjects)
                {
                    DuplicateSharedScriptableObjects(instance);
                }

                Undo.RegisterCreatedObjectUndo(instance, "Paint Prefab");
                spatialHash.Add(instance.transform.position, instance);
            }
        }

        private void DuplicateSharedScriptableObjects(GameObject instance)
        {
            Component[] components = instance.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool changed = false;

                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (property.objectReferenceValue is ScriptableObject shared)
                    {
                        property.objectReferenceValue = UnityEngine.Object.Instantiate(shared);
                        changed = true;
                    }
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private void Erase(Vector3 center)
        {
            List<GameObject> hits = new List<GameObject>();
            spatialHash.Query(center, settings.brushRadius, hits);
            int group = Undo.GetCurrentGroup();

            for (int i = 0; i < hits.Count; i++)
            {
                GameObject instance = hits[i];
                if (instance == null)
                {
                    continue;
                }

                if (settings.eraseOnlyPaletteModels && !IsPaletteInstance(instance))
                {
                    continue;
                }

                spatialHash.Remove(instance.transform.position, instance);
                Undo.DestroyObjectImmediate(instance);
            }

            Undo.SetCurrentGroupName("Erase Prefabs");
            Undo.CollapseUndoOperations(group);
        }

        private bool IsPaletteInstance(GameObject instance)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source == null)
            {
                return false;
            }

            for (int i = 0; i < settings.prefabEntries.Count; i++)
            {
                if (settings.prefabEntries[i].prefab == source)
                {
                    return true;
                }
            }

            return false;
        }

        private void Replay()
        {
            int group = Undo.GetCurrentGroup();
            for (int i = 0; i < settings.strokes.Count; i++)
            {
                BrushStroke stroke = settings.strokes[i];
                List<BrushPlacement> placements = BuildPlacements(stroke.center, stroke.seed, stroke.radius, stroke.density);
                PlaceInstances(placements);
            }

            Undo.SetCurrentGroupName("Replay Strokes");
            Undo.CollapseUndoOperations(group);
        }

        private void DrawPreview(List<BrushPlacement> placements)
        {
            if (previewMaterial == null)
            {
                return;
            }

            foreach (KeyValuePair<Mesh, List<Matrix4x4>> batch in previewBatches)
            {
                batch.Value.Clear();
            }

            for (int i = 0; i < placements.Count; i++)
            {
                GameObject prefab = placements[i].prefab;
                if (prefab == null)
                {
                    continue;
                }

                Matrix4x4 placementMatrix = Matrix4x4.TRS(placements[i].position, placements[i].rotation, placements[i].scale);
                Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;

                if (!prefabMeshCache.TryGetValue(prefab, out MeshFilter[] filters))
                {
                    filters = prefab.GetComponentsInChildren<MeshFilter>();
                    prefabMeshCache[prefab] = filters;
                }

                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh mesh = filters[f].sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    Matrix4x4 world = placementMatrix * (rootInverse * filters[f].transform.localToWorldMatrix);
                    if (!previewBatches.TryGetValue(mesh, out List<Matrix4x4> matrices))
                    {
                        matrices = new List<Matrix4x4>();
                        previewBatches[mesh] = matrices;
                    }

                    matrices.Add(world);
                }
            }

            foreach (KeyValuePair<Mesh, List<Matrix4x4>> batch in previewBatches)
            {
                List<Matrix4x4> matrices = batch.Value;
                for (int i = 0; i < matrices.Count; i += 1023)
                {
                    int count = Mathf.Min(1023, matrices.Count - i);
                    Graphics.DrawMeshInstanced(batch.Key, 0, previewMaterial, matrices.GetRange(i, count).ToArray(), count, previewBlock);
                }
            }
        }

        private static int CombineSeed(int baseSeed, Vector3 center)
        {
            unchecked
            {
                int hash = baseSeed;
                hash = hash * 397 ^ Mathf.RoundToInt(center.x * 10f);
                hash = hash * 397 ^ Mathf.RoundToInt(center.y * 10f);
                hash = hash * 397 ^ Mathf.RoundToInt(center.z * 10f);
                return hash;
            }
        }

        private VisualElement BuildModeSection()
        {
            VisualElement section = new VisualElement();

            VisualElement modes = new VisualElement();
            modes.AddToClassList("pb-mode");
            Button paint = new Button { text = PrefabBrushUI.T("mode.paint") };
            Button erase = new Button { text = PrefabBrushUI.T("mode.erase") };
            paint.AddToClassList("pb-mode__button");
            erase.AddToClassList("pb-mode__button");
            erase.AddToClassList("pb-mode__button--erase");
            modes.Add(paint);
            modes.Add(erase);
            section.Add(modes);

            Label hint = PrefabBrushUI.Hint(string.Empty);
            section.Add(hint);

            Toggle eraseOnly = new Toggle(PrefabBrushUI.T("eraseOnly")) { value = settings.eraseOnlyPaletteModels }
                .WithTooltip(PrefabBrushUI.T("eraseOnly.tip"));
            eraseOnly.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Erase Only Palette Models");
                settings.eraseOnlyPaletteModels = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(eraseOnly);

            void Show()
            {
                paint.EnableInClassList("pb-mode__button--active", !settings.eraseMode);
                erase.EnableInClassList("pb-mode__button--active", settings.eraseMode);
                hint.text = PrefabBrushUI.T(settings.eraseMode ? "mode.eraseHint" : "mode.paintHint");
                eraseOnly.style.display = settings.eraseMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void SetErase(bool value)
            {
                if (settings.eraseMode == value)
                {
                    return;
                }

                Undo.RecordObject(settings, "Set Erase Mode");
                settings.eraseMode = value;
                EditorUtility.SetDirty(settings);
                Show();
            }

            paint.clicked += () => SetErase(false);
            erase.clicked += () => SetErase(true);
            Show();
            return section;
        }

        private VisualElement BuildBrushSection()
        {
            VisualElement card = PrefabBrushUI.Card(PrefabBrushUI.T("card.brush"));

            Slider radius = new Slider(PrefabBrushUI.T("radius"), 0.1f, 50f) { value = settings.brushRadius, showInputField = true };
            radius.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Radius");
                settings.brushRadius = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            card.Add(radius);

            SliderInt density = new SliderInt(PrefabBrushUI.T("density"), 1, 50) { value = settings.brushDensity, showInputField = true };
            density.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Density");
                settings.brushDensity = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            card.Add(density);

            Toggle paintOnDrag = new Toggle(PrefabBrushUI.T("paintOnDrag")) { value = settings.paintOnDrag };
            Slider dragSpacing = new Slider(PrefabBrushUI.T("dragSpacing"), 0.05f, 10f) { value = settings.dragSpacing, showInputField = true };
            paintOnDrag.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Paint On Drag");
                settings.paintOnDrag = evt.newValue;
                EditorUtility.SetDirty(settings);
                dragSpacing.SetEnabled(evt.newValue);
            });
            dragSpacing.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Drag Spacing");
                settings.dragSpacing = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            dragSpacing.SetEnabled(settings.paintOnDrag);
            card.Add(paintOnDrag);
            card.Add(dragSpacing);

            IntegerField seedField = new IntegerField(PrefabBrushUI.T("seed")) { value = settings.seed };
            seedField.style.flexGrow = 1;
            seedField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Seed");
                settings.seed = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            Button randomize = PrefabBrushUI.Button(PrefabBrushUI.T("randomize"), () =>
            {
                Undo.RecordObject(settings, "Randomize Seed");
                settings.seed = UnityEngine.Random.Range(0, 99999);
                EditorUtility.SetDirty(settings);
                seedField.SetValueWithoutNotify(settings.seed);
            });
            randomize.style.marginLeft = 4f;
            randomize.style.marginRight = 0f;
            card.Add(PrefabBrushUI.Row(seedField, randomize));

            return card;
        }

        private VisualElement BuildPresetSection()
        {
            Foldout foldout = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.preset"), false);

            ObjectField preset = new ObjectField(PrefabBrushUI.T("preset"))
            {
                objectType = typeof(PrefabBrushPreset),
                value = loadedPreset
            };
            preset.RegisterValueChangedCallback(evt => loadedPreset = evt.newValue as PrefabBrushPreset);
            foldout.Add(preset);

            Button load = PrefabBrushUI.Button(PrefabBrushUI.T("preset.load"), () =>
            {
                if (loadedPreset == null)
                {
                    Debug.LogWarning("[WorldBuilder] Assign a preset first.");
                    return;
                }

                Undo.RecordObject(settings, "Load Brush Preset");
                loadedPreset.ApplyTo(settings);
                palette?.Refresh();
                EditorUtility.SetDirty(settings);
                UndoHistory.Push("Load Brush Preset");
            }, "grow");

            Button save = PrefabBrushUI.Button(PrefabBrushUI.T("preset.save"), () =>
            {
                if (loadedPreset == null)
                {
                    loadedPreset = ScriptableObject.CreateInstance<PrefabBrushPreset>();
                    string path = EditorUtility.SaveFilePanelInProject(
                        "Save Brush Preset", "PrefabBrushPreset", "asset",
                        "Choose where to store the brush preset.");
                    if (string.IsNullOrEmpty(path))
                    {
                        UnityEngine.Object.DestroyImmediate(loadedPreset);
                        loadedPreset = null;
                        return;
                    }

                    AssetDatabase.CreateAsset(loadedPreset, path);
                    preset.SetValueWithoutNotify(loadedPreset);
                }
                else
                {
                    Undo.RecordObject(loadedPreset, "Save Brush Preset");
                }

                loadedPreset.CaptureFrom(settings);
                EditorUtility.SetDirty(loadedPreset);
                AssetDatabase.SaveAssets();
                UndoHistory.Push("Save Brush Preset");
            }, "grow");
            save.style.marginRight = 0f;

            VisualElement row = PrefabBrushUI.Row(load, save);
            row.style.marginTop = 4f;
            foldout.Add(row);
            return foldout;
        }

        private VisualElement BuildPlacementSection()
        {
            Foldout foldout = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.placement"), true);

            Toggle align = new Toggle(PrefabBrushUI.T("align")) { value = settings.alignToNormal };
            align.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Align To Normal");
                settings.alignToNormal = evt.newValue;
                EditorUtility.SetDirty(settings);
                palette?.Refresh();
            });
            foldout.Add(align);

            Toggle yaw = new Toggle(PrefabBrushUI.T("yaw")) { value = settings.randomYaw };
            yaw.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Random Yaw");
                settings.randomYaw = evt.newValue;
                EditorUtility.SetDirty(settings);
                palette?.Refresh();
            });
            foldout.Add(yaw);

            Vector2Field scaleRange = new Vector2Field(PrefabBrushUI.T("scaleRange")) { value = settings.scaleRange };
            scaleRange.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Scale Range");
                settings.scaleRange = evt.newValue;
                EditorUtility.SetDirty(settings);
                palette?.Refresh();
            });
            foldout.Add(scaleRange);

            FloatField chunkSize = new FloatField(PrefabBrushUI.T("chunkSize")) { value = settings.chunkSize };
            chunkSize.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Chunk Size");
                settings.chunkSize = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(chunkSize);

            ObjectField waterData = new ObjectField(PrefabBrushUI.T("waterData"))
            {
                objectType = typeof(WaterWorldRuntimeData),
                value = settings.waterData
            }.WithTooltip(PrefabBrushUI.T("waterData.tip"));
            waterData.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Water Data");
                settings.waterData = evt.newValue as WaterWorldRuntimeData;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(waterData);

            Toggle duplicate = new Toggle(PrefabBrushUI.T("duplicateSO")) { value = settings.duplicateSharedScriptableObjects }
                .WithTooltip(PrefabBrushUI.T("duplicateSO.tip"));
            duplicate.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Duplicate Shared SO");
                settings.duplicateSharedScriptableObjects = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(duplicate);

            return foldout;
        }

        private VisualElement BuildPrefabSection()
        {
            palette = new PrefabPaletteView(settings);
            return palette;
        }

        private VisualElement BuildEnvironmentSection()
        {
            Foldout section = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.environment"), false);

            for (int i = 0; i < environmentBrushes.Count; i++)
            {
                IEnvironmentBrush brush = environmentBrushes[i];
                Foldout foldout = new Foldout { text = brush.DisplayName, value = false };
                foldout.Add(brush.BuildUI());
                EnvironmentUI.StyleBrushFoldout(foldout);
                section.Add(foldout);
            }

            return section;
        }

        private VisualElement BuildMaskSection()
        {
            Foldout foldout = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.mask"), false);
            BrushMask mask = settings.mask;

            FloatField minHeight = new FloatField(PrefabBrushUI.T("minHeight")) { value = mask.minHeight };
            FloatField maxHeight = new FloatField(PrefabBrushUI.T("maxHeight")) { value = mask.maxHeight };
            Slider maxSlope = new Slider(PrefabBrushUI.T("maxSlope"), 0f, 90f) { value = mask.maxSlopeAngle, showInputField = true };
            EnumField allowedBiome = new EnumField(PrefabBrushUI.T("allowedBiome"), mask.allowedBiome);

            Toggle useHeight = new Toggle(PrefabBrushUI.T("useHeight")) { value = mask.useHeightMask };
            useHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Height Mask");
                mask.useHeightMask = evt.newValue;
                EditorUtility.SetDirty(settings);
                minHeight.SetEnabled(evt.newValue);
                maxHeight.SetEnabled(evt.newValue);
            });
            minHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Min Height");
                mask.minHeight = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            maxHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Max Height");
                mask.maxHeight = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            minHeight.SetEnabled(mask.useHeightMask);
            maxHeight.SetEnabled(mask.useHeightMask);
            foldout.Add(useHeight);
            foldout.Add(minHeight);
            foldout.Add(maxHeight);

            Toggle useSlope = new Toggle(PrefabBrushUI.T("useSlope")) { value = mask.useSlopeMask };
            useSlope.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Slope Mask");
                mask.useSlopeMask = evt.newValue;
                EditorUtility.SetDirty(settings);
                maxSlope.SetEnabled(evt.newValue);
            });
            maxSlope.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Max Slope");
                mask.maxSlopeAngle = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            maxSlope.SetEnabled(mask.useSlopeMask);
            useSlope.style.marginTop = 6f;
            foldout.Add(useSlope);
            foldout.Add(maxSlope);

            Toggle useBiome = new Toggle(PrefabBrushUI.T("useBiome")) { value = mask.useBiomeMask };
            useBiome.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Biome Mask");
                mask.useBiomeMask = evt.newValue;
                EditorUtility.SetDirty(settings);
                allowedBiome.SetEnabled(evt.newValue);
            });
            allowedBiome.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Allowed Biome");
                mask.allowedBiome = (WorldBuilder.Runtime.Data.BiomeType)evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            allowedBiome.SetEnabled(mask.useBiomeMask);
            useBiome.style.marginTop = 6f;
            foldout.Add(useBiome);
            foldout.Add(allowedBiome);

            return foldout;
        }

        private VisualElement BuildModifierGraphSection()
        {
            Foldout foldout = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.modifier"), false);

            ObjectField graphField = new ObjectField(PrefabBrushUI.T("modifierGraph"))
            {
                objectType = typeof(ModifierGraph),
                value = settings.modifierGraph
            };
            graphField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Modifier Graph");
                settings.modifierGraph = evt.newValue as ModifierGraph;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(graphField);

            Button open = PrefabBrushUI.Button(PrefabBrushUI.T("openGraph"), () => ModifierGraphWindow.Open(settings.modifierGraph), "grow");
            open.style.marginTop = 4f;
            open.style.marginRight = 0f;
            foldout.Add(PrefabBrushUI.Row(open));

            return foldout;
        }

        private VisualElement BuildStrokeSection()
        {
            Foldout foldout = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.strokes"), false);
            string title = PrefabBrushUI.T("card.strokes");
            foldout.schedule.Execute(() =>
                foldout.text = title + "  ·  " + PrefabBrushUI.T("strokes.meta", settings.strokes.Count)).Every(300);

            foldout.Add(PrefabBrushUI.Hint(PrefabBrushUI.T("strokes.hint")));

            Button clear = PrefabBrushUI.Button(PrefabBrushUI.T("strokes.clear"), () =>
            {
                Undo.RecordObject(settings, "Clear Strokes");
                settings.strokes.Clear();
                EditorUtility.SetDirty(settings);
            }, "danger");
            clear.style.marginRight = 0f;

            foldout.Add(PrefabBrushUI.Row(
                PrefabBrushUI.Button(PrefabBrushUI.T("strokes.replay"), Replay, "primary"),
                PrefabBrushUI.Button(PrefabBrushUI.T("strokes.export"), ExportStrokes),
                PrefabBrushUI.Button(PrefabBrushUI.T("strokes.import"), ImportStrokes),
                clear));
            return foldout;
        }

        private void ExportStrokes()
        {
            string path = EditorUtility.SaveFilePanel("Export Strokes", Application.dataPath, "brush_strokes", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            StrokeCollection collection = new StrokeCollection { strokes = settings.strokes };
            File.WriteAllText(path, JsonUtility.ToJson(collection, true));
        }

        private void ImportStrokes()
        {
            string path = EditorUtility.OpenFilePanel("Import Strokes", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            StrokeCollection collection = JsonUtility.FromJson<StrokeCollection>(File.ReadAllText(path));
            if (collection == null || collection.strokes == null)
            {
                return;
            }

            Undo.RecordObject(settings, "Import Strokes");
            settings.strokes = collection.strokes;
            EditorUtility.SetDirty(settings);

            Replay();
        }

        [Serializable]
        private sealed class StrokeCollection
        {
            public List<BrushStroke> strokes = new List<BrushStroke>();
        }
    }
}
