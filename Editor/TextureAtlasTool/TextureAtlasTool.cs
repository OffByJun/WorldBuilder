using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.TextureAtlasTool
{
    public enum AtlasSize
    {
        Size512 = 512,
        Size1024 = 1024,
        Size2048 = 2048,
        Size4096 = 4096
    }

    public sealed class TextureAtlasTool : IWorldBuilderTool
    {
        private readonly List<Texture2D> textures = new List<Texture2D>();

        private AtlasSize atlasSize = AtlasSize.Size1024;
        private string outputPath = "Assets/WorldBuilder/Atlas.png";
        private ListView resultList;
        private Label statusLabel;
        private readonly List<string> rectStrings = new List<string>();

        public string ToolName => WorldBuilderLocalization.Get("tool.textureAtlas");
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

            root.Add(InspectorHelp.Build(ToolName, "help.textureAtlas"));

            ListView textureList = new ListView(textures, 20, MakeTextureItem, BindTextureItem)
            {
                showAddRemoveFooter = true,
                reorderable = true,
                selectionType = SelectionType.Single,
                showBoundCollectionSize = false
            };
            textureList.style.minHeight = 140f;
            root.Add(textureList);

            EnumField sizeField = new EnumField("Atlas Size", atlasSize);
            sizeField.RegisterValueChangedCallback(evt => atlasSize = (AtlasSize)evt.newValue);
            root.Add(sizeField);

            TextField pathField = new TextField("Output Path") { value = outputPath };
            pathField.RegisterValueChangedCallback(evt => outputPath = evt.newValue);
            root.Add(pathField);

            Button generate = new Button(Generate) { text = "Generate" };
            root.Add(generate);

            statusLabel = new Label(string.Empty);
            root.Add(statusLabel);

            resultList = new ListView(rectStrings, 18, () => new Label(), (e, i) => ((Label)e).text = rectStrings[i])
            {
                selectionType = SelectionType.None
            };
            resultList.style.minHeight = 100f;
            root.Add(resultList);

            return root;
        }

        private VisualElement MakeTextureItem()
        {
            ObjectField field = new ObjectField
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false
            };
            field.RegisterValueChangedCallback(evt =>
            {
                int index = (int)field.userData;
                if (index >= 0 && index < textures.Count)
                {
                    textures[index] = evt.newValue as Texture2D;
                }
            });
            return field;
        }

        private void BindTextureItem(VisualElement element, int index)
        {
            ObjectField field = (ObjectField)element;
            field.userData = index;
            field.SetValueWithoutNotify(textures[index]);
        }

        private void Generate()
        {
            AtlasResult result = TextureAtlasService.Generate(textures, (int)atlasSize, outputPath);

            rectStrings.Clear();
            for (int i = 0; i < result.uvRects.Length; i++)
            {
                rectStrings.Add(i + ": " + result.uvRects[i]);
            }

            if (statusLabel != null)
            {
                statusLabel.text = result.message;
            }

            resultList?.Rebuild();
        }
    }
}
