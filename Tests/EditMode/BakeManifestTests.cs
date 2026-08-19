using NUnit.Framework;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Tests.EditMode
{
    public sealed class BakeManifestTests
    {
        [Test]
        public void BlenderBakeManifest_ParsesAndValidates()
        {
            WorldGridSettings settings = UnityEngine.ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(16f, 4, 4f, UnityEngine.Vector3.zero);
            settings.SetWorldId("SmokeWorld");
            const string json = "{\"schemaVersion\":1,\"worldId\":\"SmokeWorld\",\"chunk\":{\"x\":-1,\"z\":2}," +
                                "\"profileHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
                                "\"objects\":[{\"stableId\":\"rock\",\"name\":\"Rock\",\"role\":\"GEOMETRY\"," +
                                "\"lods\":[{\"level\":0,\"fileObject\":\"Rock_LOD0\",\"triangles\":12}]," +
                                "\"collider\":{\"type\":\"BOX\",\"fileObject\":\"Rock_COL\"}}]," +
                                "\"vertexAttributes\":[{\"name\":\"WB_ShaderData\",\"domain\":\"POINT\"," +
                                "\"channels\":{\"R\":\"UP_FACING\",\"G\":\"CAVITY_APPROX\",\"B\":\"BIOME_WEIGHT\",\"A\":\"WATER_DEPTH\"}}]}";
            BakeManifest manifest = BakeManifestCodec.Parse(json);
            Assert.That(BakeManifestCodec.Validate(manifest, settings).HasErrors, Is.False);
            Assert.That(manifest.chunk.x, Is.EqualTo(-1));
            Assert.That(manifest.vertexAttributes[0].channels.B, Is.EqualTo("BIOME_WEIGHT"));
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void InvalidDuplicateLodAndHash_AreRejected()
        {
            WorldGridSettings settings = UnityEngine.ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(16f, 4, 4f, UnityEngine.Vector3.zero);
            settings.SetWorldId("World");
            BakeManifest manifest = new BakeManifest
            {
                worldId = "World",
                profileHash = "bad",
                objects = new[]
                {
                    new BakeManifestObject
                    {
                        stableId = "a",
                        lods = new[]
                        {
                            new BakeManifestLod { level = 0, fileObject = "a", triangles = 1 },
                            new BakeManifestLod { level = 0, fileObject = "b", triangles = 1 }
                        }
                    }
                }
            };
            Assert.That(BakeManifestCodec.Validate(manifest, settings).HasErrors, Is.True);
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}
