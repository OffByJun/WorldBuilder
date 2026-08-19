using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor
{
    public static class SceneObjectCollector
    {
        public static List<GameObject> CollectGameObjects(bool sceneWide)
        {
            List<GameObject> result = new List<GameObject>();

            if (sceneWide)
            {
                GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    result.Add(all[i]);
                }

                return result;
            }

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null)
                {
                    result.Add(selection[i]);
                }
            }

            return result;
        }

        public static List<T> CollectComponents<T>(bool sceneWide) where T : Component
        {
            List<T> result = new List<T>();

            if (sceneWide)
            {
                T[] all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    result.Add(all[i]);
                }

                return result;
            }

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] == null)
                {
                    continue;
                }

                selection[i].GetComponentsInChildren(true, BufferOf<T>.List);
                for (int j = 0; j < BufferOf<T>.List.Count; j++)
                {
                    result.Add(BufferOf<T>.List[j]);
                }
            }

            return result;
        }

        private static class BufferOf<T> where T : Component
        {
            public static readonly List<T> List = new List<T>();
        }
    }
}
