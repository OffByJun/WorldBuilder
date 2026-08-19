using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Editor.BlenderBridge
{
    [InitializeOnLoad]
    public static class WorldGridSceneOverlay
    {
        private const string EnabledKey = "WorldBuilder.WorldGridOverlay.Enabled";
        private const int Radius = 4;

        private static readonly Color RegionColor = new Color(1f, 0.75f, 0.15f, 0.9f);
        private static readonly Color ChunkColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly List<Vector3> ChunkBuffer = new List<Vector3>();
        private static readonly List<Vector3> RegionBuffer = new List<Vector3>();

        private static WorldGridSettings resolvedGrid;
        private static bool gridResolved;
        private static bool enabledResolved;
        private static bool enabled;

        private static Vector3[] chunkSegments = Array.Empty<Vector3>();
        private static Vector3[] regionSegments = Array.Empty<Vector3>();
        private static bool segmentsValid;
        private static ChunkCoord cachedCenter;
        private static float cachedChunkSize;
        private static int cachedChunksPerRegion;
        private static Vector3 cachedOrigin;

        static WorldGridSceneOverlay()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.projectChanged += InvalidateCache;
        }

        public static void InvalidateCache()
        {
            gridResolved = false;
            resolvedGrid = null;
            segmentsValid = false;
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Show Authoritative Grid")]
        private static void Toggle()
        {
            enabled = !EditorPrefs.GetBool(EnabledKey, true);
            enabledResolved = true;
            EditorPrefs.SetBool(EnabledKey, enabled);
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/WorldBuilder/Chunks/Show Authoritative Grid", true)]
        private static bool ToggleValidation()
        {
            Menu.SetChecked("Tools/WorldBuilder/Chunks/Show Authoritative Grid", IsEnabled());
            return true;
        }

        private static bool IsEnabled()
        {
            if (enabledResolved) return enabled;
            enabled = EditorPrefs.GetBool(EnabledKey, true);
            enabledResolved = true;
            return enabled;
        }

        private static void Draw(SceneView sceneView)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!IsEnabled()) return;
            WorldGridSettings settings = ResolveGrid();
            if (settings == null) return;

            WorldGrid grid = settings.CreateGrid();
            ChunkCoord center = grid.WorldToChunk(sceneView.pivot);
            RebuildSegments(grid, settings.ChunksPerRegion, center);

            if (chunkSegments.Length > 0)
            {
                Handles.color = ChunkColor;
                Handles.DrawLines(chunkSegments);
            }
            if (regionSegments.Length > 0)
            {
                Handles.color = RegionColor;
                Handles.DrawLines(regionSegments);
            }

            Vector3 origin = grid.ChunkToWorldOrigin(center);
            Handles.Label(origin + new Vector3(1f, 0f, 1f),
                $"{WorldCoordNaming.ChunkName(center)}\n{WorldCoordNaming.RegionName(grid.ChunkToRegion(center))}");
        }

        private static WorldGridSettings ResolveGrid()
        {
            if (gridResolved) return resolvedGrid;
            gridResolved = true;
            BlenderBridgeSettings bridge = ChunkManifestImporter.FindSettings(false);
            resolvedGrid = bridge != null ? bridge.WorldGrid : FindGrid();
            segmentsValid = false;
            return resolvedGrid;
        }

        private static void RebuildSegments(WorldGrid grid, int chunksPerRegion, ChunkCoord center)
        {
            if (segmentsValid && cachedCenter.Equals(center) && cachedChunkSize == grid.ChunkSize &&
                cachedChunksPerRegion == chunksPerRegion && cachedOrigin == grid.Origin) return;
            cachedCenter = center;
            cachedChunkSize = grid.ChunkSize;
            cachedChunksPerRegion = chunksPerRegion;
            cachedOrigin = grid.Origin;
            segmentsValid = true;

            ChunkBuffer.Clear();
            RegionBuffer.Clear();
            Vector3 minCorner = grid.ChunkToWorldOrigin(new ChunkCoord(center.X - Radius, center.Z - Radius));
            Vector3 maxCorner = grid.ChunkToWorldOrigin(new ChunkCoord(center.X + Radius + 1, center.Z + Radius + 1));

            for (int x = center.X - Radius; x <= center.X + Radius + 1; x++)
            {
                float worldX = grid.ChunkToWorldOrigin(new ChunkCoord(x, center.Z)).x;
                List<Vector3> target = IsRegionSeam(x, chunksPerRegion) ? RegionBuffer : ChunkBuffer;
                target.Add(new Vector3(worldX, minCorner.y, minCorner.z));
                target.Add(new Vector3(worldX, minCorner.y, maxCorner.z));
            }
            for (int z = center.Z - Radius; z <= center.Z + Radius + 1; z++)
            {
                float worldZ = grid.ChunkToWorldOrigin(new ChunkCoord(center.X, z)).z;
                List<Vector3> target = IsRegionSeam(z, chunksPerRegion) ? RegionBuffer : ChunkBuffer;
                target.Add(new Vector3(minCorner.x, minCorner.y, worldZ));
                target.Add(new Vector3(maxCorner.x, minCorner.y, worldZ));
            }

            chunkSegments = ResizeTo(chunkSegments, ChunkBuffer);
            regionSegments = ResizeTo(regionSegments, RegionBuffer);
        }

        private static bool IsRegionSeam(int coordinate, int chunksPerRegion)
        {
            return WorldGrid.FloorDiv(coordinate, chunksPerRegion) !=
                   WorldGrid.FloorDiv(coordinate - 1, chunksPerRegion);
        }

        private static Vector3[] ResizeTo(Vector3[] target, List<Vector3> source)
        {
            if (target.Length != source.Count) target = new Vector3[source.Count];
            source.CopyTo(target);
            return target;
        }

        private static WorldGridSettings FindGrid()
        {
            string[] guids = AssetDatabase.FindAssets("t:WorldGridSettings");
            return guids.Length == 1
                ? AssetDatabase.LoadAssetAtPath<WorldGridSettings>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }
    }
}
