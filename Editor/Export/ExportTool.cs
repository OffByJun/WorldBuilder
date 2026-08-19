using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.Export
{
    public sealed class ExportTool : IWorldBuilderTool
    {
        [SerializeField] private float chunkSize = 16f;

        private readonly SceneDataCollector collector;
        private readonly ChunkSerializer serializer;

        public ExportTool(SceneDataCollector collector, ChunkSerializer serializer)
        {
            this.collector = collector;
            this.serializer = serializer;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.export");
        public string Category => WorldBuilderCategory.World;

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

            root.Add(InspectorHelp.Build(ToolName, "help.export"));

            FloatField size = new FloatField("Chunk Size") { value = chunkSize };
            size.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            root.Add(size);

            Button export = new Button(Export) { text = WorldBuilderLocalization.Get("btn.export") };
            root.Add(export);

            return root;
        }

        private void Export()
        {
            string path = WorldBinPath.Full;

            try
            {
                EditorUtility.DisplayProgressBar("WorldBuilder Export", "Collecting scene data...", 0.33f);
                ChunkData[] chunks = collector.Collect(chunkSize);

                EditorUtility.DisplayProgressBar("WorldBuilder Export", "Serializing chunks...", 0.66f);
                serializer.Write(path, chunks);

                EditorUtility.DisplayProgressBar("WorldBuilder Export", "Finalizing...", 1f);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            Debug.Log("WorldBuilder export complete: " + path);
        }
    }
}
