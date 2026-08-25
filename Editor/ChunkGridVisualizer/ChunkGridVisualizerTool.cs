using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.ChunkGridVisualizer
{
    public sealed class ChunkGridVisualizerTool : IWorldBuilderTool
    {
        private static readonly Color GridColor = Color.gray;
        private static readonly Color LoadedColor = Color.green;

        [SerializeField] private int chunkSize = 16;
        [SerializeField] private int viewRadius = 4;
        [SerializeField] private bool showLabels;
        [SerializeField] private bool showRegionBorders = true;
        [SerializeField] private int chunksPerRegion = 4;
        [SerializeField] private bool highlightCursorChunk = true;

        private readonly IChunkBiomeMap biomeMap;
        private readonly ChunkCoordCalculator calculator = new ChunkCoordCalculator();
        private readonly List<Vector3Int> loadedCells = new List<Vector3Int>();

        private Vector3[] gridSegments = Array.Empty<Vector3>();
        private Vector3[] loadedSegments = Array.Empty<Vector3>();
        private Vector3[] regionSegments = Array.Empty<Vector3>();
        private Vector3Int cachedCenter;
        private int cachedRadius = -1;
        private int cachedChunkSize = -1;
        private int cachedRegionSpan = -1;

        public ChunkGridVisualizerTool(IChunkBiomeMap biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.chunkGrid");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable() => cachedRadius = -1;

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.chunkGrid"));

            SliderInt size = new SliderInt("Chunk Size", 1, 128) { value = chunkSize };
            size.RegisterValueChangedCallback(evt =>
            {
                chunkSize = evt.newValue;
                cachedRadius = -1;
            });
            root.Add(size);

            SliderInt radius = new SliderInt("View Radius", 1, 32) { value = viewRadius };
            radius.RegisterValueChangedCallback(evt =>
            {
                viewRadius = evt.newValue;
                cachedRadius = -1;
            });
            root.Add(radius);

            Toggle labels = new Toggle("Show Chunk Labels") { value = showLabels };
            labels.RegisterValueChangedCallback(evt => showLabels = evt.newValue);
            root.Add(labels);

            Toggle regions = new Toggle("Show Region Borders") { value = showRegionBorders };
            regions.RegisterValueChangedCallback(evt => showRegionBorders = evt.newValue);
            root.Add(regions);

            SliderInt perRegion = new SliderInt("Chunks Per Region", 1, 32) { value = chunksPerRegion };
            perRegion.RegisterValueChangedCallback(evt => chunksPerRegion = evt.newValue);
            root.Add(perRegion);

            Toggle cursor = new Toggle("Highlight Cursor Chunk") { value = highlightCursorChunk };
            cursor.RegisterValueChangedCallback(evt => highlightCursorChunk = evt.newValue);
            root.Add(cursor);

            return root;
        }

        public void OnSceneGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Vector3Int center = calculator.ToChunkCoord(view.pivot, chunkSize);
            RebuildGrid(center);

            Handles.color = GridColor;
            Handles.DrawLines(gridSegments);

            if (showRegionBorders && chunksPerRegion > 0)
            {
                RebuildRegionBorders(center);
                Handles.color = new Color(1f, 0.6f, 0.1f, 0.9f);
                Handles.DrawLines(regionSegments);
            }

            int loaded = CollectLoadedCells(center);
            if (loaded > 0)
            {
                Handles.color = LoadedColor;
                Handles.DrawLines(loadedSegments);
            }

            if (showLabels) DrawChunkLabels(center);
            if (highlightCursorChunk) HighlightCursorChunk();
        }

        private void DrawChunkLabels(Vector3Int center)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.65f);
            for (int x = center.x - viewRadius; x <= center.x + viewRadius; x++)
            {
                for (int z = center.z - viewRadius; z <= center.z + viewRadius; z++)
                {
                    Vector3 origin = new Vector3(x * (float)chunkSize, 0f, z * (float)chunkSize);
                    Handles.Label(origin + new Vector3(chunkSize * 0.08f, 0f, chunkSize * 0.92f), $"{x},{z}");
                }
            }
        }

        private void HighlightCursorChunk()
        {
            Event e = Event.current;
            Camera camera = Camera.current;
            if (e == null || camera == null) return;

            Ray ray = camera.ScreenPointToRay(EditorGUIUtility.GUIToScreenPoint(e.mousePosition));
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float enter)) return;

            Vector3 point = ray.GetPoint(enter);
            Vector3Int coord = calculator.ToChunkCoord(point, chunkSize);
            float x0 = coord.x * (float)chunkSize;
            float z0 = coord.z * (float)chunkSize;
            float size = chunkSize;

            Vector3 a = new Vector3(x0, 0f, z0);
            Vector3 b = new Vector3(x0 + size, 0f, z0);
            Vector3 c = new Vector3(x0 + size, 0f, z0 + size);
            Vector3 d = new Vector3(x0, 0f, z0 + size);

            Handles.color = Color.cyan;
            Handles.DrawLine(a, b);
            Handles.DrawLine(b, c);
            Handles.DrawLine(c, d);
            Handles.DrawLine(d, a);
            Handles.Label(a + new Vector3(size * 0.5f, 0f, size), $"Chunk {coord.x}, {coord.z}");
        }

        private void RebuildRegionBorders(Vector3Int center)
        {
            int regionSpan = Mathf.Max(1, chunksPerRegion);
            if (cachedRegionSpan == regionSpan && cachedCenter == center && cachedRadius == viewRadius &&
                cachedChunkSize == chunkSize && regionSegments.Length > 0) return;
            cachedRegionSpan = regionSpan;

            List<Vector3> segments = new List<Vector3>();

            int minChunkX = (center.x - viewRadius);
            int maxChunkX = (center.x + viewRadius + 1);
            int minChunkZ = (center.z - viewRadius);
            int maxChunkZ = (center.z + viewRadius + 1);

            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                if (x % regionSpan != 0) continue;
                float worldX = x * (float)chunkSize;
                segments.Add(new Vector3(worldX, 0f, minChunkZ * (float)chunkSize));
                segments.Add(new Vector3(worldX, 0f, maxChunkZ * (float)chunkSize));
            }

            for (int z = minChunkZ; z <= maxChunkZ; z++)
            {
                if (z % regionSpan != 0) continue;
                float worldZ = z * (float)chunkSize;
                segments.Add(new Vector3(minChunkX * (float)chunkSize, 0f, worldZ));
                segments.Add(new Vector3(maxChunkX * (float)chunkSize, 0f, worldZ));
            }

            regionSegments = segments.ToArray();
        }

        private void RebuildGrid(Vector3Int center)
        {
            if (cachedRadius == viewRadius && cachedChunkSize == chunkSize && cachedCenter == center) return;
            cachedCenter = center;
            cachedRadius = viewRadius;
            cachedChunkSize = chunkSize;

            int lineCount = (viewRadius * 2 + 2) * 2;
            if (gridSegments.Length != lineCount * 2) gridSegments = new Vector3[lineCount * 2];

            float minX = (center.x - viewRadius) * (float)chunkSize;
            float maxX = (center.x + viewRadius + 1) * (float)chunkSize;
            float minZ = (center.z - viewRadius) * (float)chunkSize;
            float maxZ = (center.z + viewRadius + 1) * (float)chunkSize;

            int index = 0;
            for (int x = center.x - viewRadius; x <= center.x + viewRadius + 1; x++)
            {
                float worldX = x * (float)chunkSize;
                gridSegments[index++] = new Vector3(worldX, 0f, minZ);
                gridSegments[index++] = new Vector3(worldX, 0f, maxZ);
            }
            for (int z = center.z - viewRadius; z <= center.z + viewRadius + 1; z++)
            {
                float worldZ = z * (float)chunkSize;
                gridSegments[index++] = new Vector3(minX, 0f, worldZ);
                gridSegments[index++] = new Vector3(maxX, 0f, worldZ);
            }
        }

        private int CollectLoadedCells(Vector3Int center)
        {
            loadedCells.Clear();
            for (int x = center.x - viewRadius; x <= center.x + viewRadius; x++)
            {
                for (int z = center.z - viewRadius; z <= center.z + viewRadius; z++)
                {
                    Vector3Int coord = new Vector3Int(x, center.y, z);
                    if (biomeMap.TryGet(coord, out BiomeType _)) loadedCells.Add(coord);
                }
            }

            int vertexCount = loadedCells.Count * 8;
            if (vertexCount == 0) return 0;
            if (loadedSegments.Length != vertexCount) loadedSegments = new Vector3[vertexCount];

            int index = 0;
            for (int i = 0; i < loadedCells.Count; i++)
            {
                Vector3Int coord = loadedCells[i];
                float x0 = coord.x * (float)chunkSize;
                float z0 = coord.z * (float)chunkSize;
                float x1 = x0 + chunkSize;
                float z1 = z0 + chunkSize;
                Vector3 a = new Vector3(x0, 0f, z0);
                Vector3 b = new Vector3(x1, 0f, z0);
                Vector3 c = new Vector3(x1, 0f, z1);
                Vector3 d = new Vector3(x0, 0f, z1);
                loadedSegments[index++] = a;
                loadedSegments[index++] = b;
                loadedSegments[index++] = b;
                loadedSegments[index++] = c;
                loadedSegments[index++] = c;
                loadedSegments[index++] = d;
                loadedSegments[index++] = d;
                loadedSegments[index++] = a;
            }
            return loadedCells.Count;
        }
    }
}
