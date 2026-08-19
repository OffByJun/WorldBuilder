using UnityEngine;

namespace WorldBuilder.Editor
{
    public interface IRaycastConsumer
    {
        bool TryRaycast(out RaycastHit hit);
    }
}
