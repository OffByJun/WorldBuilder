using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class VisibilityZone : MonoBehaviour
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float visibility = 30f;
        [SerializeField] private Color fogColor = Color.gray;

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public float Visibility
        {
            get => visibility;
            set => visibility = value;
        }

        public Color FogColor
        {
            get => fogColor;
            set => fogColor = value;
        }
    }
}
