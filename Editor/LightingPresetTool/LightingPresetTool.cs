using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.LightingPresetTool
{
    public sealed class LightingPresetTool : IWorldBuilderTool
    {
        private const string Folder = "Assets/WorldBuilder";

        private readonly List<LightingPreset> presets = new List<LightingPreset>();

        private string pendingLabel = "Lighting";
        private ListView listView;

        public string ToolName => WorldBuilderLocalization.Get("tool.lightingPreset");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            ReloadPresets();
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.lightingPreset"));

            TextField labelField = new TextField("Label") { value = pendingLabel };
            labelField.RegisterValueChangedCallback(evt => pendingLabel = evt.newValue);
            root.Add(labelField);

            Button save = new Button(() => Save(labelField)) { text = "Save" };
            root.Add(save);

            listView = new ListView(presets, 24, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single
            };
            listView.style.minHeight = 220f;
            root.Add(listView);

            ReloadPresets();
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

            Button apply = new Button { text = "Apply", name = "apply" };
            row.Add(apply);

            Button delete = new Button { text = "Delete", name = "delete" };
            row.Add(delete);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            LightingPreset preset = presets[index];
            element.Q<Label>("label").text = preset != null ? preset.Label + "  (" + preset.name + ")" : string.Empty;
            element.Q<Button>("apply").clickable = new Clickable(() => Apply(index));
            element.Q<Button>("delete").clickable = new Clickable(() => Delete(index));
        }

        private void Save(TextField labelField)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "WorldBuilder");
            }

            LightingPreset preset = ScriptableObject.CreateInstance<LightingPreset>();
            preset.Label = string.IsNullOrEmpty(pendingLabel) ? "Lighting" : pendingLabel;
            preset.AmbientColor = RenderSettings.ambientLight;
            preset.AmbientIntensity = RenderSettings.ambientIntensity;
            preset.FogColor = RenderSettings.fogColor;
            preset.FogEnabled = RenderSettings.fog;
            preset.FogDensity = RenderSettings.fogDensity;
            preset.FogMode = RenderSettings.fogMode;

            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/LightingPreset_" + preset.Label + ".asset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();

            ReloadPresets();
        }

        private void Apply(int index)
        {
            if (index < 0 || index >= presets.Count || presets[index] == null)
            {
                return;
            }

            LightingPreset preset = presets[index];
            RenderSettings.ambientLight = preset.AmbientColor;
            RenderSettings.ambientIntensity = preset.AmbientIntensity;
            RenderSettings.fog = preset.FogEnabled;
            RenderSettings.fogColor = preset.FogColor;
            RenderSettings.fogDensity = preset.FogDensity;
            RenderSettings.fogMode = preset.FogMode;
        }

        private void Delete(int index)
        {
            if (index < 0 || index >= presets.Count || presets[index] == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(presets[index]);
            AssetDatabase.DeleteAsset(path);
            ReloadPresets();
        }

        private void ReloadPresets()
        {
            presets.Clear();

            string[] guids = AssetDatabase.FindAssets("t:LightingPreset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                LightingPreset preset = AssetDatabase.LoadAssetAtPath<LightingPreset>(path);
                if (preset != null)
                {
                    presets.Add(preset);
                }
            }

            listView?.Rebuild();
        }
    }
}
