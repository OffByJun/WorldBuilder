using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Streaming
{
    public interface IRegionContentLoader
    {
        bool HasContent(RegionCoord coordinate);
        Task<LoadedRegion> LoadAsync(RegionCoord coordinate, CancellationToken cancellationToken);
        Task UnloadAsync(LoadedRegion region, CancellationToken cancellationToken);
    }

    public interface IChunkStreamingService
    {
        bool IsRegionLoaded(RegionCoord coordinate);
        bool IsChunkLoaded(ChunkCoord coordinate);
        Task SetFocusAsync(Vector3 worldPosition, int regionRadius, CancellationToken cancellationToken);
        Task UnloadAllAsync(CancellationToken cancellationToken);
    }

    public interface IRegionSetObserver
    {
        void SetLoadedRegions(IReadOnlyList<RegionCoord> coordinates);
    }

    public sealed class DirectReferenceRegionLoader : IRegionContentLoader
    {
        private readonly DirectRegionCatalog catalog;
        private readonly Transform parent;
        private readonly WorldGridSettings settings;

        public DirectReferenceRegionLoader(DirectRegionCatalog catalog, WorldGridSettings settings, Transform parent = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
            this.parent = parent;
        }

        public bool HasContent(RegionCoord coordinate) => catalog.TryGet(coordinate, out _);

        public Task<LoadedRegion> LoadAsync(RegionCoord coordinate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryGet(coordinate, out GameObject prefab))
                throw new KeyNotFoundException($"No direct region reference exists for {coordinate}.");
            GameObject root = UnityEngine.Object.Instantiate(prefab, parent);
            root.name = WorldCoordNaming.RegionName(coordinate);
            float size = settings.RegionSize;
            root.transform.position = settings.WorldOrigin + new Vector3(coordinate.X * size, 0f, coordinate.Z * size);
            return Task.FromResult(new LoadedRegion(coordinate, root));
        }

        public Task UnloadAsync(LoadedRegion region, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region?.Root != null) UnityEngine.Object.Destroy(region.Root);
            return Task.CompletedTask;
        }
    }

    public sealed class ChunkStreamingService : IChunkStreamingService
    {
        private readonly WorldGrid grid;
        private readonly IRegionContentLoader loader;
        private readonly IRegionSetObserver observer;
        private readonly Dictionary<RegionCoord, LoadedRegion> loaded = new Dictionary<RegionCoord, LoadedRegion>();

        public ChunkStreamingService(WorldGridSettings settings, IRegionContentLoader loader,
            IRegionSetObserver observer = null)
        {
            grid = (settings ?? throw new ArgumentNullException(nameof(settings))).CreateGrid();
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.observer = observer;
        }

        public bool IsRegionLoaded(RegionCoord coordinate) => loaded.ContainsKey(coordinate);
        public bool IsChunkLoaded(ChunkCoord coordinate) => IsRegionLoaded(grid.ChunkToRegion(coordinate));

        public async Task SetFocusAsync(Vector3 worldPosition, int regionRadius, CancellationToken cancellationToken)
        {
            regionRadius = Mathf.Max(0, regionRadius);
            RegionCoord center = grid.WorldToRegion(worldPosition);
            List<RegionCoord> desired = new List<RegionCoord>();
            for (int x = -regionRadius; x <= regionRadius; x++)
            for (int z = -regionRadius; z <= regionRadius; z++)
            {
                RegionCoord coordinate = new RegionCoord(center.X + x, center.Z + z);
                if (loader.HasContent(coordinate)) desired.Add(coordinate);
            }
            desired.Sort((left, right) => CompareByDistance(left, right, center));
            HashSet<RegionCoord> desiredSet = new HashSet<RegionCoord>(desired);

            List<RegionCoord> unload = new List<RegionCoord>();
            foreach (RegionCoord coordinate in loaded.Keys)
                if (!desiredSet.Contains(coordinate)) unload.Add(coordinate);
            unload.Sort();
            for (int i = 0; i < unload.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadedRegion region = loaded[unload[i]];
                await loader.UnloadAsync(region, cancellationToken);
                loaded.Remove(unload[i]);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RegionCoord coordinate = desired[i];
                if (!loaded.ContainsKey(coordinate))
                    loaded.Add(coordinate, await loader.LoadAsync(coordinate, cancellationToken));
            }

            NotifyLoadedRegions();
        }

        public async Task UnloadAllAsync(CancellationToken cancellationToken)
        {
            List<RegionCoord> coordinates = new List<RegionCoord>(loaded.Keys);
            coordinates.Sort();
            for (int i = 0; i < coordinates.Count; i++)
                await loader.UnloadAsync(loaded[coordinates[i]], cancellationToken);
            loaded.Clear();
            NotifyLoadedRegions();
        }

        private void NotifyLoadedRegions()
        {
            if (observer == null) return;
            List<RegionCoord> coordinates = new List<RegionCoord>(loaded.Keys);
            coordinates.Sort();
            observer.SetLoadedRegions(coordinates);
        }

        private static int CompareByDistance(RegionCoord left, RegionCoord right, RegionCoord center)
        {
            long leftX = left.X - (long)center.X;
            long leftZ = left.Z - (long)center.Z;
            long rightX = right.X - (long)center.X;
            long rightZ = right.Z - (long)center.Z;
            int distance = (leftX * leftX + leftZ * leftZ).CompareTo(rightX * rightX + rightZ * rightZ);
            return distance != 0 ? distance : left.CompareTo(right);
        }
    }
}
