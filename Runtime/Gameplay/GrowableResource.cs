using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Gameplay
{
    /// <summary>
    /// Stage-based growth for resource nodes: sprout → … → mature, driven by world time.
    /// Visuals are either child GameObjects (index = stage) or prefabs instantiated under
    /// an anchor. Feeds <see cref="HarvestableNode"/> respawn loops.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrowableResource : MonoBehaviour
    {
        [SerializeField] private List<GameObject> stages = new List<GameObject>();
        [Min(1f)] [SerializeField] private float secondsPerStage = 600f;        [SerializeField] private bool growOnlyWhenVisible = true;
        [Tooltip("Start fully grown (placed by scatter as mature).")]
        [SerializeField] private bool startMature = true;

        public int CurrentStage { get; private set; }
        public int StageCount => stages.Count;
        public event Action<int> StageChanged;

        /// <summary>Default stage duration for newly placed nodes; mods may override.</summary>
        public static float DefaultsSecondsPerStage
        {
            get => defaultSecondsPerStage;
            set => defaultSecondsPerStage = Mathf.Max(1f, value);
        }
        private static float defaultSecondsPerStage = 600f;

        private float stageTimer;

        public float Growth01 =>
            StageCount <= 1 ? 1f : (CurrentStage + Mathf.Clamp01(stageTimer / secondsPerStage)) / (StageCount - 1);

        private void OnEnable()
        {
            // Adopt mod-driven default when the instance still carries the legacy value.
            if (Mathf.Approximately(secondsPerStage, 600f))
                secondsPerStage = DefaultsSecondsPerStage;
            SetStage(startMature ? StageCount - 1 : 0);
        }

        public void SetStage(int index)
        {
            CurrentStage = Mathf.Clamp(index, 0, Math.Max(0, StageCount - 1));
            ApplyStage();
            stageTimer = 0f;
            StageChanged?.Invoke(CurrentStage);
        }

        /// <summary>Manual tick for tests and time-skips; returns true when a stage advanced.</summary>
        public bool Advance(float deltaTime)
        {
            if (StageCount <= 1 || CurrentStage >= StageCount - 1) return false;
            stageTimer += Mathf.Max(0f, deltaTime);
            if (stageTimer < secondsPerStage) return false;
            stageTimer = 0f;
            SetStage(CurrentStage + 1);
            return true;
        }

        private void Update()
        {
            if (!growOnlyWhenVisible || IsVisibleToCamera())
                Advance(Time.deltaTime);
        }

        private bool IsVisibleToCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return true;
            Vector3 viewport = camera.WorldToViewportPoint(transform.position);
            return viewport.z > 0f && viewport.x is >= -0.2f and <= 1.2f && viewport.y is >= -0.2f and <= 1.2f;
        }

        private void ApplyStage()
        {
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] == null) continue;
                if (stages[i].transform.parent == transform || stages[i].scene.IsValid() == false)
                    stages[i].SetActive(i == CurrentStage);
                else
                    stages[i].SetActive(i == CurrentStage);
            }
        }

        // ---- HarvestableNode lives in the same file for cohesion ----

        [Serializable]
        public struct ItemYield
        {
            public string itemId;
            public int minAmount;
            public int maxAmount;
        }
    }

    /// <summary>
    /// Interaction endpoint on top of <see cref="GrowableResource"/>: harvest yields,
    /// optional harvest duration (progress handled by caller), automatic respawn by
    /// resetting the growth loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestableNode : MonoBehaviour
    {
        [SerializeField] private GrowableResource growth;
        [SerializeField] private List<GrowableResource.ItemYield> yields = new List<GrowableResource.ItemYield>
        {
            new GrowableResource.ItemYield { itemId = "wood", minAmount = 1, maxAmount = 3 }
        };
        [Min(0f)] [SerializeField] private float harvestDurationSeconds;
        [SerializeField] private bool destroyOnHarvest;

        /// <summary>Default yield table for newly placed nodes; mods may override.</summary>
        public static List<GrowableResource.ItemYield> DefaultYields { get; set; } =
            new List<GrowableResource.ItemYield>
            {
                new GrowableResource.ItemYield { itemId = "wood", minAmount = 1, maxAmount = 3 }
            };

        private void OnEnable()
        {
            if (yields == null || yields.Count == 0)
                yields = DefaultYields;
            ResolveGrowth();
        }

        public IReadOnlyList<GrowableResource.ItemYield> Yields => yields;
        public float HarvestDuration => harvestDurationSeconds;

        public bool ReadyForHarvest
        {
            get
            {
                GrowableResource source = ResolveGrowth();
                return source == null || source.StageCount <= 1 ||
                       source.CurrentStage >= source.StageCount - 1;
            }
        }

        public event Action<IReadOnlyList<GrowableResource.ItemYield>> Harvested;
        public event Action Respawned;

        /// <summary>Lazy sibling binding — edit-mode tests never run OnEnable.</summary>
        private GrowableResource ResolveGrowth()
        {
            if (growth == null) growth = GetComponent<GrowableResource>();
            return growth;
        }

        /// <summary>
        /// Attempts to harvest. Only succeeds when the node is ready; returns the rolled
        /// yields and resets/destroys the node.
        /// </summary>
        public bool TryHarvest(out List<GrowableResource.ItemYield> rolled)
        {
            rolled = null;
            if (!ReadyForHarvest) return false;

            rolled = new List<GrowableResource.ItemYield>(yields.Count);
            foreach (GrowableResource.ItemYield entry in yields)
            {
                int max = Math.Max(entry.minAmount, entry.maxAmount);
                rolled.Add(new GrowableResource.ItemYield
                {
                    itemId = entry.itemId,
                    minAmount = entry.minAmount,
                    maxAmount = max,
                });
                // Deterministic roll happens here so callers share one code path:
                rolled[^1] = new GrowableResource.ItemYield
                {
                    itemId = entry.itemId,
                    minAmount = entry.minAmount,
                    maxAmount = UnityEngine.Random.Range(entry.minAmount, max + 1)
                };
            }

            Harvested?.Invoke(rolled);

            if (destroyOnHarvest)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
            else if (ResolveGrowth() != null)
            {
                growth.SetStage(0);
                Respawned?.Invoke();
            }
            return true;
        }
    }
}
