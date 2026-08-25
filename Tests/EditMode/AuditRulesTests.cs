using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class AuditRulesTests
    {
        [Test]
        public void IsolatedChunks_FlaggedOnlyWithoutNeighbors()
        {
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            Fill(store, new Vector3Int(3, 0, 4), 1f);
            Fill(store, new Vector3Int(9, 0, -2), 1f); // far away island

            List<AuditIssue> issues = WorldAuditRules.CheckIsolatedChunks(store);
            Assert.That(issues.Count, Is.EqualTo(2));
            Assert.That(issues.All(issue => issue.Code == "WB_ISOLATED_CHUNK"), Is.True);

            // Bridging the islands clears both flags.
            for (int z = 3; z >= 1; z--) Fill(store, new Vector3Int(3, 0, z), 1f);
            for (int x = 4; x <= 8; x++) Fill(store, new Vector3Int(x, 0, 1), 1f);
            Fill(store, new Vector3Int(9, 0, 1), 1f);
            Fill(store, new Vector3Int(9, 0, 0), 1f);
            Fill(store, new Vector3Int(9, 0, -1), 1f);

            Assert.That(WorldAuditRules.CheckIsolatedChunks(store), Is.Empty);
        }

        [Test]
        public void DensitySanity_CatchesNaNAndOutOfRange()
        {
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            store.TryGetEntry(store.GetOrCreate(new Vector3Int(0, 0, 0)).coord, out _);

            VoxelChunkEntry nanEntry = store.GetOrCreate(new Vector3Int(0, 0, 0));
            nanEntry.density[0] = float.NaN;

            List<AuditIssue> nanIssues = WorldAuditRules.CheckDensitySanity(store);
            Assert.That(nanIssues.Any(issue => issue.Code == "WB_DENSITY_NAN"), Is.True);

            nanEntry.density[0] = 0f;
            VoxelChunkEntry rangeEntry = store.GetOrCreate(new Vector3Int(1, 0, 0));
            rangeEntry.density[7] = 1.75f;

            List<AuditIssue> rangeIssues = WorldAuditRules.CheckDensitySanity(store);
            Assert.That(rangeIssues.Any(issue => issue.Code == "WB_DENSITY_RANGE"), Is.True);
        }

        [Test]
        public void BorderContinuity_ReportsMismatchedAdjacentBorders()
        {
            const int resolution = 16;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();

            VoxelChunkEntry left = store.GetOrCreate(new Vector3Int(0, 0, 0));
            VoxelChunkEntry right = store.GetOrCreate(new Vector3Int(1, 0, 0));
            foreach (float[] density in new[] { left.density, right.density })
                for (int i = 0; i < density.Length; i++) density[i] = 1f;

            Assert.That(WorldAuditRules.CheckBorderContinuity(store), Is.Empty,
                "identical fills must weld cleanly");

            right.density[resolution * resolution * resolution - resolution] = 0f; // +X face corner

            List<AuditIssue> issues = WorldAuditRules.CheckBorderContinuity(store);
            Assert.That(issues.Any(issue => issue.Code == "WB_BORDER_MISMATCH"), Is.True);
        }

        private static void Fill(VoxelStoreAsset store, Vector3Int coord, float value)
        {
            VoxelChunkEntry entry = store.GetOrCreate(coord);
            for (int i = 0; i < entry.density.Length; i++) entry.density[i] = value;
        }
    }
}
