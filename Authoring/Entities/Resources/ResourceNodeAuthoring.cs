using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Resources.Authoring
{
    [Serializable]
    public struct ResourceDropAuthoring
    {
        public int ItemId;
        [Min(0)] public int MinimumCount;
        [Min(0)] public int MaximumCount;
        [Range(0f, 1f)] public float Probability;
    }

    [RequireComponent(typeof(WorldBuilder.Entities.Authoring.WorldEntityAuthoring))]
    [AddComponentMenu("WorldBuilder/Entities/Resource Node")]
    public sealed class ResourceNodeAuthoring : MonoBehaviour
    {
        [SerializeField] private string displayName = "Resource";
        [Min(0.01f), SerializeField] private float health = 100f;
        [Min(0f), SerializeField] private float hitCooldownSeconds = 0.25f;
        [Min(0f), SerializeField] private float respawnSeconds;
        [SerializeField] private HarvestMethod allowedMethods = HarvestMethod.Hand;
        [Tooltip("-1 allows any tool matching the method/tier/power requirements.")]
        [SerializeField] private int requiredToolItemId = -1;
        [Min(0), SerializeField] private int minimumToolTier;
        [Min(0f), SerializeField] private float minimumToolPower;
        [Tooltip("WorldEntity prefab id of the generic dropped-item prefab.")]
        [SerializeField] private int droppedItemPrefabId;
        [SerializeField] private uint randomSeed = 1;
        [SerializeField] private ResourceDropAuthoring[] drops = Array.Empty<ResourceDropAuthoring>();

        private void OnValidate()
        {
            health = Mathf.Max(0.01f, health);
            hitCooldownSeconds = Mathf.Max(0f, hitCooldownSeconds);
            respawnSeconds = Mathf.Max(0f, respawnSeconds);
            minimumToolTier = Mathf.Clamp(minimumToolTier, 0, byte.MaxValue);
            minimumToolPower = Mathf.Max(0f, minimumToolPower);
            if (allowedMethods == HarvestMethod.None) allowedMethods = HarvestMethod.Hand;
            if (randomSeed == 0) randomSeed = 1;
        }

        private sealed class ResourceNodeBaker : Baker<ResourceNodeAuthoring>
        {
            public override void Bake(ResourceNodeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new ResourceNode
                {
                    DisplayName = new FixedString64Bytes(authoring.displayName ?? string.Empty),
                    MaxHealth = authoring.health,
                    Health = authoring.health,
                    HitCooldownSeconds = authoring.hitCooldownSeconds,
                    RespawnSeconds = authoring.respawnSeconds,
                    AllowedMethods = authoring.allowedMethods,
                    RequiredToolItemId = authoring.requiredToolItemId,
                    MinimumToolTier = (byte)authoring.minimumToolTier,
                    MinimumToolPower = authoring.minimumToolPower,
                    DroppedItemPrefabId = authoring.droppedItemPrefabId,
                    RandomSeed = authoring.randomSeed == 0 ? 1u : authoring.randomSeed
                });
                AddComponent(entity, new ResourceRespawnState());
                DynamicBuffer<ResourceDrop> buffer = AddBuffer<ResourceDrop>(entity);
                ResourceDropAuthoring[] drops = authoring.drops ?? Array.Empty<ResourceDropAuthoring>();
                for (int i = 0; i < drops.Length; i++)
                {
                    ResourceDropAuthoring drop = drops[i];
                    int minimum = Mathf.Max(0, drop.MinimumCount);
                    int maximum = Mathf.Max(minimum, drop.MaximumCount);
                    if (maximum == 0 || drop.Probability <= 0f) continue;
                    buffer.Add(new ResourceDrop
                    {
                        ItemId = drop.ItemId,
                        MinimumCount = minimum,
                        MaximumCount = maximum,
                        Probability = Mathf.Clamp01(drop.Probability)
                    });
                }
            }
        }
    }
}
