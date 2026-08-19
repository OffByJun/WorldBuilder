using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.SpawnHeatmap
{
    public sealed class SpawnHeatmapTool : IWorldBuilderTool
    {
        [SerializeField] private float heatRadius = 8f;
        [SerializeField] private int maxDensity = 10;

        private readonly ISpawnerSceneQuery query;
        private readonly List<Vector3> cachedPositions = new List<Vector3>();
        private readonly List<Color> cachedColors = new List<Color>();
        private double lastSolve = double.NegativeInfinity;
        private int cachedCount = -1;
        private float cachedRadius;
        private int cachedMaxDensity;

        public SpawnHeatmapTool(ISpawnerSceneQuery query)
        {
            this.query = query;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.heatmap");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.heatmap"));

            Slider radius = new Slider("Heat Radius", 0.5f, 50f) { value = heatRadius };
            radius.RegisterValueChangedCallback(evt => heatRadius = evt.newValue);
            root.Add(radius);

            SliderInt max = new SliderInt("Max Density", 1, 50) { value = maxDensity };
            max.RegisterValueChangedCallback(evt => maxDensity = evt.newValue);
            root.Add(max);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!WorldBuilderSceneGUI.IsRepaint) return;

            Solve();
            for (int i = 0; i < cachedPositions.Count; i++)
            {
                Handles.color = cachedColors[i];
                Handles.DrawSolidDisc(cachedPositions[i], Vector3.up, heatRadius * 0.5f);
            }
        }

        private void Solve()
        {
            IReadOnlyList<ISpawner> spawners = query.GetAll();
            double now = EditorApplication.timeSinceStartup;
            if (cachedCount == spawners.Count && cachedRadius == heatRadius &&
                cachedMaxDensity == maxDensity && now - lastSolve < 1.0)
            {
                return;
            }
            cachedCount = spawners.Count;
            cachedRadius = heatRadius;
            cachedMaxDensity = maxDensity;
            lastSolve = now;

            cachedPositions.Clear();
            cachedColors.Clear();
            float sqrRadius = heatRadius * heatRadius;
            for (int i = 0; i < spawners.Count; i++)
            {
                Vector3 position = spawners[i].SpawnPosition;
                int neighbors = CountNeighbors(spawners, position, sqrRadius);
                float t = Mathf.Clamp01((float)neighbors / maxDensity);
                Color color = Color.Lerp(Color.blue, Color.red, t);
                color.a = 0.35f;
                cachedPositions.Add(position);
                cachedColors.Add(color);
            }
        }

        private static int CountNeighbors(IReadOnlyList<ISpawner> spawners, Vector3 position, float sqrRadius)
        {
            int count = 0;
            for (int i = 0; i < spawners.Count; i++)
            {
                if ((spawners[i].SpawnPosition - position).sqrMagnitude <= sqrRadius)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
