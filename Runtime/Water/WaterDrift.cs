using UnityEngine;

namespace WorldBuilder.Runtime.Water
{
    /// <summary>
    /// Pure flow-integration math shared by <see cref="WaterDrifter"/> and tests:
    /// pushes bodies along the sampled flow, springs them toward the float line while
    /// submerged, applies drag in water and gravity in air.
    /// </summary>
    public static class WaterDrift
    {
        /// <summary>
        /// Advances one velocity step for a body at <paramref name="position"/>.
        /// Deterministic and allocation-free; safe to run on any thread.
        /// </summary>
        public static Vector3 Integrate(Vector3 position, Vector3 velocity, in WaterSample sample,
            in DriftParams parameters, float deltaTime)
        {
            if (deltaTime <= 0f) return velocity;

            if (!sample.IsInWater || sample.Depth <= 0f)
            {
                Vector3 airborne = velocity;
                airborne.y -= parameters.Gravity * deltaTime;
                return airborne * Mathf.Clamp01(1f - parameters.AirDrag * deltaTime);
            }

            // Horizontal: accelerate toward the flow target; drag acts on the DEVIATION
            // from that target so rivers/currents always win in steady state.
            Vector3 flow = sample.FlowDirection;
            flow.y = 0f;
            Vector3 flowTarget = Vector3.zero;
            if (flow.sqrMagnitude > 1e-6f)
            {
                flow.Normalize();
                flowTarget = flow * sample.FlowSpeed;
            }

            float damp = Mathf.Clamp01(1f - parameters.WaterDrag * deltaTime);
            Vector3 deviation = Vector3.MoveTowards(
                new Vector3(velocity.x - flowTarget.x, 0f, velocity.z - flowTarget.z),
                Vector3.zero,
                parameters.FlowAcceleration * deltaTime) * damp;
            velocity.x = flowTarget.x + deviation.x;
            velocity.z = flowTarget.z + deviation.z;

            // Vertical: buoyancy spring toward the float line, or controlled sinking.
            velocity.y *= damp;
            if (parameters.FloatOnSurface)
            {
                float targetY = sample.SurfaceHeight - parameters.FloatDraft;
                float error = position.y - targetY;
                float springTargetY = Mathf.Clamp(-error * parameters.Buoyancy,
                    -parameters.MaxBuoyantSpeed, parameters.MaxBuoyantSpeed);
                velocity.y = Mathf.MoveTowards(velocity.y, springTargetY,
                    parameters.Buoyancy * 2f * deltaTime);
            }
            else
            {
                velocity.y -= parameters.Gravity * parameters.SinkGravityScale * deltaTime;
            }

            return velocity;
        }

        /// <summary>
        /// Force that steers a dynamic body toward the drift-integrated velocity — lets
        /// Rigidbody objects float and ride currents while still colliding normally.
        /// </summary>
        public static Vector3 ComputeSteeringForce(Vector3 currentVelocity, Vector3 targetVelocity,
            float mass, float deltaTime)
        {
            if (mass <= 0f || deltaTime <= 0f) return Vector3.zero;
            return (targetVelocity - currentVelocity) * (mass / deltaTime);
        }
    }

    [System.Serializable]
    public struct DriftParams
    {
        [Tooltip("Downward acceleration when out of water.")]
        public float Gravity;
        [Tooltip("Gravity multiplier for intentionally sinking bodies.")]
        public float SinkGravityScale;
        [Tooltip("How quickly horizontal velocity approaches the flow speed (m/s²).")]
        public float FlowAcceleration;
        [Tooltip("Buoyancy spring constant toward the float line.")]
        public float Buoyancy;
        [Tooltip("Clamp on vertical buoyant speed (m/s).")]
        public float MaxBuoyantSpeed;
        [Tooltip("Rest distance between the body origin and the water surface.")]
        public float FloatDraft;
        [Tooltip("Enable floating; disabled bodies keep sinking inside water.")]
        public bool FloatOnSurface;
        [Tooltip("Velocity damping per second inside water.")]
        public float WaterDrag;
        [Tooltip("Velocity damping per second outside water.")]
        public float AirDrag;

        public static DriftParams Default => new DriftParams
        {
            Gravity = 9.81f,
            SinkGravityScale = 1f,
            FlowAcceleration = 4f,
            Buoyancy = 12f,
            MaxBuoyantSpeed = 2.5f,
            FloatDraft = 0.35f,
            FloatOnSurface = true,
            WaterDrag = 1.5f,
            AirDrag = 0.05f
        };
    }
}
