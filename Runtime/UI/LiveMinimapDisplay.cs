#if WB_UGUI
using UnityEngine;
using UnityEngine.UI;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Runtime.UI
{
    /// <summary>
    /// Live minimap: bakes a WorldMapBaker overview once, shows it in a RawImage and moves
    /// a player marker each frame. Attach under any Canvas; assign the RawImage + marker.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class LiveMinimapDisplay : MonoBehaviour
    {
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private float chunkSize = 128f;
        [SerializeField] private Vector2 worldOrigin = Vector2.zero;
        [SerializeField] private float worldSizeMeters = 512f;
        [Min(64)] [SerializeField] private int textureSize = 512;
        [SerializeField] private Transform player;
        [SerializeField] private RectTransform marker;
        [SerializeField] private Color markerColor = new Color(1f, 0.35f, 0.3f);
        [SerializeField] private bool bakeOnStart = true;

        private RawImage image;
        private Texture2D baked;

        public void Configure(VoxelStoreAsset target, Vector2 origin, float sizeMeters)
        {
            store = target;
            worldOrigin = origin;
            worldSizeMeters = sizeMeters;
            Rebake();
        }

        private void Start()
        {
            image = GetComponent<RawImage>();
            if (bakeOnStart && store != null) Rebake();
            if (marker != null)
            {
                var markerImage = marker.GetComponent<Image>();
                if (markerImage != null) markerImage.color = markerColor;
            }
        }

        public void Rebake()
        {
            baked = WorldMapBaker.BakeOverviewTexture(store, chunkSize, biomeMap: null,
                worldOrigin, textureSize, worldSizeMeters, seaLevel: 0f,
                includeCaveOverlay: true);
            if (image == null) image = GetComponent<RawImage>();
            if (image != null) image.texture = baked;
        }

        private void LateUpdate()
        {
            if (player == null || marker == null || image == null) return;

            Vector2 normalized = new Vector2(
                Mathf.Clamp01((player.position.x - worldOrigin.x) / worldSizeMeters),
                Mathf.Clamp01((player.position.z - worldOrigin.y) / worldSizeMeters));

            RectTransform map = image.rectTransform;
            Vector2 size = map.rect.size;
            marker.SetParent(map, true);
            marker.anchoredPosition = new Vector2(
                (normalized.x - 0.5f) * size.x,
                (normalized.y - 0.5f) * size.y);
        }

        private void OnDestroy()
        {
            if (baked != null) Destroy(baked);
        }
    }
}
#endif
