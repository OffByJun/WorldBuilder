using UnityEngine;

namespace WorldBuilder.Entities.Creatures.Authoring
{
    public static class CreatureBodyBounds
    {
        public static float CalculateRadius(Transform root, Renderer[] renderers)
        {
            float radius = .5f;
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.localBounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = toRoot.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, sign));
                    radius = Mathf.Max(radius, point.magnitude);
                }
            }
            return radius;
        }
    }
}
