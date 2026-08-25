using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// File-based persistence for runtime edits: writes placement snapshots produced by
    /// <see cref="Editing.RuntimePlacementService.ToJson"/> under persistentDataPath.
    /// </summary>
    public static class WorldSaveService
    {
        public sealed class SaveInfo
        {
            public string Slot { get; }
            public DateTime TimestampUtc { get; }
            public int PlacementCount { get; }

            public SaveInfo(string slot, DateTime timestampUtc, int placementCount)
            {
                Slot = slot;
                TimestampUtc = timestampUtc;
                PlacementCount = placementCount;
            }
        }

        [Serializable]
        private sealed class SaveFile
        {
            public int version = 1;
            public string worldId = string.Empty;
            public string timestampUtc = string.Empty;
            public int placementCount;
            public string placementsJson = string.Empty;
        }

        /// <summary>Override for tests; defaults to &lt;persistentDataPath&gt;/WorldBuilder/Saves.</summary>
        public static Func<string> DirectoryProvider { get; set; }

        public static string DefaultWorldId = "World_01";

        private static string Directory =>
            DirectoryProvider != null ? DirectoryProvider() : Path.Combine(Application.persistentDataPath, "WorldBuilder", "Saves");

        public static void Save(string slot, string placementsJson, string worldId = null)
        {
            if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Slot name is required.", nameof(slot));

            SaveFile file = new SaveFile
            {
                worldId = string.IsNullOrEmpty(worldId) ? DefaultWorldId : worldId,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                placementCount = CountPlacements(placementsJson),
                placementsJson = placementsJson ?? string.Empty
            };

            string path = Path.Combine(Directory, Sanitize(slot) + ".json");
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(file));
        }

        /// <summary>
        /// Loads a slot and restores it through <see cref="Editing.RuntimePlacementService.RestoreFromJson"/>.
        /// Returns false when the slot does not exist.
        /// </summary>
        public static bool Load(string slot, Func<string, GameObject> prefabResolver)
        {
            if (!TryRead(slot, out SaveFile file)) return false;

            Editing.RuntimePlacementService.Reset();
            Editing.RuntimePlacementService.RestoreFromJson(file.placementsJson, prefabResolver);
            return true;
        }

        public static bool Exists(string slot)
        {
            return File.Exists(PathFor(slot));
        }

        public static bool Delete(string slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public static List<SaveInfo> List()
        {
            List<SaveInfo> result = new List<SaveInfo>();
            string directory = Directory;
            if (!System.IO.Directory.Exists(directory)) return result;

            foreach (string path in System.IO.Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                    if (file == null) continue;
                    result.Add(new SaveInfo(
                        Path.GetFileNameWithoutExtension(path),
                        DateTime.TryParse(file.timestampUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
                            ? parsed
                            : File.GetLastWriteTimeUtc(path),
                        file.placementCount));
                }
                catch (Exception)
                {
                    // Corrupt files are skipped rather than breaking the listing.
                }
            }

            result.Sort((left, right) => right.TimestampUtc.CompareTo(left.TimestampUtc));
            return result;
        }

        private static bool TryRead(string slot, out SaveFile file)
        {
            file = null;
            string path = PathFor(slot);
            if (!File.Exists(path)) return false;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return false;
            }
            return file != null;
        }

        private static string PathFor(string slot)
        {
            return Path.Combine(Directory, Sanitize(slot) + ".json");
        }

        private static string Sanitize(string value)
        {
            char[] characters = value.Trim().ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                char c = characters[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') characters[i] = '_';
            }
            return new string(characters);
        }

        private static int CountPlacements(string json)
        {
            // Our own ToJson output stores placements as objects that all carry "prefabId".
            if (string.IsNullOrEmpty(json)) return 0;
            int count = 0;
            int index = 0;
            while ((index = json.IndexOf("\"prefabId\"", index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += "\"prefabId\"".Length;
            }
            return count;
        }

        // ---- Terrain deltas ----

        [Serializable]
        private sealed class TerrainDeltaFile
        {
            public int version = 1;
            public List<string> coords = new List<string>();
            public List<string> sizes = new List<string>();
            public List<string> densitiesBase64 = new List<string>();
        }

        /// <summary>
        /// Persists full density snapshots for the given chunks (typically the ones the
        /// player deformed). Restoring overwrites those chunks wholesale.
        /// </summary>
        public static void SaveTerrain(string slot, WorldBuilder.Runtime.Data.VoxelStoreAsset store,
            IEnumerable<Vector3Int> editedChunks)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var file = new TerrainDeltaFile();
            foreach (Vector3Int coord in editedChunks)
            {
                if (!store.TryGetVoxelData(coord, out WorldBuilder.Runtime.Data.VoxelData voxels)) continue;
                float[] flat = new float[voxels.sizeX * voxels.sizeY * voxels.sizeZ];
                for (int x = 0; x < voxels.sizeX; x++)
                for (int y = 0; y < voxels.sizeY; y++)
                for (int z = 0; z < voxels.sizeZ; z++)
                    flat[x + voxels.sizeX * (y + voxels.sizeY * z)] = voxels.GetDensity(x, y, z);

                byte[] bytes = new byte[flat.Length * sizeof(float)];
                Buffer.BlockCopy(flat, 0, bytes, 0, bytes.Length);
                file.coords.Add($"{coord.x},{coord.y},{coord.z}");
                file.sizes.Add($"{voxels.sizeX},{voxels.sizeY},{voxels.sizeZ}");
                file.densitiesBase64.Add(Convert.ToBase64String(bytes));
            }

            string path = Path.Combine(Directory, Sanitize(slot) + "_terrain.json");
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(file));
        }

        /// <summary>
        /// Restores terrain chunk deltas saved with <see cref="SaveTerrain"/>. Returns the
        /// number of chunks restored, or -1 when no terrain file exists for the slot.
        /// </summary>
        public static int LoadTerrain(string slot, WorldBuilder.Runtime.Data.VoxelStoreAsset store,
            Action<Vector3Int> chunkRestored = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            string path = Path.Combine(Directory, Sanitize(slot) + "_terrain.json");
            if (!File.Exists(path)) return -1;

            TerrainDeltaFile file = JsonUtility.FromJson<TerrainDeltaFile>(File.ReadAllText(path));
            if (file?.coords == null) return -1;

            int restored = 0;
            for (int i = 0; i < file.coords.Count && i < file.densitiesBase64.Count; i++)
            {
                string[] parts = file.coords[i].Split(',');
                if (parts.Length != 3 ||
                    !int.TryParse(parts[0], out int cx) || !int.TryParse(parts[1], out int cy) ||
                    !int.TryParse(parts[2], out int cz)) continue;

                byte[] bytes = Convert.FromBase64String(file.densitiesBase64[i]);
                int count = bytes.Length / sizeof(float);

                int sideX, sideY, sideZ;
                if (i < file.sizes.Count)
                {
                    string[] sizeParts = file.sizes[i].Split(',');
                    if (sizeParts.Length != 3 ||
                        !int.TryParse(sizeParts[0], out sideX) || !int.TryParse(sizeParts[1], out sideY) ||
                        !int.TryParse(sizeParts[2], out sideZ) || sideX <= 0 || sideY <= 0 || sideZ <= 0)
                        continue;
                }
                else
                {
                    sideX = sideY = sideZ = Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(count, 1f / 3f)));
                }

                if (sideX * sideY * sideZ != count) continue;

                var voxels = new WorldBuilder.Runtime.Data.VoxelData(sideX, sideY, sideZ);
                float[] flat = new float[count];
                Buffer.BlockCopy(bytes, 0, flat, 0, bytes.Length);
                for (int x = 0; x < sideX; x++)
                for (int y = 0; y < sideY; y++)
                for (int z = 0; z < sideZ; z++)
                    voxels.SetDensity(x, y, z, flat[x + sideX * (y + sideY * z)]);

                store.SetVoxelData(new Vector3Int(cx, cy, cz), voxels);
                chunkRestored?.Invoke(new Vector3Int(cx, cy, cz));
                restored++;
            }
            return restored;
        }
    }
}
