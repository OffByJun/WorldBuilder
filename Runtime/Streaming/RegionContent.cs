using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Streaming
{
    public sealed class ChunkRoot : MonoBehaviour
    {
        [SerializeField] private int chunkX;
        [SerializeField] private int chunkZ;
        [SerializeField] private string sourceHash = string.Empty;
        [SerializeField] private string sourceManifest = string.Empty;
        public ChunkCoord Coordinate => new ChunkCoord(chunkX, chunkZ);
        public string SourceHash => sourceHash;
        public string SourceManifest => sourceManifest;
        public void Configure(ChunkCoord coordinate, string hash = "", string manifest = "")
        {
            chunkX = coordinate.X;
            chunkZ = coordinate.Z;
            sourceHash = hash ?? string.Empty;
            sourceManifest = manifest ?? string.Empty;
        }
    }

    public sealed class RegionRoot : MonoBehaviour
    {
        [SerializeField] private int regionX;
        [SerializeField] private int regionZ;
        public RegionCoord Coordinate => new RegionCoord(regionX, regionZ);
        public void Configure(RegionCoord coordinate) { regionX = coordinate.X; regionZ = coordinate.Z; }
    }

    public sealed class ChunkMarker : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string markerType = string.Empty;
        [SerializeField] private ChunkMarkerProperty[] properties = Array.Empty<ChunkMarkerProperty>();
        public string StableId => stableId;
        public string MarkerType => markerType;
        public IReadOnlyList<ChunkMarkerProperty> Properties => properties;
        public void Configure(string id, string type, ChunkMarkerProperty[] values = null)
        {
            stableId = id ?? string.Empty;
            markerType = type ?? string.Empty;
            properties = values ?? Array.Empty<ChunkMarkerProperty>();
        }
    }

    public sealed class ChunkEntityPlacement : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string assetId = string.Empty;
        [SerializeField] private int prefabId;
        [SerializeField] private string kind = string.Empty;
        [SerializeField] private int authoringLayer;
        public string StableId => stableId;
        public string AssetId => assetId;
        public int PrefabId => prefabId;
        public string Kind => kind;
        public int AuthoringLayer => authoringLayer;

        public void Configure(string id, string asset, int prefab, string entityKind, int layer)
        {
            stableId = id ?? string.Empty;
            assetId = asset ?? string.Empty;
            prefabId = prefab;
            kind = entityKind ?? string.Empty;
            authoringLayer = layer;
        }
    }

    [Serializable]
    public struct ChunkMarkerProperty
    {
        public string key;
        public string value;
        public ChunkMarkerProperty(string key, string value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public sealed class DirectRegionReference
    {
        public int regionX;
        public int regionZ;
        public GameObject prefab;
        public RegionCoord Coordinate => new RegionCoord(regionX, regionZ);
    }

    [CreateAssetMenu(menuName = "WorldBuilder/Streaming/Direct Region Catalog", fileName = "DirectRegionCatalog")]
    public sealed class DirectRegionCatalog : ScriptableObject
    {
        [SerializeField] private List<DirectRegionReference> regions = new List<DirectRegionReference>();
        [NonSerialized] private Dictionary<RegionCoord, GameObject> lookup;
        public IReadOnlyList<DirectRegionReference> Regions => regions;

        public void Configure(IEnumerable<DirectRegionReference> entries)
        {
            regions.Clear();
            if (entries != null) regions.AddRange(entries);
            regions.Sort((left, right) => left.Coordinate.CompareTo(right.Coordinate));
            RebuildLookup();
        }

        public bool TryGet(RegionCoord coordinate, out GameObject prefab)
        {
            if (lookup == null) RebuildLookup();
            return lookup.TryGetValue(coordinate, out prefab) && prefab != null;
        }

        private void OnEnable() => RebuildLookup();

        private void RebuildLookup()
        {
            lookup = new Dictionary<RegionCoord, GameObject>(regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                DirectRegionReference entry = regions[i];
                if (entry != null) lookup[entry.Coordinate] = entry.prefab;
            }
        }
    }

    public sealed class LoadedRegion
    {
        public RegionCoord Coordinate { get; }
        public GameObject Root { get; }
        public LoadedRegion(RegionCoord coordinate, GameObject root) { Coordinate = coordinate; Root = root; }
    }
}
