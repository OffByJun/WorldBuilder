using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Grid
{
    public readonly struct WorldGrid
    {
        public float ChunkSize { get; }
        public int ChunksPerRegion { get; }
        public float QueryCellSize { get; }
        public Vector3 Origin { get; }

        public WorldGrid(float chunkSize, int chunksPerRegion, float queryCellSize, Vector3 origin)
        {
            if (chunkSize <= 0f) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            if (chunksPerRegion <= 0) throw new ArgumentOutOfRangeException(nameof(chunksPerRegion));
            if (queryCellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(queryCellSize));
            ChunkSize = chunkSize;
            ChunksPerRegion = chunksPerRegion;
            QueryCellSize = queryCellSize;
            Origin = origin;
        }

        public ChunkCoord WorldToChunk(Vector3 position) => new ChunkCoord(
            Mathf.FloorToInt((position.x - Origin.x) / ChunkSize),
            Mathf.FloorToInt((position.z - Origin.z) / ChunkSize));

        public QueryCellCoord WorldToQueryCell(Vector3 position) => new QueryCellCoord(
            Mathf.FloorToInt((position.x - Origin.x) / QueryCellSize),
            Mathf.FloorToInt((position.z - Origin.z) / QueryCellSize));

        public RegionCoord ChunkToRegion(ChunkCoord chunk) => new RegionCoord(
            FloorDiv(chunk.X, ChunksPerRegion), FloorDiv(chunk.Z, ChunksPerRegion));

        public RegionCoord WorldToRegion(Vector3 position) => ChunkToRegion(WorldToChunk(position));

        public Vector3 ChunkToWorldOrigin(ChunkCoord chunk) =>
            Origin + new Vector3(chunk.X * ChunkSize, 0f, chunk.Z * ChunkSize);

        public Vector3 QueryCellToWorldOrigin(QueryCellCoord cell) =>
            Origin + new Vector3(cell.X * QueryCellSize, 0f, cell.Z * QueryCellSize);

        public Vector3 RegionToWorldOrigin(RegionCoord region) =>
            Origin + new Vector3(region.X * ChunkSize * ChunksPerRegion, 0f,
                region.Z * ChunkSize * ChunksPerRegion);

        public Vector3 ChunkToRegionLocalOrigin(ChunkCoord chunk)
        {
            RegionCoord region = ChunkToRegion(chunk);
            int localX = chunk.X - region.X * ChunksPerRegion;
            int localZ = chunk.Z - region.Z * ChunksPerRegion;
            return new Vector3(localX * ChunkSize, 0f, localZ * ChunkSize);
        }

        public Vector3 WorldToChunkLocal(Vector3 worldPosition)
        {
            ChunkCoord chunk = WorldToChunk(worldPosition);
            return worldPosition - ChunkToWorldOrigin(chunk);
        }

        public Bounds GetChunkBounds(ChunkCoord chunk)
        {
            Vector3 min = ChunkToWorldOrigin(chunk);
            return new Bounds(new Vector3(min.x + ChunkSize * 0.5f, 0f, min.z + ChunkSize * 0.5f),
                new Vector3(ChunkSize, float.MaxValue, ChunkSize));
        }

        public bool Owns(ChunkCoord chunk, Vector3 position)
        {
            Vector3 min = ChunkToWorldOrigin(chunk);
            return position.x >= min.x && position.x < min.x + ChunkSize &&
                   position.z >= min.z && position.z < min.z + ChunkSize;
        }

        public bool Owns(RegionCoord region, Vector3 position)
        {
            Vector3 min = RegionToWorldOrigin(region);
            float size = ChunkSize * ChunksPerRegion;
            return position.x >= min.x && position.x < min.x + size &&
                   position.z >= min.z && position.z < min.z + size;
        }

        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }
}
