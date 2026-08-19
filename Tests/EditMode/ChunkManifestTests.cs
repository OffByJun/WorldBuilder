using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Tests
{
    public sealed class ChunkManifestTests
    {
        [Test]
        public void ValidManifest_RoundTripsAndValidates()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.Configure(128f, 4, 32f, Vector3.zero);
            settings.SetWorldId("World_01");
            ChunkManifest manifest = new ChunkManifest
            {
                worldId = "World_01",
                chunk = new ChunkManifestCoord { x = -1, z = 2 },
                region = new ChunkManifestCoord { x = -1, z = 0 },
                chunkSize = 128f,
                localOrigin = Vector3.zero,
                source = new ChunkSourceInfo { authoringHash = Hash },
                content = ValidContent()
            };
            manifest.contentHash = ChunkManifestCodec.ComputeContentHash(manifest);
            ChunkManifest parsed = ChunkManifestCodec.Parse(ChunkManifestCodec.Serialize(manifest));
            Assert.That(parsed.chunk.x, Is.EqualTo(-1));
            Assert.That(ChunkManifestCodec.Validate(parsed, settings).HasErrors, Is.False);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void NonLocalOrigin_IsRejected()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            settings.SetWorldId("World");
            ChunkManifest manifest = new ChunkManifest
            {
                worldId = "World",
                source = new ChunkSourceInfo { authoringHash = Hash },
                localOrigin = Vector3.right,
                content = ValidContent()
            };
            manifest.contentHash = ChunkManifestCodec.ComputeContentHash(manifest);
            Assert.That(ChunkManifestCodec.Validate(manifest, settings).HasErrors, Is.True);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void PlacementDocument_RequiresStableIdsAndInstanceAssetId()
        {
            ChunkManifest manifest = new ChunkManifest
            {
                worldId = "World_01",
                chunk = new ChunkManifestCoord { x = 0, z = 0 }
            };
            ChunkPlacementDocument document = new ChunkPlacementDocument
            {
                worldId = "World_01",
                chunk = new ChunkManifestCoord { x = 0, z = 0 },
                objects = new[]
                {
                    new ChunkPlacementRecord
                    {
                        stableId = "tree-01",
                        role = "INSTANCE",
                        matrix = new float[16]
                    }
                }
            };
            Assert.That(ChunkManifestCodec.ValidatePlacements(document, manifest).HasErrors, Is.True);
        }

        [Test]
        public void ContentHash_MatchesBlenderCanonicalContract()
        {
            ChunkManifest manifest = new ChunkManifest
            {
                source = new ChunkSourceInfo { authoringHash = Hash },
                content = ValidContent()
            };
            Assert.That(ChunkManifestCodec.ComputeContentHash(manifest),
                Is.EqualTo("896f8d41ea23dafc9811594b72cbc37a9960363a4ddaed8b67bc233eb8e2fa63"));
        }

        private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static ChunkManifestContent ValidContent()
        {
            return new ChunkManifestContent
            {
                geometry = new ChunkFileReference { path = "geometry.fbx", sha256 = Hash, bytes = 1 },
                collision = new ChunkFileReference(),
                placements = new ChunkFileReference(),
                localBounds = new ChunkBoundsRecord { min = Vector3.zero, max = Vector3.one }
            };
        }
    }
}
