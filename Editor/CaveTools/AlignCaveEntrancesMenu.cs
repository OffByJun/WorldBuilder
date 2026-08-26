using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.CaveTools
{
    /// <summary>
    /// Reads CaveEntrance_NN empties exported by the Blender Cave Network Builder and
    /// carves matching walk-in shafts through the voxel store at those XZ positions.
    /// </summary>
    public static class AlignCaveEntrancesMenu
    {
        private const float ChunkSize = 128f;
        private const string MarkerPrefix = "CaveEntrance_";

        [MenuItem("WorldBuilder/Caves/Carve Entrances At Marker Objects")]
        public static void CarveAtMarkers()
        {
            List<Transform> markers = Object.FindObjectsOfType<Transform>(true)
                .Where(t => t.name.StartsWith(MarkerPrefix))
                .OrderBy(t => t.name)
                .ToList();

            if (markers.Count == 0)
            {
                Debug.LogWarning("[WorldBuilder] No 'CaveEntrance_*' objects in the scene. " +
                                 "Enable 'Export Entrance Markers' in the Blender cave builder first.");
                return;
            }

            TerrainShapeParams shape = FindAsset<TerrainShapeParams>();
            CaveShapeParams caves = FindAsset<CaveShapeParams>();
            if (shape == null || caves == null)
            {
                Debug.LogWarning("[WorldBuilder] Both a TerrainShapeParams and a CaveShapeParams " +
                                 "asset must exist in the project to rebuild surface heights.");
                return;
            }

            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset found.");
                return;
            }

            UnityEditor.Undo.RecordObject(store, "Align Cave Entrances");
            int carved = 0;
            int placed = 0;
            var touchedColumns = new List<Vector2>();

            foreach (Transform marker in markers)
            {
                Vector2 worldXz = new Vector2(marker.position.x, marker.position.z);
                TerrainField.HeightMap heights = BuildLocalHeights(shape, worldXz);
                int changed = CaveField.CarveEntranceAt(store, heights, shape, caves,
                    ChunkSize, worldXz);
                if (changed > 0) placed++;
                carved += changed;
                touchedColumns.Add(worldXz);
            }
            EditorUtility.SetDirty(store);

            // Re-mesh every chunk column a shaft may have touched.
            int remeshed = 0;
            foreach (Vector2 column in touchedColumns)
            {
                int cx = Mathf.FloorToInt(column.x / ChunkSize);
                int cz = Mathf.FloorToInt(column.y / ChunkSize);
                for (int cy = -1; cy <= 3; cy++)
                {
                    var coord = new Vector3Int(cx, cy, cz);
                    if (TerrainChunkRenderer.Registry.TryGetValue(coord, out _) &&
                        TerrainDeformer.Remesh(store, ChunkSize, store.Resolution, coord))
                        remeshed++;
                }
            }

            Debug.Log($"[WorldBuilder] Entrances: {placed}/{markers.Count} carved " +
                      $"({carved:N0} voxels, {remeshed} chunk(s) remeshed).");
        }

        /// <summary>Tiny heightmap centred on one column — enough for protection-depth math.</summary>
        private static TerrainField.HeightMap BuildLocalHeights(TerrainShapeParams shape, Vector2 centre)
        {
            const float cellSize = 1f;
            const int size = 9; // ±4 m around the marker
            Vector2 origin = centre - new Vector2(size * cellSize * 0.5f, size * cellSize * 0.5f);
            return TerrainField.BuildHeightMap(shape, origin, size, cellSize);
        }

        private static T FindAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
