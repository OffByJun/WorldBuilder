using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    public sealed class LakeWaterBody : WaterBodyAuthoring
    {
        [SerializeField] private List<Vector3> polygon = new List<Vector3>
        {
            new Vector3(-5f, 0f, -5f), new Vector3(-5f, 0f, 5f),
            new Vector3(5f, 0f, 5f), new Vector3(5f, 0f, -5f)
        };
        [SerializeField, Min(0.01f)] private float depth = 4f;

        public IList<Vector3> Polygon => polygon;
        public float Depth { get => depth; set => depth = Mathf.Max(0.01f, value); }

        protected override void OnValidate()
        {
            base.OnValidate();
            depth = Mathf.Max(0.01f, depth);
        }
    }
}
