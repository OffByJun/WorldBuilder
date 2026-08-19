using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    internal static class EnvironmentAssetWriter
    {
        private const string Root = "Assets/WorldBuilder/Environment";

        public static GameObject CreateVariant(Mesh mesh, Material material, EnvironmentType type, string baseName, int index)
        {
            string typeFolder = EnsureFolder(Root + "/" + type);
            string meshFolder = EnsureFolder(typeFolder + "/Meshes");

            string safeName = baseName + "_" + index;
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/" + safeName + "_Mesh.asset");
            AssetDatabase.CreateAsset(mesh, meshPath);

            GameObject go = new GameObject(safeName);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(typeFolder + "/" + safeName + ".prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public static Material GetOrCreateMaterial(EnvironmentType type, Color color)
        {
            string typeFolder = EnsureFolder(Root + "/" + type);
            string matPath = typeFolder + "/" + type + "_Material.mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, matPath);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        public static void Register(PrefabBrushSettings settings, GameObject prefab, EnvironmentType type)
        {
            if (prefab == null)
            {
                return;
            }

            Undo.RecordObject(settings, "Register Environment Prefab");
            settings.prefabEntries.Add(new PrefabEntry { prefab = prefab, weight = 1f, envType = type });
            EditorUtility.SetDirty(settings);
        }

        private static string EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return path;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
            return path;
        }
    }
}
