using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldBuilder.Editor.MaterialCompareTool
{
    public struct MaterialComparisonRow
    {
        public string propertyName;
        public string leftValue;
        public string rightValue;
        public bool different;
    }

    public static class MaterialCompareService
    {
        public static List<MaterialComparisonRow> Compare(Material left, Material right)
        {
            List<MaterialComparisonRow> rows = new List<MaterialComparisonRow>();
            if (left == null || right == null)
            {
                return rows;
            }

            List<string> names = new List<string>();
            CollectNames(left, names);
            CollectNames(right, names);

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string leftValue = ValueOf(left, name);
                string rightValue = ValueOf(right, name);

                rows.Add(new MaterialComparisonRow
                {
                    propertyName = name,
                    leftValue = leftValue,
                    rightValue = rightValue,
                    different = leftValue != rightValue
                });
            }

            return rows;
        }

        private static void CollectNames(Material material, List<string> names)
        {
            if (material.shader == null)
            {
                return;
            }

            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        private static string ValueOf(Material material, string propertyName)
        {
            if (material.shader == null)
            {
                return "-";
            }

            Shader shader = material.shader;
            int index = shader.FindPropertyIndex(propertyName);
            if (index < 0)
            {
                return "-";
            }

            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                    return material.GetColor(propertyName).ToString();
                case ShaderPropertyType.Vector:
                    return material.GetVector(propertyName).ToString();
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return material.GetFloat(propertyName).ToString("F4");
                case ShaderPropertyType.Int:
                    return material.GetInteger(propertyName).ToString();
                case ShaderPropertyType.Texture:
                    Texture texture = material.GetTexture(propertyName);
                    return texture != null ? texture.name : "(none)";
                default:
                    return "-";
            }
        }
    }
}
