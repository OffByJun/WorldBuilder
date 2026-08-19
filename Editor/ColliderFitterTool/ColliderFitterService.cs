using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.ColliderFitterTool
{
    public enum ColliderFitType
    {
        Box,
        Sphere,
        Capsule
    }

    public static class ColliderFitterService
    {
        public static bool TryGetBounds(GameObject target, out Bounds bounds)
        {
            bounds = new Bounds();
            if (target == null)
            {
                return false;
            }

            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            bounds = filter.sharedMesh.bounds;
            return true;
        }

        public static void Fit(GameObject target, ColliderFitType type, bool replaceExisting)
        {
            if (!TryGetBounds(target, out Bounds bounds))
            {
                return;
            }

            if (replaceExisting)
            {
                Collider[] existing = target.GetComponents<Collider>();
                for (int i = 0; i < existing.Length; i++)
                {
                    Undo.DestroyObjectImmediate(existing[i]);
                }
            }

            switch (type)
            {
                case ColliderFitType.Box:
                {
                    BoxCollider collider = Undo.AddComponent<BoxCollider>(target);
                    collider.center = bounds.center;
                    collider.size = bounds.size;
                    break;
                }
                case ColliderFitType.Sphere:
                {
                    SphereCollider collider = Undo.AddComponent<SphereCollider>(target);
                    collider.center = bounds.center;
                    collider.radius = bounds.extents.magnitude;
                    break;
                }
                case ColliderFitType.Capsule:
                {
                    CapsuleCollider collider = Undo.AddComponent<CapsuleCollider>(target);
                    collider.center = bounds.center;
                    collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    collider.height = bounds.size.y;
                    collider.direction = 1;
                    break;
                }
            }

            EditorUtility.SetDirty(target);
        }
    }
}
