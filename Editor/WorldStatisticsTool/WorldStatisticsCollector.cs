using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace WorldBuilder.Editor.WorldStatisticsTool
{
    public static class WorldStatisticsCollector
    {
        public static StatisticsSnapshot Collect(WorldDataStore store)
        {
            StatisticsSnapshot snapshot = new StatisticsSnapshot();

            CollectRenderingStats(ref snapshot);
            CollectPhysicsStats(ref snapshot);
            CollectLightingStats(ref snapshot);
            CollectWorldDataStats(store, ref snapshot);

            snapshot.totalObjects = snapshot.meshCount + (store != null ? store.GetTotalCount() : 0);

            return snapshot;
        }

        private static void CollectRenderingStats(ref StatisticsSnapshot snapshot)
        {
            MeshFilter[] filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            HashSet<Mesh> uniqueMeshes = new HashSet<Mesh>();
            int triangles = 0;
            int vertices = 0;

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh m = filters[i].sharedMesh;
                if (m == null) continue;
                uniqueMeshes.Add(m);
                triangles += m.triangles.Length / 3;
                vertices += m.vertexCount;
            }

            long meshMemory = 0;
            foreach (Mesh m in uniqueMeshes)
                meshMemory += Profiler.GetRuntimeMemorySizeLong(m);

            snapshot.totalTriangles = triangles;
            snapshot.totalVertices = vertices;
            snapshot.meshCount = uniqueMeshes.Count;
            snapshot.meshMemoryBytes = meshMemory;

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            HashSet<Material> uniqueMaterials = new HashSet<Material>();
            HashSet<Texture> uniqueTextures = new HashSet<Texture>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = renderers[i].sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                {
                    Material mat = mats[j];
                    if (mat == null || !uniqueMaterials.Add(mat)) continue;
                    string[] texProps = mat.GetTexturePropertyNames();
                    for (int k = 0; k < texProps.Length; k++)
                    {
                        Texture tex = mat.GetTexture(texProps[k]);
                        if (tex != null) uniqueTextures.Add(tex);
                    }
                }
            }

            long texMemory = 0;
            foreach (Texture tex in uniqueTextures)
                texMemory += Profiler.GetRuntimeMemorySizeLong(tex);

            snapshot.materialCount = uniqueMaterials.Count;
            snapshot.textureCount = uniqueTextures.Count;
            snapshot.textureMemoryBytes = texMemory;
        }

        private static void CollectPhysicsStats(ref StatisticsSnapshot snapshot)
        {
            snapshot.rigidbodyCount = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            snapshot.colliderCount = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
        }

        private static void CollectLightingStats(ref StatisticsSnapshot snapshot)
        {
            snapshot.lightCount = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
            snapshot.reflectionProbeCount = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length;
        }

        private static void CollectWorldDataStats(WorldDataStore store, ref StatisticsSnapshot snapshot)
        {
            snapshot.worldDataCounts = new Dictionary<string, int>();
            if (store == null) return;

            foreach (KeyValuePair<Type, List<IWorldDataEntry>> kvp in store.GetAllCategories())
                snapshot.worldDataCounts[kvp.Key.Name] = kvp.Value.Count;
        }
    }
}
