using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    [AddComponentMenu("WorldBuilder/Entities/Creature Habitat")]
    public sealed class CreatureHabitatAuthoring : MonoBehaviour
    {
        [SerializeField] private string displayName = "Habitat";
        [SerializeField] private Vector3 size = new Vector3(24f, 10f, 24f);
        [Tooltip("Environments this habitat provides. A creature settles only if all of its required environments are covered.")]
        [SerializeField] private CreatureEnvironmentMask provided = CreatureEnvironmentMask.OpenWater;
        [Tooltip("0 means unlimited.")]
        [Min(0), SerializeField] private int capacity = 8;
        [SerializeField] private bool allowMediumCreatures = true;

        private void OnValidate()
        {
            size = new Vector3(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y), Mathf.Max(1f, size.z));
            capacity = Mathf.Max(0, capacity);
        }

        private sealed class HabitatBaker : Baker<CreatureHabitatAuthoring>
        {
            public override void Bake(CreatureHabitatAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureHabitat
                {
                    DisplayName = new FixedString64Bytes(authoring.displayName ?? string.Empty),
                    HalfExtents = authoring.size * 0.5f,
                    Provided = authoring.provided,
                    Capacity = Mathf.Max(0, authoring.capacity),
                    AllowMedium = (byte)(authoring.allowMediumCreatures ? 1 : 0)
                });
                AddBuffer<CreatureHabitatMember>(entity);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 0.85f, 0.55f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
#endif
    }

    [AddComponentMenu("WorldBuilder/Entities/Creature Crop Plot")]
    public sealed class CropPlotAuthoring : MonoBehaviour
    {
        [SerializeField] private int seedItemId = -1;
        [SerializeField] private int harvestItemId = -1;
        [Min(1), SerializeField] private int harvestCount = 1;
        [Min(0.1f), SerializeField] private float growSeconds = 30f;
        [Min(0.1f), SerializeField] private float workSeconds = 2.5f;
        [Min(0.5f), SerializeField] private float interactRadius = 1.5f;
        [SerializeField] private bool autoReplant = true;
        [Tooltip("Optional. Restricts this plot to creatures settled in that habitat.")]
        [SerializeField] private CreatureHabitatAuthoring habitat;

        private void OnValidate()
        {
            harvestCount = Mathf.Max(1, harvestCount);
            growSeconds = Mathf.Max(0.1f, growSeconds);
            workSeconds = Mathf.Max(0.1f, workSeconds);
            interactRadius = Mathf.Max(0.5f, interactRadius);
        }

        private sealed class CropPlotBaker : Baker<CropPlotAuthoring>
        {
            public override void Bake(CropPlotAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureWorkSite
                {
                    RequiredRole = CreatureRole.Farming,
                    State = CreatureWorkSiteState.Growing,
                    Claimant = Entity.Null,
                    Habitat = authoring.habitat != null
                        ? GetEntity(authoring.habitat.gameObject, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    OutputItemId = authoring.harvestItemId,
                    OutputCount = Mathf.Max(1, authoring.harvestCount),
                    WorkSeconds = authoring.workSeconds,
                    InteractRadius = authoring.interactRadius
                });
                AddComponent<CreatureWorkSiteReady>(entity);
                SetComponentEnabled<CreatureWorkSiteReady>(entity, false);
                AddComponent(entity, new CropPlot
                {
                    SeedItemId = authoring.seedItemId,
                    GrowSeconds = authoring.growSeconds,
                    ReadyTime = 0d,
                    AutoReplant = (byte)(authoring.autoReplant ? 1 : 0)
                });
            }
        }
    }

    [AddComponentMenu("WorldBuilder/Entities/Creature Storage")]
    public sealed class CreatureStorageAuthoring : MonoBehaviour
    {
        [SerializeField] private string displayName = "Storage";
        [Min(1), SerializeField] private int slotCapacity = 16;
        [Min(1), SerializeField] private int stackCapacity = 99;
        [Tooltip("Optional. Ties this storage to one habitat so its workers deliver here.")]
        [SerializeField] private CreatureHabitatAuthoring habitat;

        private void OnValidate()
        {
            slotCapacity = Mathf.Max(1, slotCapacity);
            stackCapacity = Mathf.Max(1, stackCapacity);
        }

        private sealed class StorageBaker : Baker<CreatureStorageAuthoring>
        {
            public override void Bake(CreatureStorageAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureStorage
                {
                    DisplayName = new FixedString64Bytes(authoring.displayName ?? string.Empty),
                    SlotCapacity = Mathf.Max(1, authoring.slotCapacity),
                    StackCapacity = Mathf.Max(1, authoring.stackCapacity),
                    Habitat = authoring.habitat != null
                        ? GetEntity(authoring.habitat.gameObject, TransformUsageFlags.Dynamic)
                        : Entity.Null
                });
                AddBuffer<CreatureStorageSlot>(entity);
            }
        }
    }
}
