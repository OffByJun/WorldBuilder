using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Resources.Authoring
{
    [AddComponentMenu("WorldBuilder/Entities/Resource Spawn Terrain")]
    [DisallowMultipleComponent]
    public sealed class ResourceSpawnTerrainAuthoring : MonoBehaviour
    {
        private sealed class ResourceSpawnTerrainBaker : Baker<ResourceSpawnTerrainAuthoring>
        {
            public override void Bake(ResourceSpawnTerrainAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<ResourceSpawnTerrain>(entity);
            }
        }
    }
}
