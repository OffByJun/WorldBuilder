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

namespace WorldBuilder.Editor.PrefabBrush
{
    public sealed class PrefabBrushTool : IWorldBuilderTool, IRaycastConsumer
    {
        private const float SpatialCellSize = 4f;

        private readonly IBiomeMap biomeMap;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();
        private readonly Dictionary<Mesh, List<Matrix4x4>> previewBatches = new Dictionary<Mesh, List<Matrix4x4>>();

        private PrefabBrushSettings settings;
        private SpatialHash<GameObject> spatialHash;
        private Material previewMaterial;
        private MaterialPropertyBlock previewBlock;
        private List<IEnvironmentBrush> environmentBrushes;

        public PrefabBrushTool(IBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => "Prefab Brush Pro++";
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
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
            // root.Add(new Label("Prefab Brush PRO++"));
            
            root.Add(InspectorHelp.Build(ToolName, "help.prefabBrush"));
            root.Add(BuildSeedSection());
            root.Add(BuildBrushSection());
            root.Add(BuildPlacementSection());
            root.Add(BuildPrefabSection());
            root.Add(BuildEnvironmentSection());
            root.Add(BuildMaskSection());
            root.Add(BuildModifierGraphSection());
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
                GameObject prefab = PickWeighted(valid, totalWeight, rng);

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
                float scale = Mathf.Lerp(settings.scaleRange.x, settings.scaleRange.y, (float)rng.NextDouble());

                if (!PassesMask(position, normal))
                {
                    continue;
                }

                Quaternion rotation = settings.alignToNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
                if (settings.randomYaw)
                {
                    rotation *= Quaternion.AngleAxis(yaw, Vector3.up);
                }

                BrushContext context = new BrushContext
                {
                    position = position,
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

                context.position += ModifierGraphEvaluator.EvaluatePositionOffset(settings.modifierGraph, modifierContext);
                context.rotation *= Quaternion.Euler(ModifierGraphEvaluator.EvaluateRotation(settings.modifierGraph, modifierContext));
                context.scale = Vector3.Scale(context.scale, ModifierGraphEvaluator.EvaluateScale(settings.modifierGraph, modifierContext));

                placements.Add(new BrushPlacement
                {
                    prefab = prefab,
                    position = context.position,
                    rotation = context.rotation,
                    scale = context.scale
                });
            }

            return placements;
        }

        private GameObject PickWeighted(List<PrefabEntry> valid, float totalWeight, System.Random rng)
        {
            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0.0;
            for (int i = 0; i < valid.Count; i++)
            {
                cumulative += valid[i].weight;
                if (roll <= cumulative)
                {
                    return valid[i].prefab;
                }
            }

            return valid[valid.Count - 1].prefab;
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
            List<GameObject> hits = spatialHash.Query(center, settings.brushRadius);
            int group = Undo.GetCurrentGroup();

            for (int i = 0; i < hits.Count; i++)
            {
                GameObject instance = hits[i];
                if (instance == null)
                {
                    continue;
                }

                spatialHash.Remove(instance.transform.position, instance);
                Undo.DestroyObjectImmediate(instance);
            }

            Undo.SetCurrentGroupName("Erase Prefabs");
            Undo.CollapseUndoOperations(group);
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

                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>();
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

        private VisualElement BuildSeedSection()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            IntegerField seedField = new IntegerField("Seed") { value = settings.seed };
            seedField.style.flexGrow = 1;
            seedField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Seed");
                settings.seed = evt.newValue;
                EditorUtility.SetDirty(settings);
            });

            Button randomize = new Button(() =>
            {
                Undo.RecordObject(settings, "Randomize Seed");
                settings.seed = UnityEngine.Random.Range(0, 99999);
                EditorUtility.SetDirty(settings);
                seedField.SetValueWithoutNotify(settings.seed);
            }) { text = "Randomize" };

            row.Add(seedField);
            row.Add(randomize);
            return row;
        }

        private VisualElement BuildBrushSection()
        {
            VisualElement section = new VisualElement();

            Slider radius = new Slider("Radius", 0.1f, 50f) { value = settings.brushRadius };
            radius.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Radius");
                settings.brushRadius = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(radius);

            SliderInt density = new SliderInt("Density", 1, 50) { value = settings.brushDensity };
            density.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Density");
                settings.brushDensity = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(density);

            Toggle erase = new Toggle("Erase Mode") { value = settings.eraseMode };
            erase.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Erase Mode");
                settings.eraseMode = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(erase);

            return section;
        }

        private VisualElement BuildPlacementSection()
        {
            VisualElement section = new VisualElement();

            Toggle align = new Toggle("Align To Normal") { value = settings.alignToNormal };
            align.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Align To Normal");
                settings.alignToNormal = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(align);

            Toggle yaw = new Toggle("Random Yaw") { value = settings.randomYaw };
            yaw.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Random Yaw");
                settings.randomYaw = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(yaw);

            Vector2Field scaleRange = new Vector2Field("Scale Range") { value = settings.scaleRange };
            scaleRange.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Scale Range");
                settings.scaleRange = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(scaleRange);

            FloatField chunkSize = new FloatField("Chunk Size") { value = settings.chunkSize };
            chunkSize.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Chunk Size");
                settings.chunkSize = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(chunkSize);

            Toggle duplicate = new Toggle("Duplicate Shared SO") { value = settings.duplicateSharedScriptableObjects };
            duplicate.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Duplicate Shared SO");
                settings.duplicateSharedScriptableObjects = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            section.Add(duplicate);

            return section;
        }

        private VisualElement BuildPrefabSection()
        {
            VisualElement section = new VisualElement();
            section.Add(new Label("Prefabs"));

            VisualElement list = new VisualElement();
            section.Add(list);

            void Rebuild()
            {
                list.Clear();
                for (int i = 0; i < settings.prefabEntries.Count; i++)
                {
                    if (settings.prefabEntries[i].envType != EnvironmentType.None)
                    {
                        continue;
                    }

                    int index = i;

                    VisualElement row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;

                    ObjectField field = new ObjectField
                    {
                        objectType = typeof(GameObject),
                        value = settings.prefabEntries[index].prefab
                    };
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(settings, "Set Prefab");
                        PrefabEntry entry = settings.prefabEntries[index];
                        entry.prefab = evt.newValue as GameObject;
                        settings.prefabEntries[index] = entry;
                        EditorUtility.SetDirty(settings);
                    });

                    Slider weight = new Slider(0f, 1f) { value = settings.prefabEntries[index].weight };
                    weight.style.width = 120f;
                    weight.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(settings, "Set Weight");
                        PrefabEntry entry = settings.prefabEntries[index];
                        entry.weight = evt.newValue;
                        settings.prefabEntries[index] = entry;
                        EditorUtility.SetDirty(settings);
                    });

                    Button remove = new Button(() =>
                    {
                        Undo.RecordObject(settings, "Remove Prefab");
                        settings.prefabEntries.RemoveAt(index);
                        EditorUtility.SetDirty(settings);
                        Rebuild();
                    }) { text = "X" };

                    row.Add(field);
                    row.Add(weight);
                    row.Add(remove);
                    list.Add(row);
                }
            }

            Button add = new Button(() =>
            {
                Undo.RecordObject(settings, "Add Prefab");
                settings.prefabEntries.Add(new PrefabEntry { prefab = null, weight = 1f });
                EditorUtility.SetDirty(settings);
                Rebuild();
            }) { text = "Add" };

            section.Add(add);
            Rebuild();
            return section;
        }

        private VisualElement BuildEnvironmentSection()
        {
            Foldout section = new Foldout { text = "Environment Brush", value = false };
            Toggle sectionToggle = section.Q<Toggle>();
            if (sectionToggle != null)
            {
                sectionToggle.style.unityFontStyleAndWeight = FontStyle.Bold;
                sectionToggle.style.fontSize = 13f;
            }

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
            Foldout foldout = new Foldout { text = "Brush Mask", value = false };
            BrushMask mask = settings.mask;

            Toggle useHeight = new Toggle("Use Height Mask") { value = mask.useHeightMask };
            useHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Height Mask");
                mask.useHeightMask = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(useHeight);

            FloatField minHeight = new FloatField("Min Height") { value = mask.minHeight };
            minHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Min Height");
                mask.minHeight = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(minHeight);

            FloatField maxHeight = new FloatField("Max Height") { value = mask.maxHeight };
            maxHeight.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Max Height");
                mask.maxHeight = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(maxHeight);

            Toggle useSlope = new Toggle("Use Slope Mask") { value = mask.useSlopeMask };
            useSlope.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Slope Mask");
                mask.useSlopeMask = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(useSlope);

            Slider maxSlope = new Slider("Max Slope Angle", 0f, 90f) { value = mask.maxSlopeAngle };
            maxSlope.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Max Slope");
                mask.maxSlopeAngle = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(maxSlope);

            Toggle useBiome = new Toggle("Use Biome Mask") { value = mask.useBiomeMask };
            useBiome.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Biome Mask");
                mask.useBiomeMask = evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(useBiome);

            EnumField allowedBiome = new EnumField("Allowed Biome", mask.allowedBiome);
            allowedBiome.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, "Set Allowed Biome");
                mask.allowedBiome = (WorldBuilder.Runtime.Data.BiomeType)evt.newValue;
                EditorUtility.SetDirty(settings);
            });
            foldout.Add(allowedBiome);

            return foldout;
        }

        private VisualElement BuildModifierGraphSection()
        {
            VisualElement section = new VisualElement();

            ObjectField graphField = new ObjectField("Modifier Graph")
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
            section.Add(graphField);

            Button open = new Button(() => ModifierGraphWindow.Open(settings.modifierGraph)) { text = "Open Modifier Graph" };
            section.Add(open);

            return section;
        }

        private VisualElement BuildStrokeSection()
        {
            VisualElement section = new VisualElement();
            section.Add(new Label("Scatter Strokes"));

            Label count = new Label();
            section.Add(count);
            section.schedule.Execute(() => count.text = "Recorded Strokes: " + settings.strokes.Count).Every(200);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            row.Add(new Button(Replay) { text = "Replay" });
            row.Add(new Button(ExportStrokes) { text = "Export" });
            row.Add(new Button(ImportStrokes) { text = "Import" });
            row.Add(new Button(() =>
            {
                Undo.RecordObject(settings, "Clear Strokes");
                settings.strokes.Clear();
                EditorUtility.SetDirty(settings);
            }) { text = "Clear" });

            section.Add(row);
            return section;
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
