using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;
using WorldBuilder.Editor.BlenderBridge;

namespace WorldBuilder.Tests
{
    public sealed class ChunkBakePrefabAssemblerTests
    {
        [Test]
        public void ResetGeneratedRoot_PreservesUserOwnedChildren()
        {
            GameObject root = new GameObject("Chunk");
            try
            {
                new GameObject("UserChild").transform.SetParent(root.transform, false);
                Transform oldGenerated = new GameObject(ChunkBakePrefabAssembler.GeneratedRootName).transform;
                oldGenerated.SetParent(root.transform, false);
                new GameObject("Stale").transform.SetParent(oldGenerated, false);

                Transform generated = ChunkBakePrefabAssembler.ResetGeneratedRoot(root.transform);

                Assert.That(root.transform.Find("UserChild"), Is.Not.Null);
                Assert.That(generated.childCount, Is.EqualTo(0));
                Assert.That(root.transform.Cast<Transform>().Count(x => x.name == ChunkBakePrefabAssembler.GeneratedRootName), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Assemble_CreatesLodGroupAndPrimitiveCollider()
        {
            GameObject generated = new GameObject("Generated");
            GameObject geometry = new GameObject("Geometry");
            geometry.transform.SetParent(generated.transform, false);
            GameObject collision = new GameObject("Collision");
            collision.transform.SetParent(generated.transform, false);
            Mesh mesh = CreateMesh();
            try
            {
                CreateMeshObject("Rock_LOD0", geometry.transform, mesh);
                CreateMeshObject("Rock_LOD1", geometry.transform, mesh);
                GameObject colliderObject = CreateMeshObject("Rock_COL", collision.transform, mesh);
                BakeManifest manifest = new BakeManifest
                {
                    objects = new[]
                    {
                        new BakeManifestObject
                        {
                            stableId = "rock-01",
                            lods = new[]
                            {
                                new BakeManifestLod { level = 0, fileObject = "Rock_LOD0", triangles = 1 },
                                new BakeManifestLod { level = 1, fileObject = "Rock_LOD1", triangles = 1 }
                            },
                            collider = new BakeManifestCollider { type = "BOX", fileObject = "Rock_COL" }
                        }
                    }
                };
                WorldBakeReport report = new WorldBakeReport();

                ChunkBakePrefabAssembler.Assemble(manifest, geometry, collision, new[] { 0.7f, 0.2f }, report);

                Assert.That(report.HasErrors, Is.False);
                LODGroup group = generated.GetComponentInChildren<LODGroup>();
                Assert.That(group, Is.Not.Null);
                Assert.That(group.GetLODs().Length, Is.EqualTo(2));
                Assert.That(group.GetLODs()[0].screenRelativeTransitionHeight, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(colliderObject.GetComponent<BoxCollider>(), Is.Not.Null);
                Assert.That(colliderObject.GetComponent<MeshRenderer>().enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(generated);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SourceHash_ChangesWithProfileAndTransitions()
        {
            BakeManifest manifest = new BakeManifest { profileHash = new string('a', 64) };
            string first = ChunkBakePrefabAssembler.ComputeSourceHash(new string('b', 64), manifest, new[] { 0.6f });
            string second = ChunkBakePrefabAssembler.ComputeSourceHash(new string('b', 64), manifest, new[] { 0.5f });
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.Length, Is.EqualTo(64));
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh)
        {
            GameObject value = new GameObject(name);
            value.transform.SetParent(parent, false);
            value.AddComponent<MeshFilter>().sharedMesh = mesh;
            value.AddComponent<MeshRenderer>();
            return value;
        }

        private static Mesh CreateMesh()
        {
            Mesh mesh = new Mesh { name = "TestTriangle" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
