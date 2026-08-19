using Unity.Entities;
using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    /// <summary>
    /// Generic repeatable work site for gathering, hauling, craft assist and feeding.
    /// Farming plots use CropPlotAuthoring instead because they grow on a timer.
    /// </summary>
    [AddComponentMenu("WorldBuilder/Entities/Creature Work Site")]
    public sealed class CreatureWorkSiteAuthoring : MonoBehaviour
    {
        [SerializeField] private CreatureRole requiredRole = CreatureRole.Gathering;
        [SerializeField] private int outputItemId = -1;
        [Min(1), SerializeField] private int outputCount = 1;
        [Min(0.1f), SerializeField] private float workSeconds = 3f;
        [Min(0.5f), SerializeField] private float interactRadius = 1.5f;
        [Tooltip("Seconds before this site becomes workable again. 0 makes it a one-shot site.")]
        [Min(0f), SerializeField] private float refreshSeconds = 20f;
        [Tooltip("Optional. Restricts this site to creatures settled in that habitat.")]
        [SerializeField] private CreatureHabitatAuthoring habitat;
        [SerializeField] private bool startsReady = true;

        private void OnValidate()
        {
            outputCount = Mathf.Max(1, outputCount);
            workSeconds = Mathf.Max(0.1f, workSeconds);
            interactRadius = Mathf.Max(0.5f, interactRadius);
            refreshSeconds = Mathf.Max(0f, refreshSeconds);
            if (requiredRole == CreatureRole.None) requiredRole = CreatureRole.Gathering;
        }

        private sealed class WorkSiteBaker : Baker<CreatureWorkSiteAuthoring>
        {
            public override void Bake(CreatureWorkSiteAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureWorkSite
                {
                    RequiredRole = authoring.requiredRole,
                    State = authoring.startsReady
                        ? CreatureWorkSiteState.Ready
                        : CreatureWorkSiteState.Growing,
                    Claimant = Entity.Null,
                    Habitat = authoring.habitat != null
                        ? GetEntity(authoring.habitat.gameObject, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    OutputItemId = authoring.outputItemId,
                    OutputCount = Mathf.Max(1, authoring.outputCount),
                    WorkSeconds = authoring.workSeconds,
                    InteractRadius = authoring.interactRadius
                });
                AddComponent<CreatureWorkSiteReady>(entity);
                SetComponentEnabled<CreatureWorkSiteReady>(entity, authoring.startsReady);

                if (authoring.refreshSeconds > 0f)
                    AddComponent(entity, new CreatureWorkSiteRefresh
                    {
                        RefreshSeconds = authoring.refreshSeconds,
                        NextReadyTime = 0d
                    });
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.25f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
#endif
    }
}
