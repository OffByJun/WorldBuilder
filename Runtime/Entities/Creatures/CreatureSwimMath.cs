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

        /// <summary>Eases pitch and roll back to level while keeping the heading.</summary>
        public static quaternion LevelOut(quaternion rotation, float deltaTime, float rate = 2f)
        {
            float3 forward = math.mul(rotation, new float3(0f, 0f, 1f));
            float3 flat = new float3(forward.x, 0f, forward.z);
            if (math.lengthsq(flat) < 1e-6f) return rotation;
            quaternion level = quaternion.LookRotationSafe(math.normalize(flat), math.up());
            return math.slerp(rotation, level, math.saturate(deltaTime * rate));
        }

        public static bool ShouldRepath(in CreatureSwim swim, float3 position, double elapsedTime)
            // The interval controls how soon another destination may be chosen, not how long a trip may last.
            => swim.NextRepathTime <= 0d ||
               (elapsedTime >= swim.NextRepathTime && HasArrived(position, swim.TargetPoint, math.max(0f, swim.ArriveRadius)));

        public static CreatureSwimStep Advance(float3 position, quaternion rotation, float3 target,
            float cruiseSpeed, float turnSpeedRadians, float deltaTime)
        {
            float3 offset = target - position;
            float distance = math.length(offset);
            if (distance <= math.EPSILON || deltaTime <= 0f)
                return new CreatureSwimStep(position, rotation);
            float3 heading = offset / distance;
            quaternion desired = quaternion.LookRotationSafe(heading, math.up());
            quaternion next = RotateTowards(rotation, desired, math.max(0f, turnSpeedRadians) * deltaTime);
            // Navigation follows the destination; the visible body turns independently. Advancing along the
            // partly turned forward vector made nearby/behind targets produce circles and repeated overshoots.
            float stepDistance = math.min(distance, math.max(0f, cruiseSpeed) * deltaTime);
            return new CreatureSwimStep(position + heading * stepDistance, next);
        }

        /// <summary>Steepest climb or dive a smoothly swimming body takes (about 40 degrees).</summary>
        public const float MaximumPitchRadians = .7f;

        /// <summary>
        /// Swims like a body with momentum instead of a pointer. Turn rates accelerate towards what the heading error
        /// asks for (so turns ease in and out), the body moves mostly along where it faces, and it slows while it is
        /// still turning towards the target or arriving. Moving partly towards the target, more so when close, keeps a
        /// target beside or behind the creature from turning into an endless circle.
        /// </summary>
        public static CreatureSwimStep AdvanceSmooth(float3 position, quaternion rotation, float3 target,
            float cruiseSpeed, float turnSpeedRadians, float deltaTime,
            ref float speed, ref float yawRate, ref float pitchRate)
        {
            if (deltaTime <= 0f) return new CreatureSwimStep(position, rotation);
            float3 offset = target - position;
            float distance = math.length(offset);
            float3 forward = math.normalizesafe(math.forward(rotation), new float3(0f, 0f, 1f));
            float3 heading = distance > 1e-4f ? offset / distance : forward;

            float yaw = math.atan2(forward.x, forward.z);
            float pitch = math.asin(math.clamp(forward.y, -1f, 1f));
            float desiredYaw = math.lengthsq(heading.xz) > 1e-6f ? math.atan2(heading.x, heading.z) : yaw;
            float desiredPitch = math.clamp(math.asin(math.clamp(heading.y, -1f, 1f)),
                -MaximumPitchRadians, MaximumPitchRadians);

            float maxTurn = math.max(.05f, turnSpeedRadians);
            // Proportional steering capped at the species' turn speed, reached with a limited angular acceleration.
            float desiredYawRate = math.clamp(WrapPi(desiredYaw - yaw) * 2.2f, -maxTurn, maxTurn);
            yawRate = MoveTowards(yawRate, desiredYawRate, maxTurn * 2.5f * deltaTime);
            float desiredPitchRate = math.clamp((desiredPitch - pitch) * 2f, -maxTurn * .6f, maxTurn * .6f);
            pitchRate = MoveTowards(pitchRate, desiredPitchRate, maxTurn * 2f * deltaTime);
            yaw += yawRate * deltaTime;
            pitch = math.clamp(pitch + pitchRate * deltaTime, -MaximumPitchRadians, MaximumPitchRadians);
            float cosPitch = math.cos(pitch);
            float3 nextForward = new float3(math.sin(yaw) * cosPitch, math.sin(pitch), math.cos(yaw) * cosPitch);
            quaternion nextRotation = quaternion.LookRotationSafe(nextForward, math.up());

            // Slow down while facing away from the target and when arriving; speed up and slow down gradually.
            float cruise = math.max(0f, cruiseSpeed);
            float alignment = math.saturate((math.dot(nextForward, heading) + .1f) / 1.1f);
            float arrival = math.saturate(distance / math.max(.5f, cruise * 1.2f));
            float desiredSpeed = cruise * math.lerp(.3f, 1f, alignment) * math.lerp(.35f, 1f, arrival);
            float acceleration = math.max(.5f, cruise * (desiredSpeed < speed ? 1.6f : .9f));
            speed = MoveTowards(math.max(0f, speed), desiredSpeed, acceleration * deltaTime);

            float steer = math.lerp(.2f, .7f, 1f - arrival);
            float3 direction = math.normalizesafe(math.lerp(nextForward, heading, steer), heading);
            float stepDistance = math.min(distance, speed * deltaTime);
            return new CreatureSwimStep(position + direction * stepDistance, nextRotation);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
            => math.abs(target - current) <= maxDelta ? target : current + math.sign(target - current) * maxDelta;

        public static float WrapPi(float angle)
        {
            angle = math.fmod(angle + math.PI, 2f * math.PI);
            if (angle < 0f) angle += 2f * math.PI;
            return angle - math.PI;
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
