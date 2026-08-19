using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Resources.Authoring
{
    [RequireComponent(typeof(WorldBuilder.Entities.Authoring.WorldEntityAuthoring))]
    [AddComponentMenu("WorldBuilder/Entities/Dropped Item")]
    public sealed class DroppedItemAuthoring : MonoBehaviour
    {
        [SerializeField] private int itemId;
        [Min(1), SerializeField] private int count = 1;
        [SerializeField] private string displayName = "Item";

        private sealed class DroppedItemBaker : Baker<DroppedItemAuthoring>
        {
            public override void Bake(DroppedItemAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new DroppedItem
                {
                    ItemId = authoring.itemId,
                    Count = Mathf.Max(1, authoring.count),
                    DisplayName = new FixedString64Bytes(authoring.displayName ?? string.Empty)
                });
                AddComponent<DroppedItemPendingPickup>(entity);
                SetComponentEnabled<DroppedItemPendingPickup>(entity, false);
            }
        }
    }
}
