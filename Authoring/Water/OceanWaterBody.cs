using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    public sealed class OceanWaterBody : WaterBodyAuthoring
    {
        [SerializeField] private float seaLevel;
        [SerializeField] private Vector3 baseFlowDirection = Vector3.zero;
        [SerializeField] private float baseFlowSpeed;

        public float SeaLevel { get => seaLevel; set => seaLevel = value; }

        /// <summary>Global surface current applied to every underwater ocean sample.</summary>
        public Vector3 BaseFlowDirection
        {
            get => baseFlowDirection;
            set => baseFlowDirection = value;
        }

        public float BaseFlowSpeed
        {
            get => baseFlowSpeed;
            set => baseFlowSpeed = Mathf.Max(0f, value);
        }

        protected override void Reset()
        {
            base.Reset();
            Priority = 0;
        }
    }
}
