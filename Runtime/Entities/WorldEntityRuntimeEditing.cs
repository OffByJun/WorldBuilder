using UnityEngine;

namespace WorldBuilder.Entities
{
    /// <summary>
    /// Thin runtime-editing facade over <see cref="WorldEntityCommandQueue"/> so gameplay code
    /// can place and remove world entities at runtime without touching ECS internals.
    /// </summary>
    public static class WorldEntityRuntimeEditing
    {
        public static bool IsAvailable => WorldEntityCommandQueue.IsReady;

        public static bool TryPlace(int prefabId, Vector3 position, Quaternion rotation, float uniformScale = 1f)
        {
            return WorldEntityCommandQueue.TrySpawn(prefabId, position, rotation, uniformScale);
        }

        public static bool TryRestore(in WorldEntitySnapshot snapshot)
        {
            return WorldEntityCommandQueue.TrySpawn(snapshot);
        }
    }
}
