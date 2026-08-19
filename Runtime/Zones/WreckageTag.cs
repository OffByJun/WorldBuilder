using UnityEngine;

namespace WorldBuilder.Runtime.Zones
{
    public sealed class WreckageTag : MonoBehaviour
    {
        [SerializeField] private int logNumber;

        public int LogNumber
        {
            get => logNumber;
            set => logNumber = value;
        }
    }
}
