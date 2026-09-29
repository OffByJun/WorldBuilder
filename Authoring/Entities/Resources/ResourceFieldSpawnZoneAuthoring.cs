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
        [Tooltip("Normalized XZ polygon points. Coordinates usually stay between -1 and 1.")]
        [SerializeField] private Vector2[] polygon =
        {
            new Vector2(-0.9f, -0.6f),
            new Vector2(-0.35f, -1f),
            new Vector2(0.75f, -0.8f),
            new Vector2(1f, 0.1f),
            new Vector2(0.55f, 0.9f),
            new Vector2(-0.45f, 0.75f),
            new Vector2(-1f, 0.15f)
        };
        [Min(0.1f), SerializeField] private float raycastHeight = 20f;
        [Range(0f, 1f), SerializeField] private float minimumGroundNormalY = 0.45f;
        [Min(0.01f), SerializeField] private float spawnInterval = 5f;
        [Min(0), SerializeField] private int maximumAlive = 16;
        [Min(1), SerializeField] private int spawnPerTick = 2;
        [SerializeField] private uint randomSeed = 1;

        private void OnValidate()
        {
            size = new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));
            raycastHeight = Mathf.Max(0.1f, raycastHeight);
            minimumGroundNormalY = Mathf.Clamp01(minimumGroundNormalY);
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
                    MinimumGroundNormalY = authoring.minimumGroundNormalY,
                    SpawnInterval = authoring.spawnInterval,
                    MaximumAlive = Mathf.Max(0, authoring.maximumAlive),
                    SpawnPerTick = Mathf.Max(1, authoring.spawnPerTick),
                    RandomState = authoring.randomSeed == 0 ? 1u : authoring.randomSeed
                });
                AddBuffer<ResourceFieldSpawnedEntity>(entity);
                DynamicBuffer<ResourceFieldSpawnPoint> points = AddBuffer<ResourceFieldSpawnPoint>(entity);
                if (authoring.polygon == null) return;
                foreach (Vector2 point in authoring.polygon)
                    points.Add(new ResourceFieldSpawnPoint { Value = point });
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == ResourceFieldSpawnKind.ResourceNode
                ? new Color(0.2f, 0.8f, 0.25f, 0.7f)
                : new Color(1f, 0.75f, 0.1f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (polygon == null || polygon.Length < 3)
            {
                Gizmos.DrawWireCube(Vector3.zero, size);
                return;
            }

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Length];
                Gizmos.DrawLine(
                    new Vector3(current.x * size.x * 0.5f, 0f, current.y * size.z * 0.5f),
                    new Vector3(next.x * size.x * 0.5f, 0f, next.y * size.z * 0.5f));
            }
        }
#endif
    }
}
