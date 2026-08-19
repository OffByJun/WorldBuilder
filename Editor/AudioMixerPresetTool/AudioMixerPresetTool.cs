using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.AudioMixerPresetTool
{
    public sealed class AudioMixerPresetTool : IWorldBuilderTool
    {
        private readonly List<string> parameterStrings = new List<string>();

        private AudioMixerPreset preset;
        private ListView parameterList;
        private Label statusLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.audioMixerPreset");
        public string Category => WorldBuilderCategory.Audio;

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

            root.Add(InspectorHelp.Build(ToolName, "help.audioMixerPreset"));

            ObjectField presetField = new ObjectField("Preset")
            {
                objectType = typeof(AudioMixerPreset),
                allowSceneObjects = false,
                value = preset
            };
            presetField.RegisterValueChangedCallback(evt =>
            {
                preset = evt.newValue as AudioMixerPreset;
                RefreshList();
            });
            root.Add(presetField);

            Button save = new Button(Save) { text = "Save" };
            root.Add(save);

            Button apply = new Button(Apply) { text = "Apply" };
            root.Add(apply);

            statusLabel = new Label(string.Empty);
            root.Add(statusLabel);

            parameterList = new ListView(parameterStrings, 18, () => new Label(), (e, i) => ((Label)e).text = parameterStrings[i])
            {
                selectionType = SelectionType.None
            };
            parameterList.style.minHeight = 160f;
            root.Add(parameterList);

            RefreshList();
            return root;
        }

        private void Save()
        {
            if (preset == null || preset.Mixer == null)
            {
                SetStatus("Preset or mixer missing.");
                return;
            }

            Undo.RecordObject(preset, "Save Mixer Preset");

            List<AudioMixerParamEntry> parameters = preset.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                if (preset.Mixer.GetFloat(parameters[i].paramName, out float value))
                {
                    AudioMixerParamEntry entry = parameters[i];
                    entry.value = value;
                    parameters[i] = entry;
                }
            }

            EditorUtility.SetDirty(preset);
            RefreshList();
            SetStatus("Saved " + parameters.Count + " parameters.");
        }

        private void Apply()
        {
            if (preset == null || preset.Mixer == null)
            {
                SetStatus("Preset or mixer missing.");
                return;
            }

            List<AudioMixerParamEntry> parameters = preset.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                preset.Mixer.SetFloat(parameters[i].paramName, parameters[i].value);
            }

            SetStatus("Applied " + parameters.Count + " parameters.");
        }

        private void RefreshList()
        {
            parameterStrings.Clear();

            if (preset != null)
            {
                List<AudioMixerParamEntry> parameters = preset.Parameters;
                for (int i = 0; i < parameters.Count; i++)
                {
                    parameterStrings.Add(parameters[i].paramName + " = " + parameters[i].value.ToString("F2"));
                }
            }

            parameterList?.Rebuild();
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }
        }
    }
}
