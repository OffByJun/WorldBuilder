using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.TextureAtlasTool
{
    public struct AtlasResult
    {
        public bool success;
        public string message;
        public Rect[] uvRects;
    }

    public static class TextureAtlasService
    {
        public static AtlasResult Generate(List<Texture2D> textures, int size, string outputPath)
        {
            AtlasResult result = new AtlasResult { uvRects = new Rect[0] };

            if (textures == null || textures.Count == 0)
            {
                result.message = "No textures.";
                return result;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                result.message = "Output path is empty.";
                return result;
            }

            List<Texture2D> valid = new List<Texture2D>();
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] != null)
                {
                    valid.Add(textures[i]);
                }
            }

            if (valid.Count == 0)
            {
                result.message = "No valid textures.";
                return result;
            }

            Texture2D atlas = new Texture2D(size, size);
            Rect[] rects = atlas.PackTextures(valid.ToArray(), 2, size);

            byte[] png = atlas.EncodeToPNG();
            Object.DestroyImmediate(atlas);

            if (png == null)
            {
                result.message = "Encoding failed.";
                return result;
            }

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, png);
            AssetDatabase.Refresh();

            result.success = true;
            result.uvRects = rects;
            result.message = "Saved: " + outputPath;
            return result;
        }
    }
}
