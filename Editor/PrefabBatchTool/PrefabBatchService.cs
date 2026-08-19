using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBatchTool
{
    public static class PrefabBatchService
    {
        public static int ApplyOverrides(bool sceneWide)
        {
            List<GameObject> roots = CollectPrefabRoots(sceneWide);
            for (int i = 0; i < roots.Count; i++)
            {
                Undo.RegisterFullObjectHierarchyUndo(roots[i], "Apply Overrides");
                PrefabUtility.ApplyPrefabInstance(roots[i], InteractionMode.UserAction);
            }

            return roots.Count;
        }

        public static int RevertOverrides(bool sceneWide)
        {
            List<GameObject> roots = CollectPrefabRoots(sceneWide);
            for (int i = 0; i < roots.Count; i++)
            {
                Undo.RegisterFullObjectHierarchyUndo(roots[i], "Revert Overrides");
                PrefabUtility.RevertPrefabInstance(roots[i], InteractionMode.UserAction);
            }

            return roots.Count;
        }

        private static List<GameObject> CollectPrefabRoots(bool sceneWide)
        {
            List<GameObject> source = SceneObjectCollector.CollectGameObjects(sceneWide);
            List<GameObject> roots = new List<GameObject>();

            for (int i = 0; i < source.Count; i++)
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(source[i]))
                {
                    continue;
                }

                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(source[i]);
                if (root != null && !roots.Contains(root))
                {
                    roots.Add(root);
                }
            }

            return roots;
        }
    }
}
