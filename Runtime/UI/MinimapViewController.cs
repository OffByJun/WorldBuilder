using UnityEngine;
using UnityEngine.UI;

namespace WorldBuilder.Runtime.UI
{
    /// <summary>
    /// Displays a baked minimap texture and tracks markers: the followed transform
    /// (usually the player) plus optional WorldDataSnapshot record pins.
    /// North-up mapping: world XZ → minimap UV, matching MinimapBakerTool output.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class MinimapViewController : MonoBehaviour
    {
        [SerializeField] private Texture minimapTexture;
        [SerializeField] private Vector3 worldCenter = Vector3.zero;
        [SerializeField] private float worldExtent = 256f;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private Transform followTarget;
        [SerializeField] private bool useCameraAsFallback = true;
        [SerializeField] private float markerPadding = 4f;

        private RawImage image;

        public Texture MinimapTexture
        {
            get => minimapTexture;
            set
            {
                minimapTexture = value;
                if (image != null) image.texture = value;
            }
        }

        public Transform FollowTarget
        {
            get => followTarget;
            set => followTarget = value;
        }

        private void Awake()
        {
            image = GetComponent<RawImage>();
            if (minimapTexture != null) image.texture = minimapTexture;
        }

        private void LateUpdate()
        {
            if (playerMarker == null) return;
            Transform target = followTarget;
            if (target == null && useCameraAsFallback && Camera.main != null) target = Camera.main.transform;
            if (target == null) return;

            Vector2 anchored;
            if (TryWorldToAnchored(target.position, out anchored))
            {
                playerMarker.anchoredPosition = anchored;
                playerMarker.gameObject.SetActive(true);
            }
            else
            {
                playerMarker.gameObject.SetActive(false);
            }
        }

        /// <summary>Places an arbitrary marker (e.g., a POI pin) on the minimap.</summary>
        public bool TryWorldToAnchored(Vector3 worldPosition, out Vector2 anchored)
        {
            RectTransform area = image.rectTransform;
            float width = area.rect.width - markerPadding * 2f;
            float height = area.rect.height - markerPadding * 2f;
            if (worldExtent <= 0f || width <= 0f || height <= 0f)
            {
                anchored = default;
                return false;
            }

            float u = (worldPosition.x - (worldCenter.x - worldExtent * 0.5f)) / worldExtent;
            float v = (worldPosition.z - (worldCenter.z - worldExtent * 0.5f)) / worldExtent;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                anchored = default;
                return false;
            }

            anchored = new Vector2(
                markerPadding + u * width,
                markerPadding + v * height);
            return true;
        }
    }
}
