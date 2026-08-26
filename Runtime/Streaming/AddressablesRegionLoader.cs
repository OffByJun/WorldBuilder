#if WB_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Streaming
{
    /// <summary>
    /// IRegionContentLoader backed by Unity Addressables. Compiled only when the
    /// com.unity.addressables package is installed (versionDefines: WB_ADDRESSABLES).
    /// Regions load by address produced from <see cref="WorldCoordNaming.RegionName"/>;
    /// existence checks delegate to an optional DirectRegionCatalog.
    /// </summary>
    public sealed class AddressablesRegionLoader : IRegionContentLoader
    {
        private readonly DirectRegionCatalog existenceCatalog;
        private readonly Transform parent;
        private readonly Dictionary<RegionCoord, AsyncOperationHandle<GameObject>> handles =
            new Dictionary<RegionCoord, AsyncOperationHandle<GameObject>>();

        public Func<RegionCoord, string> AddressResolver { get; set; } = WorldCoordNaming.RegionName;

        public AddressablesRegionLoader(DirectRegionCatalog existenceCatalog = null, Transform parent = null)
        {
            this.existenceCatalog = existenceCatalog;
            this.parent = parent;
        }

        public bool HasContent(RegionCoord coordinate)
        {
            return existenceCatalog == null || existenceCatalog.TryGet(coordinate, out _);
        }

        public async Task<LoadedRegion> LoadAsync(RegionCoord coordinate, CancellationToken cancellationToken)
        {
            string address = AddressResolver(coordinate);
            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
            while (!handle.IsDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Addressables.Release(handle);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new KeyNotFoundException($"No Addressables asset for region '{address}'.");

            GameObject root = UnityEngine.Object.Instantiate(handle.Result, parent);
            root.name = WorldCoordNaming.RegionName(coordinate);
            handles[coordinate] = handle;
            return new LoadedRegion(coordinate, root);
        }

        public Task UnloadAsync(LoadedRegion region, CancellationToken cancellationToken)
        {
            if (region?.Root != null) UnityEngine.Object.Destroy(region.Root);
            if (region != null && handles.TryGetValue(region.Coordinate, out AsyncOperationHandle<GameObject> handle))
            {
                Addressables.Release(handle);
                handles.Remove(region.Coordinate);
            }
            return Task.CompletedTask;
        }
    }
}
#endif
