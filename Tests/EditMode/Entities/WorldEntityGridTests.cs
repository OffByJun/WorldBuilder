using NUnit.Framework;
using Unity.Mathematics;

namespace WorldBuilder.Entities.Tests
{
    public sealed class WorldEntityGridTests
    {
        private static WorldEntityRuntimeConfig Config => new WorldEntityRuntimeConfig
        {
            ChunkSize = 128f,
            ChunksPerRegion = 4,
            WorldOrigin = float3.zero
        };

        [TestCase(0f, 0)]
        [TestCase(127.999f, 0)]
        [TestCase(128f, 1)]
        [TestCase(-0.001f, -1)]
        [TestCase(-128f, -1)]
        [TestCase(-128.001f, -2)]
        public void WorldToChunk_UsesHalfOpenFloorOwnership(float x, int expectedChunk)
        {
            int2 chunk = WorldEntityGridUtility.WorldToChunk(new float3(x, 0f, 0f), Config);
            Assert.That(chunk.x, Is.EqualTo(expectedChunk));
        }

        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(4, 1)]
        [TestCase(-1, -1)]
        [TestCase(-4, -1)]
        [TestCase(-5, -2)]
        public void ChunkToRegion_UsesFloorDivision(int chunkX, int expectedRegion)
        {
            int2 region = WorldEntityGridUtility.ChunkToRegion(new int2(chunkX, 0), 4);
            Assert.That(region.x, Is.EqualTo(expectedRegion));
        }

        [Test]
        public void Identity_DefaultIsInvalid_AndValuesCompareDeterministically()
        {
            Assert.That(default(WorldEntityIdentity).IsValid, Is.False);
            WorldEntityIdentity left = new WorldEntityIdentity { High = 10, Low = 20 };
            WorldEntityIdentity right = new WorldEntityIdentity { High = 10, Low = 20 };
            Assert.That(left.IsValid, Is.True);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }
    }
}
