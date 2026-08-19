using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public readonly struct CreatureSwimStep
    {
        public readonly float3 Position;
        public readonly quaternion Rotation;

        public CreatureSwimStep(float3 position, quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public static class CreatureSwimMath
    {
        public static float3 NextWanderTarget(float3 home, float wanderRadius, float verticalRadius,
            ref Random random)
        {
            float2 direction = random.NextFloat2Direction();
            float radius = random.NextFloat(0f, math.max(0f, wanderRadius));
            float height = random.NextFloat(-math.abs(verticalRadius), math.abs(verticalRadius));
            return home + new float3(direction.x * radius, height, direction.y * radius);
        }

        public static float3 ConstrainToHomeRegion(float3 position, in CreatureSwim swim,
            in WorldEntityRuntimeConfig config)
            => swim.LeashToHomeRegion == 0
                ? position
                : WorldEntityGridUtility.ClampToRegion(position, swim.HomeRegion, config, swim.RegionMargin);

        public static bool HasArrived(float3 position, float3 target, float arriveRadius)
            => math.distancesq(position, target) <= arriveRadius * arriveRadius;

        public static bool ShouldRepath(in CreatureSwim swim, float3 position, double elapsedTime)
            => elapsedTime >= swim.NextRepathTime || HasArrived(position, swim.TargetPoint, swim.ArriveRadius);

        public static CreatureSwimStep Advance(float3 position, quaternion rotation, float3 target,
            float cruiseSpeed, float turnSpeedRadians, float deltaTime)
        {
            float3 heading = math.normalizesafe(target - position, math.forward(rotation));
            quaternion desired = quaternion.LookRotationSafe(heading, math.up());
            quaternion next = RotateTowards(rotation, desired, math.max(0f, turnSpeedRadians) * deltaTime);
            return new CreatureSwimStep(position + math.forward(next) * math.max(0f, cruiseSpeed) * deltaTime, next);
        }

        public static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
        {
            float dot = math.clamp(math.abs(math.dot(from, to)), -1f, 1f);
            float angle = 2f * math.acos(dot);
            if (angle <= math.EPSILON || maxRadians <= 0f) return angle <= math.EPSILON ? to : from;
            return math.slerp(from, to, math.saturate(maxRadians / angle));
        }
    }
}
