using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.HeightBiomeMapper
{
    public sealed class HeightBiomeMapperTool : IWorldBuilderTool
    {
        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private AnimationCurve heightToBiome = AnimationCurve.Linear(0f, 0f, 100f, 4f);

        private readonly IChunkBiomeMap biomeMap;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();

        public HeightBiomeMapperTool(IChunkBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.heightBiome");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.heightBiome"));

            FloatField size = new FloatField("Chunk Size") { value = chunkSize };
            size.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            root.Add(size);

            CurveField curve = new CurveField("Height To Biome") { value = heightToBiome };
            curve.RegisterValueChangedCallback(evt => heightToBiome = evt.newValue);
            root.Add(curve);

            Label count = new Label();
            root.Add(count);

            Button apply = new Button(Apply) { text = WorldBuilderLocalization.Get("btn.apply") };
            root.Add(apply);

            root.schedule.Execute(() => count.text = "Assigned Chunks: " + biomeMap.Entries.Count).Every(200);

            return root;
        }

        public void OnSceneGUI()
        {
        }

        private void Apply()
        {
            List<Vector3Int> coords = new List<Vector3Int>(biomeMap.Entries.Keys);
            int biomeCount = Enum.GetValues(typeof(BiomeType)).Length;

            for (int i = 0; i < coords.Count; i++)
            {
                Vector3Int coord = coords[i];
                float altitude = calculator.ToWorldCenter(coord, chunkSize).y;
                int index = Mathf.Clamp(Mathf.RoundToInt(heightToBiome.Evaluate(altitude)), 0, biomeCount - 1);
                biomeMap.Set(coord, (BiomeType)index);
            }
        }
    }
}
