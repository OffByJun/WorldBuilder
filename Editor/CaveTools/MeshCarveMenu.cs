using System;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.CaveTools
{
    /// <summary>
    /// Authoring bridge: carves the voxel store along the surface of a selected mesh —
    /// the Unity-side twin of the Blender Cave Network Builder. Select an object with a
    /// MeshFilter (imported cave network, custom tunnel), then run the menu.
    /// </summary>
    public static class MeshCarveMenu
    {
        private const float ChunkSize = 128f;

        [MenuItem("WorldBuilder/Caves/Carve Store With Selected Mesh")]
        public static void CarveWithSelectedMesh()
        {
            GameObject selection = UnityEditor.Selection.activeGameObject;
            if (selection == null)
            {
                Debug.LogWarning("[WorldBuilder] Select an object with a MeshFilter first.");
                return;
            }

            MeshFilter filter = selection.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning($"[WorldBuilder] '{selection.name}' has no MeshFilter/mesh.");
                return;
            }

            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset found.");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            UnityEditor.Undo.RecordObject(store, "Carve With Mesh");
            int changed = MeshCarver.CarveAlongSurface(store, ChunkSize, filter.sharedMesh,
                filter.transform.localToWorldMatrix,
                thickness: 3f, yRange: new Vector2(-96f, 96f));
            UnityEditor.EditorUtility.SetDirty(store);

            // Re-mesh any registered runtime chunks so the result is visible immediately.
            int remeshed = 0;
            foreach (Vector3Int coord in TerrainDeformer.EditedChunks)
                if (TerrainDeformer.Remesh(store, ChunkSize, store.Resolution, coord)) remeshed++;

            watch.Stop();
            Debug.Log($"[WorldBuilder] Carved {changed:N0} voxels along '{filter.sharedMesh.name}' " +
                      $"({remeshed} chunk(s) remeshed) in {watch.Elapsed.TotalSeconds:F1}s.");
        }
    }
}
