using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Runtime.Terrain
{
    [Serializable]
    public struct TerrainEditCommand
    {
        public Vector3 center;
        public float radius;
        public float delta;
        /// <summary>Author id / peer index for attribution in multiplayer sessions.</summary>
        public int authorId;
    }

    /// <summary>
    /// Wire-ready serialization for runtime terrain edits: a list of sphere commands that
    /// replays deterministically on any peer through <see cref="TerrainDeformer.Modify"/>.
    /// Includes an FNV-1a checksum so receivers can drop corrupted packets cheaply.
    /// </summary>
    public static class TerrainEditCodec
    {
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        [Serializable]
        private sealed class Packet
        {
            public int version = 1;
            public string checksum = "";
            public List<CommandRecord> commands = new List<CommandRecord>();
        }

        [Serializable]
        private sealed class CommandRecord
        {
            public float cx, cy, cz, radius, delta;
            public int author;
        }

        public static string ToJson(IEnumerable<TerrainEditCommand> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            var packet = new Packet();
            foreach (TerrainEditCommand command in commands)
            {
                packet.commands.Add(new CommandRecord
                {
                    cx = command.center.x, cy = command.center.y, cz = command.center.z,
                    radius = command.radius, delta = command.delta, author = command.authorId
                });
            }
            packet.checksum = Checksum(packet.commands);
            return JsonUtility.ToJson(packet);
        }

        public static bool TryParse(string json, out List<TerrainEditCommand> commands,
            out string error)
        {
            commands = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "Empty packet."; return false; }

            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(json); }
            catch (Exception exception) { error = exception.Message; return false; }

            if (packet?.commands == null) { error = "No commands."; return false; }
            if (Checksum(packet.commands) != packet.checksum)
            {
                error = "Checksum mismatch — packet corrupted.";
                return false;
            }

            commands = new List<TerrainEditCommand>(packet.commands.Count);
            foreach (CommandRecord record in packet.commands)
            {
                commands.Add(new TerrainEditCommand
                {
                    center = new Vector3(record.cx, record.cy, record.cz),
                    radius = record.radius,
                    delta = record.delta,
                    authorId = record.author
                });
            }
            return true;
        }

        /// <summary>Applies every command through <see cref="TerrainDeformer.Modify"/>. Returns voxels changed.</summary>
        public static int Replay(VoxelStoreAsset store, float chunkSize,
            IEnumerable<TerrainEditCommand> commands)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            int total = 0;
            foreach (TerrainEditCommand command in commands)
                total += TerrainDeformer.Modify(store, chunkSize,
                    command.center, command.radius, command.delta);
            return total;
        }

        private static string Checksum(List<CommandRecord> records)
        {
            uint hash = FnvOffset;
            foreach (CommandRecord record in records)
            {
                hash ^= (uint)record.cx.GetHashCode(); hash *= FnvPrime;
                hash ^= (uint)record.cy.GetHashCode(); hash *= FnvPrime;
                hash ^= (uint)record.cz.GetHashCode(); hash *= FnvPrime;
                hash ^= (uint)record.radius.GetHashCode(); hash *= FnvPrime;
                hash ^= (uint)record.delta.GetHashCode(); hash *= FnvPrime;
                hash ^= (uint)record.author.GetHashCode(); hash *= FnvPrime;
            }
            return hash.ToString("x8");
        }
    }
}
