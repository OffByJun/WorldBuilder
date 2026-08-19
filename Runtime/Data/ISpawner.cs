using UnityEngine;

namespace WorldBuilder.Runtime.Data
{
    public interface ISpawner
    {
        int PrefabId { get; }
        Vector3 SpawnPosition { get; }
    }
}
