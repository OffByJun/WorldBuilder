using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.FBXImportTool
{
    [Serializable]
    public class FBXImportPreset
    {
        public string label;
        public float scaleFactor = 1f;
        public bool importNormals = true;
        public bool importTangents = true;
        public bool generateCollider;
        public ModelImporterNormals normalMode = ModelImporterNormals.Import;
    }

    public sealed class FBXImportTool : IWorldBuilderTool
    {
        [Serializable]
        private sealed class PresetCollection
        {
            public List<FBXImportPreset> presets = new List<FBXImportPreset>();
        }

        private const string PrefKey = "WB_FBXImportPresets";

        private readonly List<FBXImportPreset> presets = new List<FBXImportPreset>();
        private readonly FBXImportPreset current = new FBXImportPreset { label = "Preset" };

        private DefaultAsset targetFolder;
        private ListView presetList;
        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.fbxImport");
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

            root.Add(InspectorHelp.Build(ToolName, "help.fbxImport"));

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

            FloatField scale = new FloatField("Scale Factor") { value = current.scaleFactor };
            scale.RegisterValueChangedCallback(evt => current.scaleFactor = evt.newValue);
            root.Add(scale);

            Toggle normals = new Toggle("Import Normals") { value = current.importNormals };
            normals.RegisterValueChangedCallback(evt => current.importNormals = evt.newValue);
            root.Add(normals);

            EnumField normalMode = new EnumField("Normal Mode", current.normalMode);
            normalMode.RegisterValueChangedCallback(evt => current.normalMode = (ModelImporterNormals)evt.newValue);
            root.Add(normalMode);

            Toggle tangents = new Toggle("Import Tangents") { value = current.importTangents };
            tangents.RegisterValueChangedCallback(evt => current.importTangents = evt.newValue);
            root.Add(tangents);

            Toggle collider = new Toggle("Generate Collider") { value = current.generateCollider };
            collider.RegisterValueChangedCallback(evt => current.generateCollider = evt.newValue);
            root.Add(collider);

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

            FBXImportPreset source = presets[index];
            current.label = source.label;
            current.scaleFactor = source.scaleFactor;
            current.importNormals = source.importNormals;
            current.importTangents = source.importTangents;
            current.generateCollider = source.generateCollider;
            current.normalMode = source.normalMode;
        }

        private void SavePreset()
        {
            presets.Add(new FBXImportPreset
            {
                label = string.IsNullOrEmpty(current.label) ? "Preset " + (presets.Count + 1) : current.label,
                scaleFactor = current.scaleFactor,
                importNormals = current.importNormals,
                importTangents = current.importTangents,
                generateCollider = current.generateCollider,
                normalMode = current.normalMode
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
            int applied = FBXImportService.Apply(current, folderPath);
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
