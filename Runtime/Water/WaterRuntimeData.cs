using System;
using UnityEngine;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Runtime.Water
{
    public enum FluidType : byte { Air = 0, Water = 1 }

    [Serializable]
    public struct RiverSegmentData
    {
        public Vector3 start;
        public Vector3 end;
        public float startWidth;
        public float endWidth;
        public float startDepth;
        public float endDepth;
        public Vector3 flowDirection;
        public float flowSpeed;
        public int bodyId;
        public int priority;
        public Bounds bounds;
    }

    [Serializable]
    public struct BoxVolumeData
    {
        public Matrix4x4 worldToUnitBox;
        public Bounds bounds;
        public FluidType fluidType;
        public int bodyId;
        public int priority;
    }

    [Serializable]
    public struct LakeData
    {
        public int vertexStart;
        public int vertexCount;
        public float surfaceHeight;
        public float depth;
        public int bodyId;
        public int priority;
        public Bounds bounds;
    }

    /// <summary>
    /// Flow override region (baked from <c>WaterCurrentZone</c>). Not a water body by
    /// itself: wherever the winning sample is water and this zone contains the point, the
    /// sampled flow direction/speed is replaced by the zone's — enabling waterfalls,
    /// whirlpools and river mouths on top of oceans, lakes and rivers alike.
    /// </summary>
    [Serializable]
    public struct CurrentZoneData
    {
        public Bounds bounds;
        public Vector3 direction;
        public float speed;
        public int bodyId;
        public int priority;
    }

    [Serializable]
    public struct WaterQueryCellData
    {
        public QueryCellCoord coordinate;
        public int riverIndexStart;
        public int riverIndexCount;
        public int volumeIndexStart;
        public int volumeIndexCount;
        public int lakeIndexStart;
        public int lakeIndexCount;
        public int currentIndexStart;
        public int currentIndexCount;
    }

    [CreateAssetMenu(menuName = "WorldBuilder/Water/Runtime Data", fileName = "WaterWorldRuntimeData")]
    public sealed class WaterWorldRuntimeData : ScriptableObject
    {
        [SerializeField] private Vector3 worldOrigin;
        [SerializeField] private float queryCellSize = 32f;
        [SerializeField] private bool hasOcean;
        [SerializeField] private float seaLevel;
        [SerializeField] private int oceanBodyId;
        [SerializeField] private int oceanPriority;
        [SerializeField] private RiverSegmentData[] riverSegments = Array.Empty<RiverSegmentData>();
        [SerializeField] private BoxVolumeData[] volumes = Array.Empty<BoxVolumeData>();
        [SerializeField] private LakeData[] lakes = Array.Empty<LakeData>();
        [SerializeField] private CurrentZoneData[] currents = Array.Empty<CurrentZoneData>();
        [SerializeField] private Vector2[] lakeVertices = Array.Empty<Vector2>();
        [SerializeField] private WaterQueryCellData[] cells = Array.Empty<WaterQueryCellData>();
        [SerializeField] private int[] riverIndices = Array.Empty<int>();
        [SerializeField] private int[] volumeIndices = Array.Empty<int>();
        [SerializeField] private int[] lakeIndices = Array.Empty<int>();
        [SerializeField] private int[] currentIndices = Array.Empty<int>();
        [SerializeField] private Vector3 oceanFlowDirection = Vector3.zero;
        [SerializeField] private float oceanFlowSpeed;
        [SerializeField] private string deterministicHash = string.Empty;

        public Vector3 WorldOrigin => worldOrigin;
        public float QueryCellSize => queryCellSize;
        public bool HasOcean => hasOcean;
        public float SeaLevel => seaLevel;
        public int OceanBodyId => oceanBodyId;
        public int OceanPriority => oceanPriority;
        public Vector3 OceanFlowDirection => oceanFlowDirection;
        public float OceanFlowSpeed => oceanFlowSpeed;
        public RiverSegmentData[] RiverSegments => riverSegments;
        public BoxVolumeData[] Volumes => volumes;
        public LakeData[] Lakes => lakes;
        public CurrentZoneData[] Currents => currents;
        public Vector2[] LakeVertices => lakeVertices;
        public WaterQueryCellData[] Cells => cells;
        public int[] RiverIndices => riverIndices;
        public int[] VolumeIndices => volumeIndices;
        public int[] LakeIndices => lakeIndices;
        public int[] CurrentIndices => currentIndices;
        public string DeterministicHash => deterministicHash;

        public void Initialize(Vector3 origin, float cellSize, bool ocean, float oceanLevel,
            int oceanId, int oceanPriorityValue, Vector3 flowDirection, float flowSpeed,
            RiverSegmentData[] rivers, BoxVolumeData[] boxVolumes,
            LakeData[] lakeData, CurrentZoneData[] currentZones, Vector2[] polygonVertices,
            WaterQueryCellData[] queryCells,
            int[] riverCellIndices, int[] volumeCellIndices, int[] lakeCellIndices,
            int[] currentCellIndices, string hash)
        {
            worldOrigin = origin;
            queryCellSize = cellSize;
            hasOcean = ocean;
            seaLevel = oceanLevel;
            oceanBodyId = oceanId;
            oceanPriority = oceanPriorityValue;
            oceanFlowDirection = flowDirection;
            oceanFlowSpeed = flowSpeed;
            riverSegments = rivers ?? Array.Empty<RiverSegmentData>();
            volumes = boxVolumes ?? Array.Empty<BoxVolumeData>();
            lakes = lakeData ?? Array.Empty<LakeData>();
            currents = currentZones ?? Array.Empty<CurrentZoneData>();
            lakeVertices = polygonVertices ?? Array.Empty<Vector2>();
            cells = queryCells ?? Array.Empty<WaterQueryCellData>();
            riverIndices = riverCellIndices ?? Array.Empty<int>();
            volumeIndices = volumeCellIndices ?? Array.Empty<int>();
            lakeIndices = lakeCellIndices ?? Array.Empty<int>();
            currentIndices = currentCellIndices ?? Array.Empty<int>();
            deterministicHash = hash ?? string.Empty;
        }
    }
}
