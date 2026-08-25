using UnityEngine;

namespace WorldBuilder.Runtime.Editing
{
    /// <summary>
    /// Minimal runtime world editor: raycasts from a camera, previews the placement ghost,
    /// and places/removes structures through <see cref="RuntimePlacementService"/>.
    /// Attach to any scene object with a Camera (or assign one), then call
    /// <see cref="UpdateEditing"/> from your input loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeWorldEditor : MonoBehaviour
    {
        public enum EditMode
        {
            Place,
            Remove,
            Move
        }

        [SerializeField] private Camera sourceCamera;
        [SerializeField] private GameObject[] placeablePrefabs = System.Array.Empty<GameObject>();
        [SerializeField] private int selectedPrefabIndex;
        [SerializeField] private EditMode mode = EditMode.Place;
        [SerializeField] private float maxPlacementDistance = 200f;
        [SerializeField] private float removeRadius = 2f;
        [SerializeField] private bool alignToNormal = true;
        [SerializeField] private bool snapToGrid;
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private Color ghostColor = new Color(0.3f, 1f, 0.5f, 0.45f);

        private GameObject ghost;
        private RuntimePlacementService.PlacementRecord grabbed;

        public EditMode Mode { get => mode; set => SetMode(value); }
        public int SelectedPrefabIndex
        {
            get => selectedPrefabIndex;
            set => selectedPrefabIndex = Mathf.Clamp(value, 0, Mathf.Max(0, placeablePrefabs.Length - 1));
        }
        public int PlacementCount { get; private set; }

        public event System.Action<RuntimePlacementService.PlacementRecord> Placed;
        public event System.Action<RuntimePlacementService.PlacementRecord> Removed;

        private void OnEnable()
        {
            RuntimePlacementService.Placed += OnPlaced;
            RuntimePlacementService.Removed += OnRemoved;
        }

        private void OnDisable()
        {
            RuntimePlacementService.Placed -= OnPlaced;
            RuntimePlacementService.Removed -= OnRemoved;
            DestroyGhost();
        }

        public void UpdateEditing()
        {
            Camera camera = sourceCamera != null ? sourceCamera : Camera.main;
            if (camera == null || placeablePrefabs.Length == 0) return;

            if (!TryGetSurface(camera, out Vector3 position, out Vector3 normal)) 
            {
                DestroyGhost();
                return;
            }

            if (mode == EditMode.Place) UpdateGhost(position, normal);
            else DestroyGhost();
        }

        public bool TryPlaceAtScreenPoint(Vector2 screenPoint)
        {
            Camera camera = sourceCamera != null ? sourceCamera : Camera.main;
            if (camera == null || placeablePrefabs.Length == 0) return false;
            GameObject prefab = placeablePrefabs[SelectedPrefabIndex];
            if (prefab == null) return false;

            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance, surfaceMask)) return false;

            Quaternion rotation = alignToNormal && mode == EditMode.Place
                ? Quaternion.FromToRotation(Vector3.up, hit.normal)
                : Quaternion.identity;
            Vector3 position = Snap(hit.point);
            RuntimePlacementService.Place(prefab, position, rotation);
            return true;
        }

        public bool TryRemoveAtPosition(Vector3 position)
        {
            return RuntimePlacementService.RemoveNearest(position, removeRadius, out _);
        }

        /// <summary>
        /// Move mode: grabs the placed instance under the screen point. While grabbed,
        /// <see cref="UpdateGrab"/> follows the camera-center surface point; release drops it.
        /// </summary>
        public bool TryGrabAtScreenPoint(Vector2 screenPoint)
        {
            Camera camera = sourceCamera != null ? sourceCamera : Camera.main;
            if (camera == null) return false;
            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance, surfaceMask)) return false;
            return RuntimePlacementService.TryGetInstanceRecord(hit.collider != null
                ? hit.collider.gameObject
                : hit.transform.gameObject, out grabbed);
        }

        public void UpdateGrab()
        {
            if (grabbed?.Instance == null)
            {
                ReleaseGrab();
                return;
            }
            Camera camera = sourceCamera != null ? sourceCamera : Camera.main;
            if (camera == null) return;
            if (!TryGetSurface(camera, out Vector3 position, out Vector3 normal)) return;

            grabbed.Instance.transform.position = position;
            if (alignToNormal) 
                grabbed.Instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        }

        public void ReleaseGrab()
        {
            grabbed = null;
        }

        public bool IsGrabbing => grabbed != null;

        private bool TryGetSurface(Camera camera, out Vector3 position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance, surfaceMask)) return false;
            position = Snap(hit.point);
            normal = hit.normal;
            return true;
        }

        private Vector3 Snap(Vector3 position)
        {
            if (!snapToGrid || gridSize <= 0f) return position;
            return new Vector3(
                Mathf.Round(position.x / gridSize) * gridSize,
                position.y,
                Mathf.Round(position.z / gridSize) * gridSize);
        }

        private void UpdateGhost(Vector3 position, Vector3 normal)
        {
            GameObject prefab = placeablePrefabs[SelectedPrefabIndex];
            if (prefab == null)
            {
                DestroyGhost();
                return;
            }

            if (ghost == null || ghost.name != "__WB_Ghost_" + prefab.name)
            {
                DestroyGhost();
                ghost = Instantiate(prefab);
                ghost.name = "__WB_Ghost_" + prefab.name;
                PrepareGhost(ghost.transform);
            }

            ghost.transform.position = position;
            ghost.transform.rotation = alignToNormal
                ? Quaternion.FromToRotation(Vector3.up, normal)
                : Quaternion.identity;
        }

        private void PrepareGhost(Transform target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                Material[] ghostMaterials = new Material[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                {
                    Shader shader = materials[m] != null ? materials[m].shader : Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    Material ghostMaterial = new Material(shader);
                    Color baseColor = ghostColor;
                    if (ghostMaterial.HasProperty("_BaseColor")) ghostMaterial.SetColor("_BaseColor", baseColor);
                    if (ghostMaterial.HasProperty("_Color")) ghostMaterial.SetColor("_Color", baseColor);
                    ghostMaterials[m] = ghostMaterial;
                }

                renderers[i].sharedMaterials = ghostMaterials;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        }

        private void DestroyGhost()
        {
            if (ghost == null) return;
            Destroy(ghost);
            ghost = null;
        }

        private void SetMode(EditMode value)
        {
            mode = value;
            if (value != EditMode.Place) DestroyGhost();
        }

        private void OnPlaced(RuntimePlacementService.PlacementRecord record)
        {
            PlacementCount++;
            Placed?.Invoke(record);
        }

        private void OnRemoved(RuntimePlacementService.PlacementRecord record)
        {
            PlacementCount = Mathf.Max(0, PlacementCount - 1);
            Removed?.Invoke(record);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = mode == EditMode.Place ? ghostColor : Color.red;
            Gizmos.DrawWireSphere(transform.position + transform.forward * Mathf.Min(4f, maxPlacementDistance), removeRadius);
        }
    }
}
