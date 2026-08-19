using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    public sealed class OceanWaterBody : WaterBodyAuthoring
    {
        [SerializeField] private float seaLevel;

        public float SeaLevel { get => seaLevel; set => seaLevel = value; }

        protected override void Reset()
        {
            base.Reset();
            Priority = 0;
        }
    }
}
