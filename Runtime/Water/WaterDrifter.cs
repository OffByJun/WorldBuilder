using UnityEngine;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Buoyant body that drifts along sampled water flow (ocean currents, rivers, current
    /// zones). Assign <see cref="QueryService"/> from your bootstrap; without a service the
    /// component idles safely. Uses FixedUpdate integration with deterministic math from
    /// <see cref="WaterDrift.Integrate"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterDrifter : MonoBehaviour
    {
        [SerializeField] private DriftParams parameters = DriftParams.Default;
        [SerializeField] private bool simulate = true;
        [SerializeField] private float maxSpeed = 12f;

        private Vector3 velocity;
        private WaterSample lastSample = WaterSample.Air;

        /// <summary>Injected by gameplay bootstrap; null keeps the drifter idle.</summary>
        public IWaterQueryService QueryService { get; set; }

        public ref DriftParams Parameters => ref parameters;

        public Vector3 Velocity
        {
            get => velocity;
            set => velocity = value;
        }

        public WaterSample LastSample => lastSample;

        private void OnEnable()
        {
            WaterDrifterRegistry.Register(this);
        }

        private void OnDisable()
        {
            WaterDrifterRegistry.Unregister(this);
        }

        private void FixedUpdate()
        {
            if (!simulate || QueryService == null) return;

            WaterSample sample = QueryService.Sample(transform.position);
            lastSample = sample;
            velocity = WaterDrift.Integrate(transform.position, velocity, sample, parameters,
                Time.fixedDeltaTime);
            velocity = Vector3.ClampMagnitude(velocity, Mathf.Max(0.01f, maxSpeed));

            transform.position += velocity * Time.fixedDeltaTime;

            if (parameters.FloatOnSurface && sample.IsInWater)
            {
                // Gentle pitch/roll toward flow direction for visual life (boats, debris).
                Vector3 flow = sample.FlowDirection;
                if (flow.sqrMagnitude > 1e-4f && parameters.FlowAcceleration > 0f)
                {
                    Quaternion target = Quaternion.LookRotation(flow.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target,
                        Time.fixedDeltaTime * 0.5f);
                }
            }
        }
    }

    /// <summary>
    /// Static registry so systems can iterate all active drifters without scene scans.
    /// </summary>
    public static class WaterDrifterRegistry
    {
        private static readonly System.Collections.Generic.List<WaterDrifter> drifters =
            new System.Collections.Generic.List<WaterDrifter>();

        public static System.Collections.Generic.IReadOnlyList<WaterDrifter> Drifters => drifters;

        public static void Register(WaterDrifter drifter)
        {
            if (drifter != null && !drifters.Contains(drifter)) drifters.Add(drifter);
        }

        public static void Unregister(WaterDrifter drifter) => drifters.Remove(drifter);

        public static void Clear() => drifters.Clear();
    }
}
