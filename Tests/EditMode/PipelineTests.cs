using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Tests
{
    public sealed class PipelineTests
    {
        [Test]
        public void EditPacket_RoundTripsCommandsAndChecksumGuardsCorruption()
        {
            var commands = new List<TerrainEditCommand>
            {
                new TerrainEditCommand { center = new Vector3(10f, 4f, -6f), radius = 2f, delta = -1f, authorId = 7 },
                new TerrainEditCommand { center = new Vector3(0f, 0f, 0f), radius = 5f, delta = 0.5f, authorId = 3 }
            };

            string json = TerrainEditCodec.ToJson(commands);
            Assert.That(TerrainEditCodec.TryParse(json, out List<TerrainEditCommand> parsed, out string error),
                Is.True, error ?? "");

            Assert.That(parsed.Count, Is.EqualTo(2));
            Assert.That(parsed[0].center.x, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(parsed[0].authorId, Is.EqualTo(7));
            Assert.That(parsed[1].delta, Is.EqualTo(0.5f).Within(1e-6f));

            // Corrupt the payload → checksum must reject it.
            string corrupted = json.Replace("\"radius\":2", "\"radius\":9");
            if (corrupted != json)
                Assert.That(TerrainEditCodec.TryParse(corrupted, out _, out _), Is.False,
                    "tampered packets must fail the checksum");
        }

        [Test]
        public void EditPacket_ReplayCarvesTheStore()
        {
            const int resolution = 16;
            var store = ScriptableObject.CreateInstance<VoxelStoreAsset>();
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                VoxelChunkEntry entry = store.GetOrCreate(new Vector3Int(cx, 0, cz));
                for (int i = 0; i < entry.density.Length; i++) entry.density[i] = 1f;
            }

            string json = TerrainEditCodec.ToJson(new[]
            {
                new TerrainEditCommand { center = new Vector3(8f, 8f, 8f), radius = 2.5f, delta = -2f }
            });
            Assert.That(TerrainEditCodec.TryParse(json, out List<TerrainEditCommand> commands, out _),
                Is.True);

            TerrainDeformer.ResetJournal();
            int changed = TerrainEditCodec.Replay(store, 16f, commands);

            Assert.That(changed, Is.GreaterThan(0));
            var sampler = new VoxelWorldSampler(store, 16f);
            Assert.That(sampler.Sample(8f, 8f, 8f), Is.LessThan(SurfaceNetsMesher.IsoLevel));
            TerrainDeformer.ResetJournal();
        }

        [TearDown]
        public void TearDown() => TerrainDeformer.ResetJournal();
    }
}
