using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor
{
    public static class SceneRaycaster
    {
        public static bool TryRaycast(Vector2 guiPosition, out RaycastHit hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            return Physics.Raycast(ray, out hit, Mathf.Infinity);
        }

        public static bool TryRaycastDown(Vector3 origin, out RaycastHit hit)
        {
            Ray ray = new Ray(origin + Vector3.up * 100f, Vector3.down);
            return Physics.Raycast(ray, out hit, 1000f);
        }
    }
}
