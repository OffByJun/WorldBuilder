using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Resources.Authoring
{
    [AddComponentMenu("WorldBuilder/Entities/Resource Field Spawn Zone")]
    public sealed class ResourceFieldSpawnZoneAuthoring : MonoBehaviour
    {
        [SerializeField] private ResourceFieldSpawnKind kind;
        [Tooltip("WorldEntity prefab id for a resource node or generic dropped item.")]
        [SerializeField] private int prefabId;
        [Tooltip("Only used by DroppedItem zones.")]
        [SerializeField] private int itemId;
        [Min(1), SerializeField] private int minimumItemCount = 1;
        [Min(1), SerializeField] private int maximumItemCount = 1;
        [SerializeField] private Vector3 size = new Vector3(20f, 10f, 20f);
        [Min(0.1f), SerializeField] private float raycastHeight = 20f;
        [Min(0.01f), SerializeField] private float spawnInterval = 5f;
        [Min(0), SerializeField] private int maximumAlive = 16;
        [Min(1), SerializeField] private int spawnPerTick = 2;
        [SerializeField] private uint randomSeed = 1;

        private void OnValidate()
        {
            size = new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));
            raycastHeight = Mathf.Max(0.1f, raycastHeight);
            spawnInterval = Mathf.Max(0.01f, spawnInterval);
            maximumAlive = Mathf.Max(0, maximumAlive);
            spawnPerTick = Mathf.Max(1, spawnPerTick);
            minimumItemCount = Mathf.Max(1, minimumItemCount);
            maximumItemCount = Mathf.Max(minimumItemCount, maximumItemCount);
            if (randomSeed == 0) randomSeed = 1;
        }

        private sealed class SpawnZoneBaker : Baker<ResourceFieldSpawnZoneAuthoring>
        {
            public override void Bake(ResourceFieldSpawnZoneAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new ResourceFieldSpawnZone
                {
                    Kind = authoring.kind,
                    PrefabId = authoring.prefabId,
                    ItemId = authoring.itemId,
                    MinimumItemCount = Mathf.Max(1, authoring.minimumItemCount),
                    MaximumItemCount = Mathf.Max(authoring.minimumItemCount, authoring.maximumItemCount),
                    HalfExtents = authoring.size * 0.5f,
                    RaycastHeight = authoring.raycastHeight,
                    SpawnInterval = authoring.spawnInterval,
                    MaximumAlive = Mathf.Max(0, authoring.maximumAlive),
                    SpawnPerTick = Mathf.Max(1, authoring.spawnPerTick),
                    RandomState = authoring.randomSeed == 0 ? 1u : authoring.randomSeed
                });
                AddBuffer<ResourceFieldSpawnedEntity>(entity);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == ResourceFieldSpawnKind.ResourceNode
                ? new Color(0.2f, 0.8f, 0.25f, 0.7f)
                : new Color(1f, 0.75f, 0.1f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
#endif
    }
}
