using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    /// <summary>
    /// Flow override region: wherever this box overlaps water that won the query selection,
    /// the sampled flow is replaced by the zone's direction/speed. Baked into
    /// <see cref="WorldBuilder.Runtime.Water.CurrentZoneData"/> by the water baker.
    /// </summary>
    public sealed class WaterCurrentZone : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new Vector3(8f, 4f, 8f);
        [SerializeField] private Vector3 direction = Vector3.forward;
        [SerializeField] private float strength = 1f;
        [SerializeField] private int priority = 10;

        public Vector3 Size
        {
            get => size;
            set => size = value;
        }

        public Vector3 Direction
        {
            get => direction;
            set => direction = value;
        }

        public float Strength
        {
            get => strength;
            set => strength = Mathf.Max(0f, value);
        }

        public int Priority
        {
            get => priority;
            set => priority = value;
        }

        public Bounds GetWorldBounds() => new Bounds(transform.position,
            Vector3.Max(Vector3.Scale(size, Abs(transform.lossyScale)), Vector3.one * 0.01f));

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
