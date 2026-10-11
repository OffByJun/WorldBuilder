using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace WorldBuilder.Entities.Creatures
{
    /// <summary>Shared body clearance for spawning, destinations and swept movement.</summary>
    public static class CreatureTerrainCollision
    {
        private const float Skin = .05f;

        public static float3 Sink(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 position, float radius, float distance, out bool grounded)
        {
            var collector = new TerrainCastCollector(ignored, creatures);
            grounded = world.SphereCastCustom(position, radius, -math.up(), distance + Skin,
                ref collector, CollisionFilter.Default, QueryInteraction.IgnoreTriggers);
            position.y -= grounded ? math.max(0f, (distance + Skin) * collector.Hit.Fraction - Skin) : distance;
            return position;
        }

        public static float Radius(float scale, float bodyRadius = .5f)
            => math.max(.35f, math.abs(scale) * math.max(.5f, bodyRadius));

        public static bool TryPlace(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 requested, float radius, float waterSurface, out float3 position)
        {
            position = requested;
            float ceiling = waterSurface - radius - Skin;
            position.y = math.min(position.y, ceiling);
            Aabb bounds = world.CalculateAabb();
            // Start outside the whole terrain, not two metres above a potentially buried point.
            float top = math.max(bounds.Max.y + radius + 1f, position.y + radius + 1f);
            float bottom = math.min(bounds.Min.y - radius - 1f, position.y - radius - 1f);
            float3 origin = new float3(position.x, top, position.z);
            var collector = new TerrainCastCollector(ignored, creatures);
            if (world.SphereCastCustom(origin, radius, -math.up(), top - bottom, ref collector,
                    CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
                position.y = math.max(position.y, top - (top - bottom) * collector.Hit.Fraction + Skin);
            // Never fix a shallow spawn by clamping it back into the ground.
            return position.y <= ceiling && math.all(math.isfinite(position));
        }

        // Movement clearance is local: an overhead bridge is not the ground beneath a swimmer.
        public static bool TryClear(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 requested, float radius, float waterSurface, out float3 position)
        {
            position = requested;
            float ceiling = waterSurface - radius - Skin;
            position.y = math.min(position.y, ceiling);
            for (int i = 0; i < 8; i++)
            {
                var collector = new TerrainDistanceCollector(ignored, creatures, radius + Skin);
                if (!world.CalculateDistance(new PointDistanceInput
                    { Position = position, MaxDistance = radius + Skin, Filter = CollisionFilter.Default }, ref collector))
                    return math.all(math.isfinite(position));
                float penetration = radius + Skin - collector.Hit.Distance;
                if (penetration <= .001f) return math.all(math.isfinite(position));
                position += collector.Hit.SurfaceNormal * penetration;
                if (position.y > ceiling + .001f) return false;
            }
            return false;
        }

        public static bool TryRecover(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 target, float radius, float waterSurface, out float3 position)
        {
            target.y = math.min(target.y, waterSurface - radius - Skin);
            for (int i = 0; i < 25; i++)
            {
                float3 candidate = target;
                if (i > 0)
                {
                    int ring = (i - 1) / 8;
                    float angle = ((i - 1) % 8) * (math.PI / 4f);
                    float distance = radius * (1.25f + ring * .75f) + Skin;
                    candidate += new float3(math.cos(angle) * distance,
                        ring == 1 ? radius : ring == 2 ? -radius : 0f, math.sin(angle) * distance);
                }
                if (!TryClear(world, creatures, ignored, candidate, radius, waterSurface, out position)) continue;
                if (math.distancesq(position, target) <= math.pow(radius * 4f + Skin, 2f)) return true;
            }
            position = target;
            return false;
        }

        public static float3 Navigate(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 start, float3 goal, float radius, float speed, float waterSurface,
            double elapsedTime, ref CreatureSwim swim)
        {
            if (math.distancesq(goal, swim.AvoidanceTarget) > math.max(.25f, radius * radius))
            {
                swim.AvoidanceUntil = 0d;
                swim.NextAvoidanceTime = 0d;
            }
            swim.AvoidanceTarget = goal;
            if (elapsedTime < swim.NextAvoidanceTime)
                return elapsedTime < swim.AvoidanceUntil ? swim.AvoidancePoint : goal;
            swim.NextAvoidanceTime = elapsedTime + .35d;
            float3 offset = goal - start;
            float distance = math.length(offset);
            if (distance <= .001f) return goal;
            float3 forward = offset / distance;
            var obstacle = new TerrainCastCollector(ignored, creatures);
            if (!world.SphereCastCustom(start, radius, forward, distance, ref obstacle,
                    CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
            {
                swim.AvoidanceUntil = 0d;
                return goal;
            }
            if (elapsedTime < swim.AvoidanceUntil && math.distance(start, swim.AvoidancePoint) > math.max(.3f, radius * .5f))
            {
                float3 leg = swim.AvoidancePoint - start;
                var legHit = new TerrainCastCollector(ignored, creatures);
                if (!world.SphereCastCustom(start, radius, math.normalize(leg), math.length(leg), ref legHit,
                    CollisionFilter.Default, QueryInteraction.IgnoreTriggers)) return swim.AvoidancePoint;
            }

            float3 normal = obstacle.Hit.SurfaceNormal;
            float3 side = math.normalizesafe(math.cross(math.up(), normal), new float3(1f, 0f, 0f));
            if ((ignored.Index & 1) != 0) side = -side;
            float3 tangent = math.normalizesafe(forward - normal * math.dot(forward, normal), side);
            float travel = math.clamp(math.max(radius * 3f, speed * 2f), 2f, 16f);
            float bestScore = float.NegativeInfinity;
            float3 best = start;
            // Three-dimensional local detours, evaluated only when the direct route is obstructed.
            for (int i = 0; i < 10; i++)
            {
                float3 direction;
                switch (i)
                {
                    case 0: direction = math.up(); break;
                    case 1: direction = side; break;
                    case 2: direction = -side; break;
                    case 3: direction = -math.up(); break;
                    case 4: direction = tangent + normal * .5f; break;
                    case 5: direction = side + normal * .5f; break;
                    case 6: direction = -side + normal * .5f; break;
                    case 7: direction = tangent + math.up(); break;
                    case 8: direction = tangent - math.up(); break;
                    default: direction = normal + math.up(); break;
                }
                direction = math.normalizesafe(direction);
                float3 candidate = start + direction * travel;
                candidate.y = math.min(candidate.y, waterSurface - radius - Skin);
                float3 leg = candidate - start;
                float length = math.length(leg);
                if (length < .001f) continue;
                var hit = new TerrainCastCollector(ignored, creatures);
                if (world.SphereCastCustom(start, radius, leg / length, length, ref hit,
                        CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
                    candidate = start + leg * math.max(0f, hit.Hit.Fraction - Skin / length);
                float progress = math.distance(start, candidate);
                if (progress < math.max(.3f, radius * .5f)) continue;
                float3 toGoal = goal - candidate;
                float remaining = math.length(toGoal);
                var exit = new TerrainCastCollector(ignored, creatures);
                bool clear = remaining < .001f || !world.SphereCastCustom(candidate, radius,
                    toGoal / math.max(.001f, remaining), remaining, ref exit,
                    CollisionFilter.Default, QueryInteraction.IgnoreTriggers);
                float score = distance - remaining + progress * .25f + (clear ? travel * 4f : 0f);
                if (swim.AvoidanceUntil > 0d)
                    score += math.dot(math.normalizesafe(candidate - start),
                        swim.AvoidanceDirection) * travel * .6f;
                score += (10 - i) * .001f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            if (!math.isfinite(bestScore)) return goal;
            swim.AvoidanceDirection = math.normalizesafe(best - start);
            swim.AvoidancePoint = best;
            swim.AvoidanceUntil = elapsedTime + 2d;
            return best;
        }

        public static float3 Move(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 start, float3 requested, float radius, float waterSurface)
        {
            if (!TryClear(world, creatures, ignored, start, radius, waterSurface, out float3 position))
                return start;
            float3 remaining = requested - start;
            for (int i = 0; i < 3; i++)
            {
                float length = math.length(remaining);
                if (length < .0001f) break;
                var collector = new TerrainCastCollector(ignored, creatures);
                if (!world.SphereCastCustom(position, radius, remaining / length, length, ref collector,
                        CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
                {
                    position += remaining;
                    break;
                }
                float fraction = math.max(0f, collector.Hit.Fraction - Skin / length);
                position += remaining * fraction;
                remaining *= 1f - fraction;
                // Slide along slopes/walls without teleporting to the far side of an obstacle.
                float3 normal = collector.Hit.SurfaceNormal;
                remaining -= normal * math.min(0f, math.dot(remaining, normal));
                remaining.y = math.min(remaining.y, waterSurface - radius - Skin - position.y);
            }
            return position;
        }

        private struct TerrainDistanceCollector : ICollector<DistanceHit>
        {
            private readonly Entity ignored;
            private readonly ComponentLookup<CreatureAlarm> creatures;
            public TerrainDistanceCollector(Entity ignored, ComponentLookup<CreatureAlarm> creatures, float distance)
            {
                this.ignored = ignored;
                this.creatures = creatures;
                MaxFraction = distance;
                NumHits = 0;
                Hit = default;
            }
            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction { get; private set; }
            public int NumHits { get; private set; }
            public DistanceHit Hit { get; private set; }
            public bool AddHit(DistanceHit hit)
            {
                if (hit.Entity == ignored || creatures.HasComponent(hit.Entity) ||
                    hit.Material.CollisionResponse == CollisionResponsePolicy.RaiseTriggerEvents ||
                    hit.Material.CollisionResponse == CollisionResponsePolicy.None) return false;
                Hit = hit;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
        }

        private struct TerrainCastCollector : ICollector<ColliderCastHit>
        {
            private readonly Entity ignored;
            private readonly ComponentLookup<CreatureAlarm> creatures;
            public TerrainCastCollector(Entity ignored, ComponentLookup<CreatureAlarm> creatures)
            {
                this.ignored = ignored;
                this.creatures = creatures;
                MaxFraction = 1f;
                NumHits = 0;
                Hit = default;
            }
            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction { get; private set; }
            public int NumHits { get; private set; }
            public ColliderCastHit Hit { get; private set; }
            public bool AddHit(ColliderCastHit hit)
            {
                if (hit.Entity == ignored || creatures.HasComponent(hit.Entity)) return false;
                Hit = hit;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
        }
    }
}
