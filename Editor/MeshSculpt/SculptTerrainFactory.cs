using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldBuilder.Editor.MeshSculpt
{
    /// <summary>새 지형 조각의 처음 모양.</summary>
    public enum SculptTerrainShape
    {
        Flat,
        Hill,
        Island,
        Basin,
        Ridges
    }

    public struct SculptTerrainSettings
    {
        /// <summary>한 변의 길이(m).</summary>
        public float Size;

        /// <summary>정점 사이 간격(m). 작을수록 촘촘하다.</summary>
        public float Spacing;

        public SculptTerrainShape Shape;

        /// <summary>언덕·섬·능선의 높이, 분지의 깊이(m).</summary>
        public float Height;

        public int Seed;

        /// <summary>비면 <see cref="Parent"/> 아래 첫 메시의 재질, 그것도 없으면 렌더 파이프라인 기본 재질.</summary>
        public Material Material;

        /// <summary>
        /// 넣을 곳. 조각 브러시는 클릭한 오브젝트와 같은 루트 아래 메시만 고치므로, 맵과 이어서 다듬으려면 맵 루트를 고른다.
        /// 레이어·정적 설정도 여기 아래 메시를 따라간다.
        /// </summary>
        public Transform Parent;

        /// <summary>가운데 자리(월드).</summary>
        public Vector3 Center;
    }

    /// <summary>
    /// 조각할 새 지형 조각(격자 메시 + 콜라이더)을 만든다. 메시는 <see cref="MeshSculptTool.SculptFolder"/>에 저장되므로
    /// 첫 획부터 복사본 없이 바로 조각된다. 가장자리는 높이 0으로 내려앉아 주변 바닥과 이어 붙이기 쉽다.
    /// </summary>
    public static class SculptTerrainFactory
    {
        private const int MaxCells = 400;
        private const float UvMetres = 10f;

        public static GameObject Create(SculptTerrainSettings settings)
        {
            float size = Mathf.Max(1f, settings.Size);
            int cells = Mathf.Clamp(Mathf.RoundToInt(size / Mathf.Max(0.1f, settings.Spacing)), 2, MaxCells);
            Mesh mesh = BuildMesh(size, cells, settings);

            var go = new GameObject($"Sculpted Terrain ({settings.Shape})");
            Undo.RegisterCreatedObjectUndo(go, "Create Sculpt Terrain");
            if (settings.Parent) go.transform.SetParent(settings.Parent, true);
            go.transform.position = settings.Center;
            MeshRenderer reference = settings.Parent ? settings.Parent.GetComponentInChildren<MeshRenderer>() : null;
            if (reference)
            {
                go.layer = reference.gameObject.layer;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(reference.gameObject));
            }

            string folder = MeshSculptTool.SculptFolder + "/" + (string.IsNullOrEmpty(go.scene.name) ? "Scene" : go.scene.name);
            MeshSculptTool.EnsureFolder(folder);
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{folder}/Terrain_{settings.Shape}.asset"));

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = settings.Material
                ? settings.Material
                : reference ? reference.sharedMaterial : DefaultMaterial();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return go;
        }

        private static Material DefaultMaterial()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
        }

        private static Mesh BuildMesh(float size, int cells, SculptTerrainSettings settings)
        {
            int row = cells + 1;
            var vertices = new List<Vector3>(row * row);
            var uv0 = new List<Vector2>(row * row);
            var uv1 = new List<Vector2>(row * row);
            float half = size * 0.5f;
            for (int z = 0; z < row; z++)
            {
                for (int x = 0; x < row; x++)
                {
                    float u = (float)x / cells, v = (float)z / cells;
                    float px = u * size - half, pz = v * size - half;
                    vertices.Add(new Vector3(px, Height(u * 2f - 1f, v * 2f - 1f, px, pz, settings), pz));
                    uv0.Add(new Vector2(px / UvMetres, pz / UvMetres));
                    uv1.Add(new Vector2(u, v));
                }
            }

            var triangles = new List<int>(cells * cells * 6);
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int i = z * row + x;
                    // 대각선을 번갈아 그어 한쪽으로 기운 결이 생기지 않게 한다.
                    if (((x + z) & 1) == 0)
                    {
                        triangles.Add(i); triangles.Add(i + row); triangles.Add(i + row + 1);
                        triangles.Add(i); triangles.Add(i + row + 1); triangles.Add(i + 1);
                    }
                    else
                    {
                        triangles.Add(i); triangles.Add(i + row); triangles.Add(i + 1);
                        triangles.Add(i + 1); triangles.Add(i + row); triangles.Add(i + row + 1);
                    }
                }
            }

            var mesh = new Mesh { name = $"Terrain_{settings.Shape}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>(cx, cz): 가운데 0, 가장자리 ±1. (px, pz): 미터.</summary>
        private static float Height(float cx, float cz, float px, float pz, SculptTerrainSettings settings)
        {
            if (settings.Shape == SculptTerrainShape.Flat) return 0f;
            float r = Mathf.Sqrt(cx * cx + cz * cz);
            // 가장자리 띠에서 0으로 내려앉아 둘레가 평평하게 끝난다.
            float edge = 1f - Smooth(Mathf.InverseLerp(0.7f, 1f, Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz))));
            float offset = settings.Seed * 17.31f;
            float scale = Mathf.Max(8f, settings.Size * 0.12f);
            float detail = Noise(px / scale + offset, pz / scale - offset);
            float h = settings.Height;
            switch (settings.Shape)
            {
                case SculptTerrainShape.Hill:
                    return h * Mathf.Exp(-r * r * 5f) * (0.85f + 0.3f * detail) * edge;
                case SculptTerrainShape.Island:
                {
                    float body = Mathf.Clamp01(1.1f - r * 1.35f + (detail - 0.5f) * 0.45f);
                    return h * Smooth(body) * edge;
                }
                case SculptTerrainShape.Basin:
                    return -h * Smooth(Mathf.Clamp01(1f - r * 1.4f)) * (0.85f + 0.3f * detail) * edge;
                default:
                {
                    float ridge = 1f - Mathf.Abs(Noise(px / scale * 0.7f + offset, pz / scale * 0.7f) * 2f - 1f);
                    return h * ridge * ridge * edge;
                }
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static float Noise(float x, float z)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Mathf.PerlinNoise(x + 101.3f * octave, z + 57.9f * octave) * amplitude;
                total += amplitude;
                x *= 2.1f;
                z *= 2.1f;
                amplitude *= 0.5f;
            }

            return sum / total;
        }
    }
}
