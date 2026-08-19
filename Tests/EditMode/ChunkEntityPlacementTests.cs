using NUnit.Framework;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Tests
{
    public sealed class ChunkEntityPlacementTests
    {
        [Test]
        public void EntityPlacement_WithCompleteBlock_Validates()
        {
            WorldBakeReport report = ChunkManifestCodec.ValidatePlacements(
                Document(EntityRecord()), Manifest());
            Assert.That(report.HasErrors, Is.False);
        }

        [Test]
        public void EntityPlacement_WithoutAssetId_IsRejected()
        {
            ChunkPlacementRecord record = EntityRecord();
            record.assetId = string.Empty;
            Assert.That(ChunkManifestCodec.ValidatePlacements(Document(record), Manifest()).HasErrors, Is.True);
        }

        [Test]
        public void EntityPlacement_WithUnknownKind_IsRejected()
        {
            ChunkPlacementRecord record = EntityRecord();
            record.entity.kind = "Vehicle";
            Assert.That(ChunkManifestCodec.ValidatePlacements(Document(record), Manifest()).HasErrors, Is.True);
        }

        [Test]
        public void EntityPlacement_WithNegativePrefabId_IsRejected()
        {
            ChunkPlacementRecord record = EntityRecord();
            record.entity.prefabId = -1;
            Assert.That(ChunkManifestCodec.ValidatePlacements(Document(record), Manifest()).HasErrors, Is.True);
        }

        [Test]
        public void NegativeLayer_IsRejected()
        {
            ChunkPlacementRecord record = EntityRecord();
            record.layer = -1;
            Assert.That(ChunkManifestCodec.ValidatePlacements(Document(record), Manifest()).HasErrors, Is.True);
        }

        [Test]
        public void MarkerPlacement_NeedsNoAssetOrEntity()
        {
            ChunkPlacementRecord record = new ChunkPlacementRecord
            {
                stableId = "marker",
                role = ChunkPlacementRecord.MarkerRole,
                matrix = Identity()
            };
            Assert.That(ChunkManifestCodec.ValidatePlacements(Document(record), Manifest()).HasErrors, Is.False);
        }

        [Test]
        public void RoleHelpers_MatchTheBlenderContract()
        {
            ChunkPlacementRecord entity = EntityRecord();
            Assert.That(entity.IsEntity, Is.True);
            Assert.That(entity.IsInstance, Is.False);
            Assert.That(entity.UsesAsset, Is.True);
            Assert.That(entity.entity.HasFlag("RegionStreamed"), Is.True);
            Assert.That(entity.entity.HasFlag("Persistent"), Is.False);
        }

        private static ChunkPlacementRecord EntityRecord()
        {
            return new ChunkPlacementRecord
            {
                stableId = "entity-a",
                name = "ENT_kelp_0001",
                role = ChunkPlacementRecord.EntityRole,
                assetId = "kelp",
                layer = 3,
                matrix = Identity(),
                entity = new ChunkEntityRecord
                {
                    prefabId = 12,
                    kind = "Resource",
                    flags = new[] { "RegionStreamed" },
                    lifetimeSeconds = 0f
                }
            };
        }

        private static ChunkPlacementDocument Document(params ChunkPlacementRecord[] records)
        {
            return new ChunkPlacementDocument
            {
                worldId = "World_01",
                chunk = new ChunkManifestCoord { x = 1, z = -2 },
                objects = records
            };
        }

        private static ChunkManifest Manifest()
        {
            return new ChunkManifest
            {
                worldId = "World_01",
                chunk = new ChunkManifestCoord { x = 1, z = -2 }
            };
        }

        private static float[] Identity()
        {
            return new[]
            {
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f
            };
        }
    }
}
