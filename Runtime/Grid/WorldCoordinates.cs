using System;
using UnityEngine;

namespace WorldBuilder.Runtime.Grid
{
    [Serializable]
    public struct ChunkCoord : IEquatable<ChunkCoord>, IComparable<ChunkCoord>
    {
        [SerializeField] private int x;
        [SerializeField] private int z;

        public int X => x;
        public int Z => z;

        public ChunkCoord(int x, int z) { this.x = x; this.z = z; }
        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public int CompareTo(ChunkCoord other) { int x = X.CompareTo(other.X); return x != 0 ? x : Z.CompareTo(other.Z); }
        public override string ToString() => $"({X}, {Z})";
        public static bool operator ==(ChunkCoord left, ChunkCoord right) => left.Equals(right);
        public static bool operator !=(ChunkCoord left, ChunkCoord right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionCoord : IEquatable<RegionCoord>, IComparable<RegionCoord>
    {
        [SerializeField] private int x;
        [SerializeField] private int z;

        public int X => x;
        public int Z => z;

        public RegionCoord(int x, int z) { this.x = x; this.z = z; }
        public bool Equals(RegionCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is RegionCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public int CompareTo(RegionCoord other) { int x = X.CompareTo(other.X); return x != 0 ? x : Z.CompareTo(other.Z); }
        public override string ToString() => $"({X}, {Z})";
        public static bool operator ==(RegionCoord left, RegionCoord right) => left.Equals(right);
        public static bool operator !=(RegionCoord left, RegionCoord right) => !left.Equals(right);
    }

    [Serializable]
    public struct QueryCellCoord : IEquatable<QueryCellCoord>, IComparable<QueryCellCoord>
    {
        [SerializeField] private int x;
        [SerializeField] private int z;

        public int X => x;
        public int Z => z;

        public QueryCellCoord(int x, int z) { this.x = x; this.z = z; }
        public bool Equals(QueryCellCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is QueryCellCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public int CompareTo(QueryCellCoord other) { int x = X.CompareTo(other.X); return x != 0 ? x : Z.CompareTo(other.Z); }
        public override string ToString() => $"({X}, {Z})";
        public static bool operator ==(QueryCellCoord left, QueryCellCoord right) => left.Equals(right);
        public static bool operator !=(QueryCellCoord left, QueryCellCoord right) => !left.Equals(right);
    }
}
