using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.Gameplay
{
    public struct WaterfallCandidate
    {
        public Vector3 Position;     // mouth of the fall (just under the ceiling)
        public float DropHeight;     // free-fall distance available below the mouth
    }

    /// <summary>
    /// Finds waterfall mouths inside flooded caves: air pockets that sit directly below a
    /// water column defined by a water surface Y (sea level or a groundwater table).
    /// Pure scanning — feed results to your particle/sound prefabs.
    /// </summary>
    public static class WaterfallScanner
    {
        public static List<WaterfallCandidate> Scan(VoxelWorldSampler sampler, Bounds volume,
            float waterSurfaceY, float minDrop = 2f, float stepMeters = 2f,
            int maxResults = 64)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            var results = new List<WaterfallCandidate>();
            const float iso = SurfaceNetsMesher.IsoLevel;

            for (float x = volume.min.x + stepMeters * 0.5f; x < volume.max.x; x += stepMeters)
            for (float z = volume.min.z + stepMeters * 0.5f; z < volume.max.z; z += stepMeters)
            {
                // Walk down from just below the water surface: water … ceiling(solid) …
                // air pocket underneath with enough drop is a mouth.
                bool inWaterColumn = false;
                for (float y = waterSurfaceY - stepMeters; y >= volume.min.y; y -= stepMeters)
                {
                    bool solidHere = sampler.Sample(x, y, z) >= iso;

                    if (!inWaterColumn)
                    {
                        if (y <= waterSurfaceY && solidHere) inWaterColumn = true; // entered bedrock under water
                        continue;
                    }

                    if (!solidHere)
                    {
                        // First air under the water-fed rock: this is a mouth.
                        float drop = MeasureDrop(sampler, x, y - stepMeters, z,
                            volume.min.y + stepMeters);
                        if (drop >= minDrop)
                        {
                            results.Add(new WaterfallCandidate
                            {
                                Position = new Vector3(x, y, z),
                                DropHeight = drop
                            });
                            if (results.Count >= maxResults) return results;
                        }
                        break; // one mouth per column
                    }
                }
            }
            return results;
        }

        private static float MeasureDrop(VoxelWorldSampler sampler, float x, float startY,
            float z, float floorY)
        {
            const float iso = SurfaceNetsMesher.IsoLevel;
            float previous = startY;
            for (float y = startY; y >= floorY; y -= 0.5f)
            {
                if (sampler.Sample(x, y, z) >= iso) return Mathf.Max(0f, previous - y);
                previous = y;
            }
            return Mathf.Max(0f, previous - floorY);
        }
    }

    /// <summary>Runtime hook: spawns falling-water particles at scanned mouths.</summary>
    [DisallowMultipleComponent]
    public sealed class WaterfallEmitter : MonoBehaviour
    {
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private float chunkSize = 128f;
        [SerializeField] private Bounds volume = new Bounds(Vector3.zero, new Vector3(256f, 64f, 256f));
        [SerializeField] private float waterSurfaceY;
        [SerializeField] private ParticleSystem fallPrefab;
        [SerializeField] private bool buildOnStart = true;

        private void Start()
        {
            if (!buildOnStart || store == null) return;
            PlaceAll();
        }

        public List<WaterfallCandidate> PlaceAll()
        {
            var sampler = new VoxelWorldSampler(store, chunkSize);
            List<WaterfallCandidate> found =
                WaterfallScanner.Scan(sampler, volume, waterSurfaceY);

            foreach (WaterfallCandidate candidate in found)
            {
                if (fallPrefab != null)
                {
                    Instantiate(fallPrefab, candidate.Position, Quaternion.identity, transform);
                    continue;
                }
                BuildFallbackFall(candidate);
            }
            return found;
        }

        private void BuildFallbackFall(WaterfallCandidate candidate)
        {
            var go = new GameObject("WB_Waterfall");
            go.transform.SetParent(transform, false);
            go.transform.position = candidate.Position;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = Mathf.Clamp(candidate.DropHeight / 14f, 0.3f, 1.6f);
            main.startSpeed = 10f;
            main.startSize3D = true;
            main.startSizeX = 0.06f;
            main.startSizeY = 0.5f;
            main.startSizeZ = 0.06f;
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = 0.7f;
            shape.angle = 4f;
            shape.rotation = Vector3.right * -90f;

            var emission = ps.emission;
            emission.rateOverTime = 220f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
