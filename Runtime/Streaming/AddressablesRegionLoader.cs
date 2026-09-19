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
        private readonly Dictionary<LoadedRegion, AsyncOperationHandle<GameObject>> handles =
            new Dictionary<LoadedRegion, AsyncOperationHandle<GameObject>>();

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
            cancellationToken.ThrowIfCancellationRequested();
            string address = AddressResolver(coordinate);
            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
            GameObject root = null;
            try
            {
                while (!handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new KeyNotFoundException($"No Addressables asset for region '{address}'.");

                root = UnityEngine.Object.Instantiate(handle.Result, parent);
                root.name = WorldCoordNaming.RegionName(coordinate);
                cancellationToken.ThrowIfCancellationRequested();
                LoadedRegion region = new LoadedRegion(coordinate, root);
                handles.Add(region, handle);
                return region;
            }
            catch
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                Addressables.Release(handle);
                throw;
            }
        }

        public Task UnloadAsync(LoadedRegion region, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region != null && handles.TryGetValue(region, out AsyncOperationHandle<GameObject> handle))
            {
                if (region.Root != null) UnityEngine.Object.Destroy(region.Root);
                Addressables.Release(handle);
                handles.Remove(region);
            }
            return Task.CompletedTask;
        }
    }
}
#endif
