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
        [Tooltip("Apply drift as physics forces on an attached Rigidbody instead of moving the transform. Requires a Rigidbody component.")]
        [SerializeField] private bool driveRigidbody = true;

        private Vector3 velocity;
        private WaterSample lastSample = WaterSample.Air;
        private Rigidbody body;
        private bool bodyResolved;
        private bool warnedNoBody;

        private bool UsesRigidbody => driveRigidbody && Body != null;

        /// <summary>Injected by gameplay bootstrap; null keeps the drifter idle.</summary>
        public IWaterQueryService QueryService { get; set; }

        public ref DriftParams Parameters => ref parameters;

        /// <summary>Attached rigidbody, resolved lazily (transform-mode users may have none).</summary>
        public Rigidbody Body
        {
            get
            {
                if (!bodyResolved)
                {
                    body = GetComponent<Rigidbody>();
                    bodyResolved = true;
                }
                return body;
            }
        }

        public Vector3 Velocity
        {
            get => UsesRigidbody ? body.linearVelocity : velocity;
            set
            {
                if (UsesRigidbody) body.linearVelocity = value;
                else velocity = value;
            }
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

            if (driveRigidbody && Body == null && !warnedNoBody)
            {
                warnedNoBody = true;
                Debug.LogWarning($"[WaterDrifter] '{name}' has no Rigidbody — falling back to " +
                                 "transform movement. Add one for physics-driven buoyancy.");
            }

            Vector3 probePosition = UsesRigidbody ? body.position : transform.position;
            WaterSample sample = QueryService.Sample(probePosition);
            lastSample = sample;

            Vector3 currentVelocity = UsesRigidbody
                ? body.linearVelocity
                : velocity;
            Vector3 targetVelocity = WaterDrift.Integrate(probePosition, currentVelocity, sample,
                parameters, Time.fixedDeltaTime);
            targetVelocity = Vector3.ClampMagnitude(targetVelocity, Mathf.Max(0.01f, maxSpeed));

            if (UsesRigidbody)
            {
                // Steer the dynamic body toward the integrated velocity with real forces,
                // so collisions, stacking and constraints keep working.
                Vector3 force = WaterDrift.ComputeSteeringForce(
                    body.linearVelocity, targetVelocity, body.mass, Time.fixedDeltaTime);
                body.AddForce(force, ForceMode.Force);

                if (parameters.FloatOnSurface && sample.IsInWater)
                    AlignToFlow(sample);
            }
            else
            {
                velocity = targetVelocity;
                transform.position += velocity * Time.fixedDeltaTime;

                if (parameters.FloatOnSurface && sample.IsInWater)
                    AlignToFlow(sample);
            }
        }

        private void AlignToFlow(WaterSample sample)
        {
            Vector3 flow = sample.FlowDirection;
            if (flow.sqrMagnitude < 1e-4f || parameters.FlowAcceleration <= 0f) return;
            Quaternion target = Quaternion.LookRotation(flow.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target,
                Time.fixedDeltaTime * 0.5f);
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
