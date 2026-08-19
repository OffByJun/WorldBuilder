using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.MaterialBatchTool
{
    public static class MaterialBatchService
    {
        public static void ReplaceMaterial(Material src, Material dst, bool sceneWide)
        {
            if (src == null || dst == null)
            {
                return;
            }

            Renderer[] renderers = CollectRenderers(sceneWide);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] shared = renderer.sharedMaterials;
                bool changed = false;

                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] == src)
                    {
                        shared[m] = dst;
                        changed = true;
                    }
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Replace Material");
                    renderer.sharedMaterials = shared;
                    UndoHistory.Push("Replace Material");
                }
            }
        }

        public static void ReplaceShader(Shader src, Shader dst)
        {
            if (src == null || dst == null)
            {
                return;
            }

            HashSet<Material> materials = CollectSceneMaterials();
            foreach (Material material in materials)
            {
                if (material.shader == src)
                {
                    Undo.RecordObject(material, "Replace Shader");
                    material.shader = dst;
                    UndoHistory.Push("Replace Shader");
                }
            }
        }

        public static void ApplyProperty(Shader target, string propName, object value)
        {
            if (target == null || string.IsNullOrEmpty(propName) || value == null)
            {
                return;
            }

            HashSet<Material> materials = CollectSceneMaterials();
            foreach (Material material in materials)
            {
                if (material.shader != target)
                {
                    continue;
                }

                Undo.RecordObject(material, "Apply Material Property");

                if (value is float floatValue)
                {
                    material.SetFloat(propName, floatValue);
                }
                else if (value is Color colorValue)
                {
                    material.SetColor(propName, colorValue);
                }
                else if (value is Vector4 vectorValue)
                {
                    material.SetVector(propName, vectorValue);
                }

                UndoHistory.Push("Apply Material Property");
            }
        }

        public static void AssignMaterials(IReadOnlyList<Renderer> renderers, IReadOnlyList<Material> materials)
        {
            if (renderers == null || materials == null || materials.Count == 0)
            {
                return;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] assigned = new Material[renderer.sharedMaterials.Length];
                for (int m = 0; m < assigned.Length; m++)
                {
                    assigned[m] = materials[m % materials.Count];
                }

                Undo.RecordObject(renderer, "Assign Biome Preset");
                renderer.sharedMaterials = assigned;
                UndoHistory.Push("Assign Biome Preset");
            }
        }

        private static Renderer[] CollectRenderers(bool sceneWide)
        {
            if (sceneWide)
            {
                return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            }

            List<Renderer> selected = new List<Renderer>();
            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                selected.AddRange(selection[i].GetComponentsInChildren<Renderer>());
            }

            return selected.ToArray();
        }

        private static HashSet<Material> CollectSceneMaterials()
        {
            HashSet<Material> result = new HashSet<Material>();
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] shared = renderers[i].sharedMaterials;
                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] != null)
                    {
                        result.Add(shared[m]);
                    }
                }
            }

            return result;
        }
    }
}
