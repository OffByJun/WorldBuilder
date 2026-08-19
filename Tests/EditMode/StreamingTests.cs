using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Tests
{
    public sealed class StreamingTests
    {
        private sealed class Loader : IRegionContentLoader
        {
            public readonly List<RegionCoord> loaded = new List<RegionCoord>();
            public readonly List<RegionCoord> unloaded = new List<RegionCoord>();
            public bool HasContent(RegionCoord coordinate) => true;
            public Task<LoadedRegion> LoadAsync(RegionCoord coordinate, CancellationToken token)
            {
                loaded.Add(coordinate);
                return Task.FromResult(new LoadedRegion(coordinate, null));
            }
            public Task UnloadAsync(LoadedRegion region, CancellationToken token)
            {
                unloaded.Add(region.Coordinate);
                return Task.CompletedTask;
            }
        }

        [Test]
        public async Task Focus_UsesNegativeRegionCoordinatesAndUnloadsDeterministically()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(128f, 4, 32f, Vector3.zero);
            Loader loader = new Loader();
            ChunkStreamingService service = new ChunkStreamingService(settings, loader);
            await service.SetFocusAsync(new Vector3(-1f, 0f, -1f), 0, CancellationToken.None);
            Assert.That(loader.loaded[0], Is.EqualTo(new RegionCoord(-1, -1)));
            Assert.That(service.IsChunkLoaded(new ChunkCoord(-1, -1)), Is.True);
            await service.SetFocusAsync(new Vector3(1f, 0f, 1f), 0, CancellationToken.None);
            Assert.That(loader.unloaded[0], Is.EqualTo(new RegionCoord(-1, -1)));
            Assert.That(service.IsRegionLoaded(new RegionCoord(0, 0)), Is.True);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public async Task MovingRadius_PreservesOverlappingRegions()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(128f, 4, 32f, Vector3.zero);
            Loader loader = new Loader();
            ChunkStreamingService service = new ChunkStreamingService(settings, loader);
            await service.SetFocusAsync(Vector3.zero, 1, CancellationToken.None);
            Assert.That(loader.loaded.Count, Is.EqualTo(9));
            await service.SetFocusAsync(new Vector3(settings.RegionSize, 0f, 0f), 1, CancellationToken.None);
            Assert.That(loader.loaded.Count, Is.EqualTo(12));
            Assert.That(loader.unloaded.Count, Is.EqualTo(3));
            Object.DestroyImmediate(settings);
        }
    }
}
