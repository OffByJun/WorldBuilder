using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.CaveTools
{
    /// <summary>
    /// Scans cavity volumes around the scene pivot and places PointLights at cave mid-heights,
    /// spaced apart, alternating warm/cool — glow moss and crystal ambience without hand-placing.
    /// </summary>
    public static class CaveGlowLightMenu
    {
        private const float ChunkSize = 128f;

        [MenuItem("WorldBuilder/Caves/Place Glow Lights")]
        public static void PlaceGlowLights()
        {
            VoxelStoreAsset store = VoxelStoreLocator.LoadOrCreate();
            if (store == null)
            {
                Debug.LogWarning("[WorldBuilder] No VoxelStore asset found.");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;
            const float radius = 256f;

            var sampler = new VoxelWorldSampler(store, ChunkSize);
            var query = new VoxelVolumeQuery(sampler, ChunkSize);

            GameObject root = GameObject.Find("__WB_CaveLights");
            if (root == null)
            {
                root = new GameObject("__WB_CaveLights");
                Undo.RegisterCreatedObjectUndo(root, "Place Glow Lights");
            }
            Undo.RecordObject(root.transform, "Place Glow Lights");

            var placed = new List<Vector3>();
            int count = 0;
            float step = 8f;

            for (float x = pivot.x - radius; x <= pivot.x + radius; x += step)
            {
                for (float z = pivot.z - radius; z <= pivot.z + radius; z += step)
                {
                    if (!query.TryCavity(new Vector3(x, 48f, z), 128f, minClearance: 2.5f,
                            out Vector3 center, out float clearance)) continue;
                    if (center.y > 40f) continue; // skip above-ground cavities

                    bool tooClose = false;
                    foreach (Vector3 existing in placed)
                        if ((existing - center).sqrMagnitude < 12f * 12f) { tooClose = true; break; }
                    if (tooClose) continue;

                    bool warm = count % 2 == 0;
                    var lightGo = new GameObject($"GlowLight_{count + 1:00}");
                    Undo.RegisterCreatedObjectUndo(lightGo, "Place Glow Lights");
                    lightGo.transform.SetParent(root.transform, false);
                    lightGo.transform.position = center;

                    Light light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = Mathf.Clamp(clearance * 1.1f, 6f, 18f);
                    light.intensity = 0.9f;
                    light.color = warm ? new Color(1f, 0.74f, 0.45f) : new Color(0.42f, 0.85f, 0.9f);
                    light.shadows = LightShadows.None;

                    placed.Add(center);
                    count++;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Place Cave Glow Lights");
            Debug.Log($"[WorldBuilder] Placed {count} cave glow light(s).");
        }
    }
}
