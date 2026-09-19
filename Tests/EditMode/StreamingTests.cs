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

        private sealed class PendingOperation
        {
            public readonly RegionCoord coordinate;
            public readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

            public PendingOperation(RegionCoord coordinate) => this.coordinate = coordinate;
        }

        private sealed class AsyncLoader : IRegionContentLoader
        {
            public readonly List<RegionCoord> loadCalls = new List<RegionCoord>();
            public readonly List<RegionCoord> unloadCalls = new List<RegionCoord>();
            public readonly Dictionary<RegionCoord, LoadedRegion> live = new Dictionary<RegionCoord, LoadedRegion>();
            public bool honorLoadCancellation = true;
            public bool pauseUnloads;
            public int failUnloadAttempt;
            private readonly Queue<PendingOperation> loads = new Queue<PendingOperation>();
            private readonly Queue<PendingOperation> unloads = new Queue<PendingOperation>();
            private readonly SemaphoreSlim loadStarted = new SemaphoreSlim(0);
            private readonly SemaphoreSlim unloadStarted = new SemaphoreSlim(0);

            public bool HasContent(RegionCoord coordinate) => true;

            public async Task<LoadedRegion> LoadAsync(RegionCoord coordinate, CancellationToken token)
            {
                loadCalls.Add(coordinate);
                PendingOperation operation = new PendingOperation(coordinate);
                loads.Enqueue(operation);
                loadStarted.Release();
                using (token.Register(() =>
                {
                    if (honorLoadCancellation) operation.completion.TrySetCanceled();
                }))
                {
                    await operation.completion.Task;
                }
                LoadedRegion region = new LoadedRegion(coordinate, null);
                live.Add(coordinate, region);
                return region;
            }

            public async Task UnloadAsync(LoadedRegion region, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                unloadCalls.Add(region.Coordinate);
                if (unloadCalls.Count == failUnloadAttempt)
                    throw new System.InvalidOperationException("unload failed");
                if (pauseUnloads)
                {
                    PendingOperation operation = new PendingOperation(region.Coordinate);
                    unloads.Enqueue(operation);
                    unloadStarted.Release();
                    using (token.Register(() => operation.completion.TrySetCanceled()))
                    {
                        await operation.completion.Task;
                    }
                }
                Assert.That(live[region.Coordinate], Is.SameAs(region));
                live.Remove(region.Coordinate);
            }

            public async Task<PendingOperation> NextLoadAsync()
            {
                Assert.That(await loadStarted.WaitAsync(System.TimeSpan.FromSeconds(5)), Is.True,
                    "load did not start");
                return loads.Dequeue();
            }

            public async Task<PendingOperation> NextUnloadAsync()
            {
                Assert.That(await unloadStarted.WaitAsync(System.TimeSpan.FromSeconds(5)), Is.True,
                    "unload did not start");
                return unloads.Dequeue();
            }
        }

        private sealed class Observer : IRegionSetObserver
        {
            public List<RegionCoord> coordinates = new List<RegionCoord>();
            public void SetLoadedRegions(IReadOnlyList<RegionCoord> values)
            {
                coordinates = new List<RegionCoord>(values);
            }
        }

        private sealed class AsyncScenario : System.IDisposable
        {
            public readonly WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            public readonly AsyncLoader loader = new AsyncLoader();
            public readonly Observer observer = new Observer();
            public readonly ChunkStreamingService service;

            public AsyncScenario()
            {
                settings.Configure(128f, 4, 32f, Vector3.zero);
                service = new ChunkStreamingService(settings, loader, observer);
            }

            public void Dispose() => Object.DestroyImmediate(settings);
        }

        private static async Task CompletesAsync(Task task)
        {
            Assert.That(await Task.WhenAny(task, Task.Delay(5000)), Is.SameAs(task), "operation timed out");
            await task;
        }

        private static async Task IsCanceledAsync(Task task)
        {
            try
            {
                await CompletesAsync(task);
                Assert.Fail("operation should be canceled");
            }
            catch (System.OperationCanceledException)
            {
                Assert.That(task.IsCanceled, Is.True);
            }
        }

        [Test]
        public async Task OverlappingSameFocus_LoadsEachRegionOnce()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task first = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                PendingOperation load = await scenario.loader.NextLoadAsync();
                Task second = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                Task third = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
                Assert.That(second.IsCompleted, Is.False);
                load.completion.SetResult(true);
                await CompletesAsync(Task.WhenAll(first, second, third));
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
                Assert.That(scenario.loader.live.Count, Is.EqualTo(1));
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.True);
                Assert.That(scenario.observer.coordinates, Is.EquivalentTo(scenario.loader.live.Keys));
            }
        }

        [Test]
        public async Task OverlappingChangedFocus_UnloadsPreviousRegionBeforeLoadingNext()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task first = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                PendingOperation firstLoad = await scenario.loader.NextLoadAsync();
                Task second = scenario.service.SetFocusAsync(new Vector3(scenario.settings.RegionSize, 0f, 0f),
                    0, CancellationToken.None);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
                firstLoad.completion.SetResult(true);
                PendingOperation secondLoad = await scenario.loader.NextLoadAsync();
                Assert.That(secondLoad.coordinate, Is.EqualTo(new RegionCoord(1, 0)));
                Assert.That(scenario.loader.unloadCalls, Is.EqualTo(new[] { new RegionCoord(0, 0) }));
                Assert.That(scenario.loader.live, Is.Empty);
                secondLoad.completion.SetResult(true);
                await CompletesAsync(Task.WhenAll(first, second));
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.False);
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(1, 0)), Is.True);
                Assert.That(scenario.observer.coordinates, Is.EquivalentTo(scenario.loader.live.Keys));
            }
        }

        [Test]
        public async Task UnloadAllDuringPendingLoad_WaitsAndUnloadsItsResult()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task focus = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                PendingOperation load = await scenario.loader.NextLoadAsync();
                Task unload = scenario.service.UnloadAllAsync(CancellationToken.None);
                Assert.That(unload.IsCompleted, Is.False);
                load.completion.SetResult(true);
                await CompletesAsync(Task.WhenAll(focus, unload));
                Assert.That(scenario.loader.unloadCalls.Count, Is.EqualTo(1));
                Assert.That(scenario.loader.live, Is.Empty);
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.False);
                Assert.That(scenario.observer.coordinates, Is.Empty);
            }
        }

        [Test]
        public async Task FocusDuringPendingUnload_WaitsBeforeReloading()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task initial = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(initial);
                scenario.loader.pauseUnloads = true;
                Task unload = scenario.service.UnloadAllAsync(CancellationToken.None);
                PendingOperation pendingUnload = await scenario.loader.NextUnloadAsync();
                Task focus = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                Assert.That(focus.IsCompleted, Is.False);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
                pendingUnload.completion.SetResult(true);
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(Task.WhenAll(unload, focus));
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(2));
                Assert.That(scenario.loader.live.Count, Is.EqualTo(1));
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.True);
            }
        }

        [Test]
        public async Task CanceledQueuedFocus_DoesNotAffectPendingLoadAndAllowsRecovery()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task first = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                PendingOperation load = await scenario.loader.NextLoadAsync();
                Task canceled = scenario.service.SetFocusAsync(Vector3.one * scenario.settings.RegionSize,
                    0, cancellation.Token);
                cancellation.Cancel();
                await IsCanceledAsync(canceled);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
                load.completion.SetResult(true);
                await CompletesAsync(first);
                await CompletesAsync(scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None));
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task CanceledPendingLoad_ReleasesGateAndAllowsRecovery()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task first = scenario.service.SetFocusAsync(Vector3.zero, 0, cancellation.Token);
                await scenario.loader.NextLoadAsync();
                Task recovery = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                cancellation.Cancel();
                await IsCanceledAsync(first);
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(recovery);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(2));
                Assert.That(scenario.loader.live.Count, Is.EqualTo(1));
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.True);
            }
        }

        [Test]
        public async Task CancellationAfterPhysicalLoad_KeepsOwnershipUntilQueuedUnload()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                scenario.loader.honorLoadCancellation = false;
                Task focus = scenario.service.SetFocusAsync(Vector3.zero, 0, cancellation.Token);
                PendingOperation load = await scenario.loader.NextLoadAsync();
                cancellation.Cancel();
                load.completion.SetResult(true);
                await IsCanceledAsync(focus);
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.True);
                Assert.That(scenario.observer.coordinates, Is.EquivalentTo(scenario.loader.live.Keys));
                await CompletesAsync(scenario.service.UnloadAllAsync(CancellationToken.None));
                Assert.That(scenario.loader.live, Is.Empty);
                Assert.That(scenario.loader.unloadCalls.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task FailedPendingLoad_ReleasesGateAndAllowsRecovery()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task first = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                PendingOperation load = await scenario.loader.NextLoadAsync();
                Task recovery = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                load.completion.SetException(new System.InvalidOperationException("load failed"));
                try
                {
                    await CompletesAsync(first);
                    Assert.Fail("load should fail");
                }
                catch (System.InvalidOperationException exception)
                {
                    Assert.That(exception.Message, Is.EqualTo("load failed"));
                }
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(recovery);
                Assert.That(scenario.loader.loadCalls.Count, Is.EqualTo(2));
                Assert.That(scenario.loader.live.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task FailedUnloadAll_PreservesOnlyRemainingOwnershipAndAllowsRecovery()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            {
                Task focus = scenario.service.SetFocusAsync(Vector3.zero, 1, CancellationToken.None);
                for (int i = 0; i < 9; i++)
                    (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(focus);
                scenario.loader.failUnloadAttempt = 2;
                try
                {
                    await CompletesAsync(scenario.service.UnloadAllAsync(CancellationToken.None));
                    Assert.Fail("unload should fail");
                }
                catch (System.InvalidOperationException exception)
                {
                    Assert.That(exception.Message, Is.EqualTo("unload failed"));
                }
                Assert.That(scenario.loader.live.Count, Is.EqualTo(8));
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(-1, -1)), Is.False);
                Assert.That(scenario.observer.coordinates, Is.EquivalentTo(scenario.loader.live.Keys));
                await CompletesAsync(scenario.service.UnloadAllAsync(CancellationToken.None));
                Assert.That(scenario.loader.live, Is.Empty);
                Assert.That(scenario.observer.coordinates, Is.Empty);
                Assert.That(scenario.loader.unloadCalls.Count, Is.EqualTo(10));
            }
        }

        [Test]
        public async Task CancellationDuringUnload_FinishesCurrentReleaseBeforeRecovery()
        {
            using (AsyncScenario scenario = new AsyncScenario())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task initial = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(initial);
                scenario.loader.pauseUnloads = true;
                Task unload = scenario.service.UnloadAllAsync(cancellation.Token);
                PendingOperation pendingUnload = await scenario.loader.NextUnloadAsync();
                cancellation.Cancel();
                Assert.That(unload.IsCompleted, Is.False);
                pendingUnload.completion.SetResult(true);
                await IsCanceledAsync(unload);
                Assert.That(scenario.loader.live, Is.Empty);
                Assert.That(scenario.service.IsRegionLoaded(new RegionCoord(0, 0)), Is.False);
                Assert.That(scenario.observer.coordinates, Is.Empty);
                Task recovery = scenario.service.SetFocusAsync(Vector3.zero, 0, CancellationToken.None);
                (await scenario.loader.NextLoadAsync()).completion.SetResult(true);
                await CompletesAsync(recovery);
                Assert.That(scenario.loader.live.Count, Is.EqualTo(1));
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

        [Test]
        public async Task RepeatedSameFocus_IsNoOp()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(128f, 4, 32f, Vector3.zero);
            Loader loader = new Loader();
            ChunkStreamingService service = new ChunkStreamingService(settings, loader);

            await service.SetFocusAsync(Vector3.zero, 1, CancellationToken.None);
            int loadedCount = loader.loaded.Count;
            int unloadedCount = loader.unloaded.Count;

            await service.SetFocusAsync(Vector3.zero, 1, CancellationToken.None);

            Assert.That(loader.loaded.Count, Is.EqualTo(loadedCount),
                "identical focus must not reload any region");
            Assert.That(loader.unloaded.Count, Is.EqualTo(unloadedCount),
                "identical focus must not unload any region");
            Object.DestroyImmediate(settings);
        }
    }
}
