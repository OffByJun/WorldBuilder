using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace WorldBuilder.Editor.AssetReportTool
{
    public struct TextureReportRow
    {
        public string name;
        public string resolution;
        public string format;
        public float memoryMb;
    }

    public struct MeshReportRow
    {
        public string name;
        public int vertices;
        public int triangles;
        public float memoryMb;
    }

    public sealed class AssetReport
    {
        public readonly List<TextureReportRow> textures = new List<TextureReportRow>();
        public readonly List<MeshReportRow> meshes = new List<MeshReportRow>();

        public float TotalTextureMb
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < textures.Count; i++)
                {
                    sum += textures[i].memoryMb;
                }

                return sum;
            }
        }

        public float TotalMeshMb
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < meshes.Count; i++)
                {
                    sum += meshes[i].memoryMb;
                }

                return sum;
            }
        }
    }

    public static class AssetReportService
    {
        private const float BytesPerMb = 1024f * 1024f;

        public static AssetReport Scan()
        {
            AssetReport report = new AssetReport();

            HashSet<int> seenTextures = new HashSet<int>();
            HashSet<int> seenMeshes = new HashSet<int>();

            List<Renderer> renderers = SceneObjectCollector.CollectComponents<Renderer>(true);
            for (int i = 0; i < renderers.Count; i++)
            {
                CollectTextures(renderers[i], report, seenTextures);
            }

            List<MeshFilter> filters = SceneObjectCollector.CollectComponents<MeshFilter>(true);
            for (int i = 0; i < filters.Count; i++)
            {
                AddMesh(filters[i].sharedMesh, report, seenMeshes);
            }

            List<SkinnedMeshRenderer> skinned = SceneObjectCollector.CollectComponents<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Count; i++)
            {
                AddMesh(skinned[i].sharedMesh, report, seenMeshes);
            }

            return report;
        }

        private static void CollectTextures(Renderer renderer, AssetReport report, HashSet<int> seen)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null || material.shader == null)
                {
                    continue;
                }

                Shader shader = material.shader;
                int count = shader.GetPropertyCount();
                for (int p = 0; p < count; p++)
                {
                    if (shader.GetPropertyType(p) != ShaderPropertyType.Texture)
                    {
                        continue;
                    }

                    Texture texture = material.GetTexture(shader.GetPropertyName(p));
                    if (texture == null || !seen.Add(texture.GetInstanceID()))
                    {
                        continue;
                    }

                    report.textures.Add(new TextureReportRow
                    {
                        name = texture.name,
                        resolution = texture.width + "x" + texture.height,
                        format = texture is Texture2D tex2D ? tex2D.format.ToString() : texture.GetType().Name,
                        memoryMb = Profiler.GetRuntimeMemorySizeLong(texture) / BytesPerMb
                    });
                }
            }
        }

        private static void AddMesh(Mesh mesh, AssetReport report, HashSet<int> seen)
        {
            if (mesh == null || !seen.Add(mesh.GetInstanceID()))
            {
                return;
            }

            report.meshes.Add(new MeshReportRow
            {
                name = mesh.name,
                vertices = mesh.vertexCount,
                triangles = mesh.triangles.Length / 3,
                memoryMb = Profiler.GetRuntimeMemorySizeLong(mesh) / BytesPerMb
            });
        }

        public static string BuildCsv(AssetReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Type,Name,Detail1,Detail2,MemoryMB");

            for (int i = 0; i < report.textures.Count; i++)
            {
                TextureReportRow row = report.textures[i];
                builder.AppendLine("Texture," + row.name + "," + row.resolution + "," + row.format + "," + row.memoryMb.ToString("F3"));
            }

            for (int i = 0; i < report.meshes.Count; i++)
            {
                MeshReportRow row = report.meshes[i];
                builder.AppendLine("Mesh," + row.name + "," + row.vertices + "," + row.triangles + "," + row.memoryMb.ToString("F3"));
            }

            return builder.ToString();
        }
    }
}
