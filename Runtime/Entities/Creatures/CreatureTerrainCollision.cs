using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace WorldBuilder.Entities.Creatures
{
    /// <summary>Shared body clearance for spawning, destinations and swept movement.</summary>
    public static class CreatureTerrainCollision
    {
        private const float Skin = .05f;

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

        public static float3 Move(in CollisionWorld world, ComponentLookup<CreatureAlarm> creatures,
            Entity ignored, float3 start, float3 requested, float radius, float waterSurface)
        {
            if (!TryPlace(world, creatures, ignored, start, radius, waterSurface, out float3 position))
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
