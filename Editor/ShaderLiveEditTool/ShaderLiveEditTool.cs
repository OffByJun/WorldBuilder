using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.ShaderLiveEditTool
{
    public sealed class ShaderLiveEditTool : IWorldBuilderTool
    {
        private readonly Dictionary<string, float> originalFloats = new Dictionary<string, float>();
        private readonly Dictionary<string, Color> originalColors = new Dictionary<string, Color>();
        private readonly Dictionary<string, Vector4> originalVectors = new Dictionary<string, Vector4>();
        private readonly Dictionary<string, Texture> originalTextures = new Dictionary<string, Texture>();

        private Material target;
        private VisualElement propertyContainer;

        public string ToolName => WorldBuilderLocalization.Get("tool.shaderLiveEdit");
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

            root.Add(InspectorHelp.Build(ToolName, "help.shaderLiveEdit"));

            ObjectField materialField = new ObjectField("Material")
            {
                objectType = typeof(Material),
                allowSceneObjects = false,
                value = target
            };
            materialField.RegisterValueChangedCallback(evt =>
            {
                target = evt.newValue as Material;
                CaptureOriginals();
                RebuildProperties();
            });
            root.Add(materialField);

            Button reset = new Button(ResetValues) { text = "Reset" };
            root.Add(reset);

            propertyContainer = new VisualElement();
            root.Add(propertyContainer);

            RebuildProperties();
            return root;
        }

        private void CaptureOriginals()
        {
            originalFloats.Clear();
            originalColors.Clear();
            originalVectors.Clear();
            originalTextures.Clear();

            if (target == null || target.shader == null)
            {
                return;
            }

            Shader shader = target.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        originalColors[name] = target.GetColor(name);
                        break;
                    case ShaderPropertyType.Vector:
                        originalVectors[name] = target.GetVector(name);
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        originalFloats[name] = target.GetFloat(name);
                        break;
                    case ShaderPropertyType.Texture:
                        originalTextures[name] = target.GetTexture(name);
                        break;
                }
            }
        }

        private void RebuildProperties()
        {
            if (propertyContainer == null)
            {
                return;
            }

            propertyContainer.Clear();

            if (target == null || target.shader == null)
            {
                return;
            }

            Shader shader = target.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                AddPropertyField(shader, i);
            }
        }

        private void AddPropertyField(Shader shader, int index)
        {
            string name = shader.GetPropertyName(index);
            string label = shader.GetPropertyDescription(index);

            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                {
                    ColorField field = new ColorField(label) { value = target.GetColor(name) };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(target, "Shader Live Edit");
                        target.SetColor(name, evt.newValue);
                    });
                    propertyContainer.Add(field);
                    break;
                }
                case ShaderPropertyType.Vector:
                {
                    Vector4Field field = new Vector4Field(label) { value = target.GetVector(name) };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(target, "Shader Live Edit");
                        target.SetVector(name, evt.newValue);
                    });
                    propertyContainer.Add(field);
                    break;
                }
                case ShaderPropertyType.Range:
                {
                    Vector2 limits = shader.GetPropertyRangeLimits(index);
                    Slider field = new Slider(label, limits.x, limits.y) { value = target.GetFloat(name) };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(target, "Shader Live Edit");
                        target.SetFloat(name, evt.newValue);
                    });
                    propertyContainer.Add(field);
                    break;
                }
                case ShaderPropertyType.Float:
                {
                    float current = target.GetFloat(name);
                    float high = Mathf.Max(1f, Mathf.Abs(current) * 2f);
                    Slider field = new Slider(label, 0f, high) { value = current };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(target, "Shader Live Edit");
                        target.SetFloat(name, evt.newValue);
                    });
                    propertyContainer.Add(field);
                    break;
                }
                case ShaderPropertyType.Texture:
                {
                    ObjectField field = new ObjectField(label)
                    {
                        objectType = typeof(Texture),
                        allowSceneObjects = false,
                        value = target.GetTexture(name)
                    };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(target, "Shader Live Edit");
                        target.SetTexture(name, evt.newValue as Texture);
                    });
                    propertyContainer.Add(field);
                    break;
                }
            }
        }

        private void ResetValues()
        {
            if (target == null)
            {
                return;
            }

            Undo.RecordObject(target, "Shader Live Edit Reset");

            foreach (KeyValuePair<string, float> pair in originalFloats)
            {
                target.SetFloat(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, Color> pair in originalColors)
            {
                target.SetColor(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, Vector4> pair in originalVectors)
            {
                target.SetVector(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, Texture> pair in originalTextures)
            {
                target.SetTexture(pair.Key, pair.Value);
            }

            RebuildProperties();
        }
    }
}
