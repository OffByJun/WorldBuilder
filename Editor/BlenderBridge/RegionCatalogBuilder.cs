using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.BlenderBridge
{
    public static class RegionCatalogBuilder
    {
        public static void RebuildAll(BlenderBridgeSettings bridge)
        {
            if (bridge == null || bridge.WorldGrid == null) throw new ArgumentNullException(nameof(bridge));
            string chunksFolder = WorldRoot(bridge) + "/Chunks";
            SortedSet<RegionCoord> regions = new SortedSet<RegionCoord>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { chunksFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                ChunkRoot chunk = prefab != null ? prefab.GetComponent<ChunkRoot>() : null;
                if (chunk != null) regions.Add(bridge.WorldGrid.CreateGrid().ChunkToRegion(chunk.Coordinate));
            }
            foreach (RegionCoord region in regions) RebuildRegion(bridge, region, false);
            RebuildCatalog(bridge);
            AssetDatabase.SaveAssets();
        }

        public static void RebuildRegion(BlenderBridgeSettings bridge, RegionCoord region, bool rebuildCatalog = true)
        {
            WorldGrid grid = bridge.WorldGrid.CreateGrid();
            string worldRoot = WorldRoot(bridge);
            string chunksFolder = worldRoot + "/Chunks";
            string regionsFolder = worldRoot + "/Regions";
            ChunkImportPipeline.EnsureFolder(regionsFolder);
            List<GameObject> chunks = new List<GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { chunksFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                ChunkRoot chunk = prefab != null ? prefab.GetComponent<ChunkRoot>() : null;
                if (chunk != null && grid.ChunkToRegion(chunk.Coordinate) == region) chunks.Add(prefab);
            }
            chunks.Sort((left, right) => left.GetComponent<ChunkRoot>().Coordinate.CompareTo(right.GetComponent<ChunkRoot>().Coordinate));
            GameObject root = new GameObject(WorldCoordNaming.RegionName(region));
            try
            {
                root.AddComponent<RegionRoot>().Configure(region);
                for (int i = 0; i < chunks.Count; i++)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(chunks[i]) as GameObject;
                    if (instance == null) continue;
                    instance.transform.SetParent(root.transform, false);
                    instance.transform.localPosition = grid.ChunkToRegionLocalOrigin(instance.GetComponent<ChunkRoot>().Coordinate);
                }
                string path = regionsFolder + "/" + WorldCoordNaming.RegionName(region) + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            if (rebuildCatalog) RebuildCatalog(bridge);
        }

        private static void RebuildCatalog(BlenderBridgeSettings bridge)
        {
            string worldRoot = WorldRoot(bridge);
            string regionsFolder = worldRoot + "/Regions";
            string catalogPath = worldRoot + "/DirectRegionCatalog.asset";
            ChunkImportPipeline.EnsureFolder(worldRoot);
            List<DirectRegionReference> references = new List<DirectRegionReference>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { regionsFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                RegionRoot region = prefab != null ? prefab.GetComponent<RegionRoot>() : null;
                if (region == null) continue;
                references.Add(new DirectRegionReference
                {
                    regionX = region.Coordinate.X,
                    regionZ = region.Coordinate.Z,
                    prefab = prefab
                });
            }
            DirectRegionCatalog catalog = AssetDatabase.LoadAssetAtPath<DirectRegionCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DirectRegionCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.Configure(references);
            EditorUtility.SetDirty(catalog);
        }

        private static string WorldRoot(BlenderBridgeSettings bridge) =>
            bridge.GeneratedRoot + "/" + bridge.WorldGrid.WorldId;
    }
}
