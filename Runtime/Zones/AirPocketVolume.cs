using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class AirPocketVolume : MonoBehaviour
    {
        [SerializeField] private Vector3 size = Vector3.one * 4f;
        [SerializeField] private string label = string.Empty;

        public Vector3 Size
        {
            get => size;
            set => size = value;
        }

        public string Label
        {
            get => label;
            set => label = value;
        }
    }
}
