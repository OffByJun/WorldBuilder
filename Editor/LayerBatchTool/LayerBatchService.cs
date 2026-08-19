using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.LayerBatchTool
{
    public static class LayerBatchService
    {
        public static int Apply(int sourceLayer, int targetLayer, bool sceneWide, bool includeChildren)
        {
            List<GameObject> roots = SceneObjectCollector.CollectGameObjects(sceneWide);
            int changed = 0;

            for (int i = 0; i < roots.Count; i++)
            {
                changed += ApplyTo(roots[i], sourceLayer, targetLayer, includeChildren);
            }

            return changed;
        }

        private static int ApplyTo(GameObject target, int sourceLayer, int targetLayer, bool includeChildren)
        {
            int changed = 0;

            if (includeChildren)
            {
                Transform[] transforms = target.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    changed += ChangeLayer(transforms[i].gameObject, sourceLayer, targetLayer);
                }

                return changed;
            }

            changed += ChangeLayer(target, sourceLayer, targetLayer);
            return changed;
        }

        private static int ChangeLayer(GameObject target, int sourceLayer, int targetLayer)
        {
            if (target.layer != sourceLayer)
            {
                return 0;
            }

            Undo.RecordObject(target, "Layer Batch");
            target.layer = targetLayer;
            EditorUtility.SetDirty(target);
            return 1;
        }
    }
}
