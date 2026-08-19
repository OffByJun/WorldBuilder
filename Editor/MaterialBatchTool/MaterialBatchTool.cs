using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.MaterialBatchTool
{
    public sealed class MaterialBatchTool : IWorldBuilderTool
    {
        private enum PropertyValueType
        {
            Float,
            Color,
            Vector
        }

        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private BiomeType presetBiome = BiomeType.Ocean;

        private readonly IChunkBiomeMap biomeMap;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();
        private readonly List<Material> presetMaterials = new List<Material>();

        private Material replaceSource;
        private Material replaceTarget;
        private bool replaceSceneWide = true;

        private Shader shaderSource;
        private Shader shaderTarget;

        private Shader propertyShader;
        private string propertyName = string.Empty;
        private PropertyValueType propertyValueType = PropertyValueType.Float;
        private float propertyFloat;
        private Color propertyColor = Color.white;
        private Vector4 propertyVector;

        public MaterialBatchTool(IChunkBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.materialBatch");
        public string Category => WorldBuilderCategory.Rendering;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.materialBatch"));

            TabView tabs = new TabView();
            tabs.Add(BuildMaterialTab());
            tabs.Add(BuildShaderTab());
            tabs.Add(BuildPropertyTab());
            tabs.Add(BuildPresetTab());
            root.Add(tabs);

            return root;
        }

        private Tab BuildMaterialTab()
        {
            Tab tab = new Tab("Material");

            ObjectField source = new ObjectField("Source")
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            source.RegisterValueChangedCallback(evt => replaceSource = evt.newValue as Material);
            tab.Add(source);

            ObjectField target = new ObjectField("Target")
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            target.RegisterValueChangedCallback(evt => replaceTarget = evt.newValue as Material);
            tab.Add(target);

            Toggle sceneWide = new Toggle("Scene Wide") { value = replaceSceneWide };
            sceneWide.RegisterValueChangedCallback(evt => replaceSceneWide = evt.newValue);
            tab.Add(sceneWide);

            Button replace = new Button(() => MaterialBatchService.ReplaceMaterial(replaceSource, replaceTarget, replaceSceneWide))
            {
                text = "Replace"
            };
            tab.Add(replace);

            return tab;
        }

        private Tab BuildShaderTab()
        {
            Tab tab = new Tab("Shader");

            ObjectField source = new ObjectField("Source")
            {
                objectType = typeof(Shader),
                allowSceneObjects = false
            };
            source.RegisterValueChangedCallback(evt => shaderSource = evt.newValue as Shader);
            tab.Add(source);

            ObjectField target = new ObjectField("Target")
            {
                objectType = typeof(Shader),
                allowSceneObjects = false
            };
            target.RegisterValueChangedCallback(evt => shaderTarget = evt.newValue as Shader);
            tab.Add(target);

            Button replace = new Button(() => MaterialBatchService.ReplaceShader(shaderSource, shaderTarget))
            {
                text = "Replace"
            };
            tab.Add(replace);

            return tab;
        }

        private Tab BuildPropertyTab()
        {
            Tab tab = new Tab("Property");

            ObjectField shaderField = new ObjectField("Target Shader")
            {
                objectType = typeof(Shader),
                allowSceneObjects = false
            };
            shaderField.RegisterValueChangedCallback(evt => propertyShader = evt.newValue as Shader);
            tab.Add(shaderField);

            TextField nameField = new TextField("Property Name") { value = propertyName };
            nameField.RegisterValueChangedCallback(evt => propertyName = evt.newValue);
            tab.Add(nameField);

            FloatField floatField = new FloatField("Float Value") { value = propertyFloat };
            floatField.RegisterValueChangedCallback(evt => propertyFloat = evt.newValue);

            ColorField colorField = new ColorField("Color Value") { value = propertyColor };
            colorField.RegisterValueChangedCallback(evt => propertyColor = evt.newValue);

            Vector4Field vectorField = new Vector4Field("Vector Value") { value = propertyVector };
            vectorField.RegisterValueChangedCallback(evt => propertyVector = evt.newValue);

            EnumField typeField = new EnumField("Value Type", propertyValueType);
            typeField.RegisterValueChangedCallback(evt =>
            {
                propertyValueType = (PropertyValueType)evt.newValue;
                UpdateValueVisibility(floatField, colorField, vectorField);
            });
            tab.Add(typeField);

            tab.Add(floatField);
            tab.Add(colorField);
            tab.Add(vectorField);
            UpdateValueVisibility(floatField, colorField, vectorField);

            Button apply = new Button(() => MaterialBatchService.ApplyProperty(propertyShader, propertyName, CurrentPropertyValue()))
            {
                text = "Apply"
            };
            tab.Add(apply);

            return tab;
        }

        private Tab BuildPresetTab()
        {
            Tab tab = new Tab("Biome Preset");

            EnumField biomeField = new EnumField("Biome", presetBiome);
            biomeField.RegisterValueChangedCallback(evt => presetBiome = (BiomeType)evt.newValue);
            tab.Add(biomeField);

            FloatField chunkSizeField = new FloatField("Chunk Size") { value = chunkSize };
            chunkSizeField.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            tab.Add(chunkSizeField);

            ListView list = new ListView(presetMaterials, 20, MakeMaterialItem, BindMaterialItem)
            {
                showAddRemoveFooter = true,
                reorderable = true,
                selectionType = SelectionType.Single
            };
            list.style.minHeight = 120f;
            tab.Add(list);

            Button save = new Button(SavePreset) { text = "Save Preset" };
            tab.Add(save);

            Button apply = new Button(ApplyPreset) { text = "Apply Preset" };
            tab.Add(apply);

            return tab;
        }

        private VisualElement MakeMaterialItem()
        {
            ObjectField field = new ObjectField
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            field.RegisterValueChangedCallback(evt =>
            {
                int index = (int)field.userData;
                if (index >= 0 && index < presetMaterials.Count)
                {
                    presetMaterials[index] = evt.newValue as Material;
                }
            });
            return field;
        }

        private void BindMaterialItem(VisualElement element, int index)
        {
            ObjectField field = (ObjectField)element;
            field.userData = index;
            field.SetValueWithoutNotify(presetMaterials[index]);
        }

        private void UpdateValueVisibility(FloatField floatField, ColorField colorField, Vector4Field vectorField)
        {
            floatField.style.display = propertyValueType == PropertyValueType.Float ? DisplayStyle.Flex : DisplayStyle.None;
            colorField.style.display = propertyValueType == PropertyValueType.Color ? DisplayStyle.Flex : DisplayStyle.None;
            vectorField.style.display = propertyValueType == PropertyValueType.Vector ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private object CurrentPropertyValue()
        {
            switch (propertyValueType)
            {
                case PropertyValueType.Color:
                    return propertyColor;
                case PropertyValueType.Vector:
                    return propertyVector;
                default:
                    return propertyFloat;
            }
        }

        private void SavePreset()
        {
            const string folder = "Assets/WorldBuilder";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "WorldBuilder");
            }

            BiomeMaterialPreset preset = ScriptableObject.CreateInstance<BiomeMaterialPreset>();
            preset.Biome = presetBiome;
            preset.Materials.AddRange(presetMaterials);

            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/BiomeMaterialPreset_" + presetBiome + ".asset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
        }

        private void ApplyPreset()
        {
            List<Renderer> targets = new List<Renderer>();
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            for (int i = 0; i < renderers.Length; i++)
            {
                Vector3Int coord = calculator.ToChunkCoord(renderers[i].transform.position, chunkSize);
                if (biomeMap.TryGet(coord, out BiomeType biome) && biome == presetBiome)
                {
                    targets.Add(renderers[i]);
                }
            }

            MaterialBatchService.AssignMaterials(targets, presetMaterials);
        }
    }
}
