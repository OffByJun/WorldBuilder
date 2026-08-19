using System;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor
{
    /// <summary>
    /// Caches a scene-wide component scan so overlays do not call FindObjectsByType on every
    /// scene view event. Invalidated by hierarchy changes and by a short maximum age, which
    /// covers component additions that do not raise a hierarchy event.
    /// </summary>
    public sealed class SceneComponentCache<T> where T : Component
    {
        private const double MaxAgeSeconds = 1.0;

        private T[] items = Array.Empty<T>();
        private double lastScan = double.NegativeInfinity;
        private bool hooked;

        public void Hook()
        {
            if (!hooked)
            {
                EditorApplication.hierarchyChanged += Invalidate;
                Undo.undoRedoPerformed += Invalidate;
                hooked = true;
            }
            Invalidate();
        }

        public void Invalidate() => lastScan = double.NegativeInfinity;

        public T[] Items
        {
            get
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - lastScan > MaxAgeSeconds)
                {
                    items = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    lastScan = now;
                }
                return items;
            }
        }
    }
}
