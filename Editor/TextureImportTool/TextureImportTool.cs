using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.TextureImportTool
{
    [Serializable]
    public class TextureImportPreset
    {
        public string label;
        public TextureImporterCompression compression = TextureImporterCompression.Compressed;
        public bool generateMipMaps = true;
        public int maxSize = 2048;
        public TextureImporterFormat format = TextureImporterFormat.Automatic;
        public bool isNormalMap;
    }

    public sealed class TextureImportTool : IWorldBuilderTool
    {
        [Serializable]
        private sealed class PresetCollection
        {
            public List<TextureImportPreset> presets = new List<TextureImportPreset>();
        }

        private const string PrefKey = "WB_TextureImportPresets";

        private readonly List<TextureImportPreset> presets = new List<TextureImportPreset>();
        private readonly TextureImportPreset current = new TextureImportPreset { label = "Preset" };

        private DefaultAsset targetFolder;
        private ListView presetList;
        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.textureImport");
        public string Category => WorldBuilderCategory.Import;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            Load();
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.textureImport"));

            presetList = new ListView(presets, 22, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single
            };
            presetList.style.minHeight = 120f;
            presetList.selectionChanged += OnPresetSelected;
            root.Add(presetList);

            TextField label = new TextField("Label") { value = current.label };
            label.RegisterValueChangedCallback(evt => current.label = evt.newValue);
            root.Add(label);

            EnumField compression = new EnumField("Compression", current.compression);
            compression.RegisterValueChangedCallback(evt => current.compression = (TextureImporterCompression)evt.newValue);
            root.Add(compression);

            Toggle mipMaps = new Toggle("Generate MipMaps") { value = current.generateMipMaps };
            mipMaps.RegisterValueChangedCallback(evt => current.generateMipMaps = evt.newValue);
            root.Add(mipMaps);

            IntegerField maxSize = new IntegerField("Max Size") { value = current.maxSize };
            maxSize.RegisterValueChangedCallback(evt => current.maxSize = evt.newValue);
            root.Add(maxSize);

            EnumField format = new EnumField("Format", current.format);
            format.RegisterValueChangedCallback(evt => current.format = (TextureImporterFormat)evt.newValue);
            root.Add(format);

            Toggle normalMap = new Toggle("Is Normal Map") { value = current.isNormalMap };
            normalMap.RegisterValueChangedCallback(evt => current.isNormalMap = evt.newValue);
            root.Add(normalMap);

            Button save = new Button(SavePreset) { text = "Save Preset" };
            root.Add(save);

            ObjectField folder = new ObjectField("Target Folder")
            {
                objectType = typeof(DefaultAsset),
                allowSceneObjects = false
            };
            folder.RegisterValueChangedCallback(evt => targetFolder = evt.newValue as DefaultAsset);
            root.Add(folder);

            Button apply = new Button(Apply) { text = "Apply" };
            root.Add(apply);

            resultLabel = new Label(string.Empty);
            root.Add(resultLabel);

            return root;
        }

        private VisualElement MakeItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;

            Label label = new Label { name = "label" };
            label.style.flexGrow = 1f;
            row.Add(label);

            Button delete = new Button { text = "Delete", name = "delete" };
            row.Add(delete);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            element.Q<Label>("label").text = presets[index].label;
            element.Q<Button>("delete").clickable = new Clickable(() => Delete(index));
        }

        private void OnPresetSelected(IEnumerable<object> selection)
        {
            int index = presetList.selectedIndex;
            if (index < 0 || index >= presets.Count)
            {
                return;
            }

            TextureImportPreset source = presets[index];
            current.label = source.label;
            current.compression = source.compression;
            current.generateMipMaps = source.generateMipMaps;
            current.maxSize = source.maxSize;
            current.format = source.format;
            current.isNormalMap = source.isNormalMap;
        }

        private void SavePreset()
        {
            presets.Add(new TextureImportPreset
            {
                label = string.IsNullOrEmpty(current.label) ? "Preset " + (presets.Count + 1) : current.label,
                compression = current.compression,
                generateMipMaps = current.generateMipMaps,
                maxSize = current.maxSize,
                format = current.format,
                isNormalMap = current.isNormalMap
            });

            Persist();
            presetList?.Rebuild();
        }

        private void Delete(int index)
        {
            if (index < 0 || index >= presets.Count)
            {
                return;
            }

            presets.RemoveAt(index);
            Persist();
            presetList?.Rebuild();
        }

        private void Apply()
        {
            string folderPath = targetFolder != null ? AssetDatabase.GetAssetPath(targetFolder) : string.Empty;
            int applied = TextureImportService.Apply(current, folderPath);
            if (resultLabel != null)
            {
                resultLabel.text = "Applied: " + applied;
            }
        }

        private void Persist()
        {
            PresetCollection collection = new PresetCollection();
            collection.presets.AddRange(presets);
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(collection));
        }

        private void Load()
        {
            presets.Clear();

            string json = EditorPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            PresetCollection collection = JsonUtility.FromJson<PresetCollection>(json);
            if (collection?.presets != null)
            {
                presets.AddRange(collection.presets);
            }
        }
    }
}
