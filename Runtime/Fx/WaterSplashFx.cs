using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Fx
{
    /// <summary>
    /// Spawns a splash particle burst and an expanding surface ripple when its body enters
    /// or leaves water. Pairs with <see cref="WaterDrifter"/> (reads LastSample) but works
    /// standalone with any injected sampler function. Ripples are procedural annuli — no
    /// art assets required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterSplashFx : MonoBehaviour
    {
        [SerializeField] private WaterDrifter drifter;
        [SerializeField] private ParticleSystem splashPrefab;
        [SerializeField] private float rippleMaxRadius = 3f;
        [SerializeField] private float rippleLifetime = 0.9f;
        [SerializeField] private Color rippleColor = new Color(0.85f, 0.95f, 1f, 0.7f);
        [Tooltip("Vertical offset from the water surface where ripples are drawn.")]
        [SerializeField] private float surfaceLift = 0.05f;

        private readonly List<Ripple> active = new List<Ripple>();
        private Material rippleMaterial;
        private Mesh rippleMesh;
        private bool wasInWater;

        public Func<Vector3, WaterSample> SampleOverride { get; set; }

        private void OnEnable()
        {
            rippleMesh = BuildRingMesh();
            var shader = Shader.Find("Sprites/Default");
            rippleMaterial = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"))
            {
                enableInstancing = false
            };
        }

        private void OnDisable()
        {
            foreach (Ripple ripple in active)
                if (ripple.view != null) Destroy(ripple.view);
            active.Clear();
        }

        private void LateUpdate()
        {
            WaterSample sample = SampleOverride != null
                ? SampleOverride(transform.position)
                : drifter != null ? drifter.LastSample : WaterSample.Air;

            bool inWater = sample.IsInWater && sample.Depth > 0.05f;
            if (inWater != wasInWater)
            {
                Vector3 surface = transform.position;
                surface.y = inWater ? sample.SurfaceHeight : sample.SurfaceHeight - sample.Depth;
                Spawn(surface + Vector3.up * surfaceLift);
            }
            wasInWater = inWater;

            AdvanceRipples();
        }

        public void Spawn(Vector3 surfacePoint)
        {
            if (splashPrefab != null)
            {
                ParticleSystem burst = Instantiate(splashPrefab, surfacePoint, Quaternion.identity);
                burst.Play();
                Destroy(burst.gameObject, burst.main.duration + burst.main.startLifetime.constantMax);
            }

            GameObject view = new GameObject("WB_Ripple");
            view.transform.position = surfacePoint;
            var filter = view.AddComponent<MeshFilter>();
            filter.sharedMesh = rippleMesh;
            var renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = rippleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            active.Add(new Ripple { view = view, age = 0f });
        }

        private void AdvanceRipples()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Ripple ripple = active[i];
                ripple.age += Time.deltaTime;
                float t = Mathf.Clamp01(ripple.age / Mathf.Max(0.01f, rippleLifetime));

                float scale = Mathf.Lerp(rippleMaxRadius * 0.15f, rippleMaxRadius, t);
                ripple.view.transform.localScale = new Vector3(scale, 1f, scale);

                if (t >= 1f)
                {
                    Destroy(ripple.view);
                    active.RemoveAt(i);
                }
                else
                {
                    // Fade by scaling the whole object's material color via renderer property block.
                    var block = new MaterialPropertyBlock();
                    Color faded = rippleColor;
                    faded.a *= 1f - t;
                    block.SetColor("_Color", faded);
                    ripple.view.GetComponent<Renderer>().SetPropertyBlock(block);
                }
            }
        }

        private static Mesh BuildRingMesh()
        {
            const int segments = 24;
            const float inner = 0.78f;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir;
                int next = (i + 1) % segments;
                int quad = i * 6;
                triangles[quad] = i * 2;
                triangles[quad + 1] = next * 2;
                triangles[quad + 2] = next * 2 + 1;
                triangles[quad + 3] = i * 2;
                triangles[quad + 4] = next * 2 + 1;
                triangles[quad + 5] = i * 2 + 1;
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            return mesh;
        }

        private struct Ripple
        {
            public GameObject view;
            public float age;
        }
    }
}
