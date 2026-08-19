using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Tests
{
    public sealed class WorldGridTests
    {
        private WorldGrid grid;

        [SetUp]
        public void SetUp() => grid = new WorldGrid(128f, 4, 32f, new Vector3(10f, 0f, -20f));

        [TestCase(10f, -20f, 0, 0)]
        [TestCase(137.999f, 107.999f, 0, 0)]
        [TestCase(138f, 108f, 1, 1)]
        [TestCase(9.999f, -20.001f, -1, -1)]
        [TestCase(-118f, -148f, -1, -1)]
        public void WorldToChunk_UsesFloorAndHalfOpenBounds(float x, float z, int expectedX, int expectedZ)
        {
            Assert.That(grid.WorldToChunk(new Vector3(x, 0f, z)), Is.EqualTo(new ChunkCoord(expectedX, expectedZ)));
        }

        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(4, 1)]
        [TestCase(-1, -1)]
        [TestCase(-4, -1)]
        [TestCase(-5, -2)]
        public void ChunkToRegion_UsesFloorDivision(int chunkX, int expectedRegionX)
        {
            Assert.That(grid.ChunkToRegion(new ChunkCoord(chunkX, 0)).X, Is.EqualTo(expectedRegionX));
        }

        [Test]
        public void Owns_IncludesMinimumAndExcludesMaximum()
        {
            ChunkCoord chunk = new ChunkCoord(0, 0);
            Assert.That(grid.Owns(chunk, new Vector3(10f, 0f, -20f)), Is.True);
            Assert.That(grid.Owns(chunk, new Vector3(138f, 0f, 0f)), Is.False);
            Assert.That(grid.Owns(new ChunkCoord(1, 0), new Vector3(138f, 0f, 0f)), Is.True);
        }

        [Test]
        public void NegativeChunk_HasPositiveRegionLocalOrigin()
        {
            Assert.That(grid.ChunkToRegionLocalOrigin(new ChunkCoord(-1, -5)),
                Is.EqualTo(new Vector3(384f, 0f, 384f)));
        }

        [TestCase(-1, 2, "CH_-0001_+0002")]
        [TestCase(0, 0, "CH_+0000_+0000")]
        public void ChunkNames_AreDeterministicAndRoundTrip(int x, int z, string expected)
        {
            ChunkCoord coordinate = new ChunkCoord(x, z);
            Assert.That(WorldCoordNaming.ChunkName(coordinate), Is.EqualTo(expected));
            Assert.That(WorldCoordNaming.TryParseChunkName(expected, out ChunkCoord parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(coordinate));
        }
    }
}
