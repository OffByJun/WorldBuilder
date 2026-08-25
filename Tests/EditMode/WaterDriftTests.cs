using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Tests
{
    public sealed class WaterDriftTests
    {
        private static readonly DriftParams Params = DriftParams.Default;
        private const float Dt = 1f / 60f;

        private static WaterSample Flowing(Vector3 direction, float speed, float surface, float depth) =>
            new WaterSample(FluidType.Water, surface, depth, direction, speed, 1, 100);

        [Test]
        public void Body_AcceleratesAlongFlowAndReachesItsSpeed()
        {
            var flow = new Vector3(0f, 0f, 1f);
            WaterSample sample = Flowing(flow, 3f, 0f, 2f);

            Vector3 velocity = Vector3.zero;
            Vector3 position = new Vector3(0f, -1f, 0f);
            for (int i = 0; i < 600; i++)
                velocity = WaterDrift.Integrate(position, velocity, sample, Params, Dt);

            Assert.That(velocity.z, Is.EqualTo(3f).Within(0.05f), "converges to flow speed");
            Assert.That(Mathf.Abs(velocity.x), Is.LessThan(0.05f));
        }

        [Test]
        public void SubmergedFloater_SpringsBackTowardTheSurface()
        {
            WaterSample sample = Flowing(Vector3.zero, 0f, 0f, 6f);
            float draft = Params.FloatDraft;

            Vector3 position = new Vector3(0f, -(draft + 4f), 0f); // 4 m under the float line
            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < 1200 && Mathf.Abs(position.y + draft) > 0.05f; i++)
            {
                velocity = WaterDrift.Integrate(position, velocity, sample, Params, Dt);
                position += velocity * Dt;
                // Keep the sample consistent with the simulated depth.
                sample = Flowing(Vector3.zero, 0f, 0f, -position.y);
            }

            Assert.That(position.y, Is.EqualTo(-draft).Within(0.15f),
                $"settled at {position.y:F2}, expected ≈ {-draft:F2}");
        }

        [Test]
        public void AirborneBody_FallsWithGravity()
        {
            WaterSample air = WaterSample.Air;
            Vector3 velocity = Vector3.zero;
            velocity = WaterDrift.Integrate(Vector3.zero, velocity, air, Params, Dt);

            float expected = -Params.Gravity * Dt * Mathf.Clamp01(1f - Params.AirDrag * Dt);
            Assert.That(velocity.y, Is.EqualTo(expected).Within(1e-4));
        }

        [Test]
        public void FloatOnSurfaceDisabled_KeepsSinkingInsideWater()
        {
            var sinking = Params;
            sinking.FloatOnSurface = false;
            WaterSample sample = Flowing(Vector3.zero, 0f, 0f, 10f);

            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < 60; i++)
                velocity = WaterDrift.Integrate(new Vector3(0f, -5f, 0f), velocity, sample, sinking, Dt);

            Assert.That(velocity.y, Is.LessThan(-1f), "sinking bodies keep accelerating down");
        }
    }
}
