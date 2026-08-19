using UnityEngine;

namespace WorldBuilder.Editor
{
    public interface IWorldDataEntry
    {
        string Id { get; }
        string DisplayName { get; }
        Vector3 Position { get; }
        bool Enabled { get; set; }
    }
}
