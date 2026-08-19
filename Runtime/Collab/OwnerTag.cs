using UnityEngine;

namespace WorldBuilder.Runtime.Collab
{
    public sealed class OwnerTag : MonoBehaviour
    {
        [SerializeField] private string ownerName;
        [SerializeField] private Color ownerColor = Color.cyan;

        public string OwnerName
        {
            get => ownerName;
            set => ownerName = value;
        }

        public Color OwnerColor
        {
            get => ownerColor;
            set => ownerColor = value;
        }
    }
}
