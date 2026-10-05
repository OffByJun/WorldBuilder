using NUnit.Framework;
using Unity.Mathematics;
using WorldBuilder.Entities.Creatures;

namespace WorldBuilder.Entities.Tests
{
    public sealed class CreatureSwimMathTests
    {
        [Test]
        public void BehindTargetMakesProgressWithoutWaitingForBodyToTurn()
        {
            float3 target = new float3(0f, 0f, -5f);
            CreatureSwimStep step = CreatureSwimMath.Advance(float3.zero, quaternion.identity,
                target, 2f, .2f, .1f);
            Assert.That(step.Position.z, Is.LessThan(0f));
            Assert.That(math.distance(step.Position, target), Is.LessThan(5f));
            Assert.That(math.forward(step.Rotation).z, Is.GreaterThan(.99f), "Body rotation should remain smooth.");
        }

        [Test]
        public void HighSpeedStopsAtTargetWithoutOvershooting()
        {
            float3 target = new float3(.1f, .2f, .3f);
            CreatureSwimStep step = CreatureSwimMath.Advance(float3.zero, quaternion.identity,
                target, 100f, 1f, 1f);
            Assert.That(math.distance(step.Position, target), Is.LessThan(.00001f));
            CreatureSwimStep next = CreatureSwimMath.Advance(step.Position, step.Rotation, target, 100f, 1f, 1f);
            Assert.That(math.distance(next.Position, target), Is.LessThan(.00001f));
        }

        [Test]
        public void FixedDestinationIsReachedMonotonicallyFromAnOppositeHeading()
        {
            float3 position = float3.zero;
            float3 target = new float3(7f, 3f, -9f);
            quaternion rotation = quaternion.identity;
            float previousDistance = math.distance(position, target);
            for (int frame = 0; frame < 600; frame++)
            {
                CreatureSwimStep step = CreatureSwimMath.Advance(position, rotation, target, 3f, .4f, 1f / 60f);
                float distance = math.distance(step.Position, target);
                Assert.That(distance, Is.LessThanOrEqualTo(previousDistance + .000001f));
                position = step.Position;
                rotation = step.Rotation;
                previousDistance = distance;
            }
            Assert.That(previousDistance, Is.LessThan(.0001f));
        }

        [Test]
        public void ExpiredWanderTimerDoesNotReplaceAnUnreachedTarget()
        {
            var swim = new CreatureSwim { TargetPoint = new float3(0f, 0f, 20f), ArriveRadius = .5f, NextRepathTime = 2d };
            Assert.That(CreatureSwimMath.ShouldRepath(swim, float3.zero, 30d), Is.False);
            Assert.That(CreatureSwimMath.ShouldRepath(swim, swim.TargetPoint, 30d), Is.True);
            Assert.That(CreatureSwimMath.ShouldRepath(swim, swim.TargetPoint, 1d), Is.False);
        }

        [Test]
        public void FreshSpawnOrSettlementCanSelectItsFirstDestination()
        {
            var swim = new CreatureSwim { TargetPoint = new float3(0f), ArriveRadius = .5f, NextRepathTime = 0d };
            Assert.That(CreatureSwimMath.ShouldRepath(swim, new float3(20f), 10d), Is.True);
        }

        [Test]
        public void PausedMovementLeavesPositionAndOrientationUnchanged()
        {
            CreatureSwimStep step = CreatureSwimMath.Advance(float3.zero, quaternion.identity,
                new float3(5f), 3f, 1f, 0f);
            Assert.That(math.lengthsq(step.Position), Is.Zero);
            Assert.That(step.Rotation.value, Is.EqualTo(quaternion.identity.value));
        }
    }
}
