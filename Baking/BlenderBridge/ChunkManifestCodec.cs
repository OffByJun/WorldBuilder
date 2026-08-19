using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Baking.BlenderBridge
{
    public static class ChunkManifestCodec
    {
        private const float BoundsTolerance = 0.01f;

        public static ChunkManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Manifest JSON is empty.", nameof(json));
            ChunkManifest manifest = JsonUtility.FromJson<ChunkManifest>(json);
            if (manifest == null) throw new FormatException("Manifest JSON could not be parsed.");
            return manifest;
        }

        public static ChunkPlacementDocument ParsePlacements(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Placement JSON is empty.", nameof(json));
            ChunkPlacementDocument document = JsonUtility.FromJson<ChunkPlacementDocument>(json);
            if (document == null) throw new FormatException("Placement JSON could not be parsed.");
            return document;
        }

        public static string Serialize(ChunkManifest manifest, bool prettyPrint = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        public static WorldBakeReport Validate(ChunkManifest manifest, WorldGridSettings settings)
        {
            WorldBakeReport report = new WorldBakeReport();
            if (manifest == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_NULL", string.Empty, "Manifest is null.");
                return report;
            }
            if (settings == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_GRID_SETTINGS_NULL", string.Empty, "WorldGridSettings is required.");
                return report;
            }

            string path = manifest.worldId ?? string.Empty;
            if (manifest.version != ChunkManifest.CurrentVersion)
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_VERSION", path,
                    $"Expected version {ChunkManifest.CurrentVersion}, got {manifest.version}.");
            if (string.IsNullOrWhiteSpace(manifest.worldId))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_WORLD_ID", string.Empty, "worldId is required.");
            else if (!string.Equals(manifest.worldId, settings.WorldId, StringComparison.Ordinal))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_WORLD_MISMATCH", path,
                    $"Expected worldId '{settings.WorldId}', got '{manifest.worldId}'.");
            if (manifest.chunk == null || manifest.region == null)
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_COORDINATE", path, "chunk and region coordinates are required.");
            else
            {
                RegionCoord expectedRegion = settings.CreateGrid().ChunkToRegion(
                    new ChunkCoord(manifest.chunk.x, manifest.chunk.z));
                if (manifest.region.x != expectedRegion.X || manifest.region.z != expectedRegion.Z)
                    report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_REGION", path,
                        $"Chunk belongs to region {expectedRegion}, not ({manifest.region.x}, {manifest.region.z}).");
            }
            if (!Mathf.Approximately(manifest.chunkSize, settings.AuthoringChunkSize))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_CHUNK_SIZE", path,
                    $"Expected chunkSize {settings.AuthoringChunkSize}, got {manifest.chunkSize}.");
            if (manifest.localOrigin.sqrMagnitude > 0.000001f)
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_LOCAL_ORIGIN", path,
                    "Blender exports must be chunk-local and localOrigin must be (0, 0, 0).");
            ValidateCoordinateSystem(manifest.coordinateSystem, path, report);
            if (!IsSha256(manifest.contentHash))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_CONTENT_HASH", path,
                    "contentHash must be a lowercase or uppercase 64-character SHA-256 value.");
            if (manifest.source == null || !IsSha256(manifest.source.authoringHash))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_AUTHORING_HASH", path,
                    "source.authoringHash must be a 64-character SHA-256 value.");
            else if (IsSha256(manifest.contentHash) &&
                     !string.Equals(manifest.contentHash, ComputeContentHash(manifest), StringComparison.OrdinalIgnoreCase))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_CONTENT_HASH_MISMATCH", path,
                    "contentHash does not match source.authoringHash and the ordered content file references.");
            ValidateContent(manifest.content, manifest.chunkSize, path, report);
            report.Sort();
            return report;
        }

        public static WorldBakeReport ValidatePlacements(ChunkPlacementDocument document, ChunkManifest manifest)
        {
            WorldBakeReport report = new WorldBakeReport();
            if (document == null || manifest == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_PLACEMENTS_NULL", string.Empty, "Placement document and manifest are required.");
                return report;
            }
            if (document.version != ChunkPlacementDocument.CurrentVersion)
                report.Add(BakeIssueSeverity.Error, "WB_PLACEMENTS_VERSION", manifest.worldId,
                    $"Expected placement version {ChunkPlacementDocument.CurrentVersion}, got {document.version}.");
            if (!string.Equals(document.worldId, manifest.worldId, StringComparison.Ordinal))
                report.Add(BakeIssueSeverity.Error, "WB_PLACEMENTS_WORLD", manifest.worldId, "Placement worldId does not match the chunk manifest.");
            if (document.chunk == null || manifest.chunk == null || document.chunk.x != manifest.chunk.x || document.chunk.z != manifest.chunk.z)
                report.Add(BakeIssueSeverity.Error, "WB_PLACEMENTS_CHUNK", manifest.worldId, "Placement chunk coordinate does not match the manifest.");

            ChunkPlacementRecord[] objects = document.objects ?? Array.Empty<ChunkPlacementRecord>();
            Array.Sort(objects, (left, right) => string.CompareOrdinal(left?.stableId, right?.stableId));
            string previous = null;
            for (int i = 0; i < objects.Length; i++)
            {
                ChunkPlacementRecord item = objects[i];
                if (item == null || string.IsNullOrWhiteSpace(item.stableId))
                {
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ID", manifest.worldId, "Every placement requires a stableId.");
                    continue;
                }
                if (string.Equals(previous, item.stableId, StringComparison.Ordinal))
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_DUPLICATE_ID", item.stableId, "Placement stable IDs must be unique.");
                previous = item.stableId;
                if (item.matrix == null || item.matrix.Length != 16)
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_MATRIX", item.stableId, "Placement matrix must contain 16 row-major floats.");
                if (item.UsesAsset && string.IsNullOrWhiteSpace(item.assetId))
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ASSET_ID", item.stableId, $"{item.role} placements require assetId.");
                if (item.layer < 0)
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_LAYER", item.stableId, "Placement layer must not be negative.");
                if (!item.IsEntity) continue;
                if (item.entity == null)
                {
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ENTITY", item.stableId, "ENTITY placements require an entity block.");
                    continue;
                }
                if (item.entity.prefabId < 0)
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ENTITY_PREFAB", item.stableId, "Entity prefabId must not be negative.");
                if (!IsKnownEntityKind(item.entity.kind))
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ENTITY_KIND", item.stableId,
                        $"'{item.entity.kind}' is not a WorldEntityKind value.");
                if (item.entity.lifetimeSeconds < 0f)
                    report.Add(BakeIssueSeverity.Error, "WB_PLACEMENT_ENTITY_LIFETIME", item.stableId, "Entity lifetime must not be negative.");
            }
            report.Sort();
            return report;
        }

        private static bool IsKnownEntityKind(string value)
        {
            string[] names = ChunkEntityRecord.KindNames;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        public static string ResolveContentPath(string manifestPath, ChunkFileReference file)
        {
            if (file == null || !file.IsPresent) return string.Empty;
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, file.path));
        }

        public static string ComputeContentHash(ChunkManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            ChunkManifestContent content = manifest.content ?? new ChunkManifestContent();
            StringBuilder payload = new StringBuilder(512);
            payload.Append("{\"authoringHash\":\"").Append(JsonEscape(manifest.source?.authoringHash ?? string.Empty))
                .Append("\",\"files\":[");
            AppendFile(payload, content.geometry);
            payload.Append(',');
            AppendFile(payload, content.collision);
            payload.Append(',');
            AppendFile(payload, content.placements);
            payload.Append("]}");
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload.ToString()));
                StringBuilder text = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }

        private static void ValidateCoordinateSystem(ChunkCoordinateSystem value, string path, WorldBakeReport report)
        {
            if (value == null || !string.Equals(value.unit, "meter", StringComparison.Ordinal) ||
                !Mathf.Approximately(value.unitsPerMeter, 1f) ||
                !string.Equals(value.blenderUpAxis, "Z", StringComparison.Ordinal) ||
                !string.Equals(value.blenderForwardAxis, "Y", StringComparison.Ordinal) ||
                !string.Equals(value.unityUpAxis, "Y", StringComparison.Ordinal) ||
                !string.Equals(value.unityForwardAxis, "Z", StringComparison.Ordinal) ||
                !string.Equals(value.vectorMapping, "XZY", StringComparison.Ordinal))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_AXIS", path,
                    "Expected Blender Z-up/Y-forward meters mapped to Unity Y-up/Z-forward using XZY.");
        }

        private static void ValidateContent(ChunkManifestContent content, float chunkSize, string path, WorldBakeReport report)
        {
            if (content == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_CONTENT", path, "content is required.");
                return;
            }
            ValidateFile(content.geometry, ".fbx", "geometry", path, report);
            ValidateFile(content.collision, ".fbx", "collision", path, report);
            ValidateFile(content.placements, ".json", "placements", path, report);
            if (!(content.geometry?.IsPresent ?? false) && !(content.collision?.IsPresent ?? false) &&
                !(content.placements?.IsPresent ?? false))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_EMPTY", path, "A chunk must contain geometry, collision, or placements.");
            if (content.localBounds != null)
            {
                Vector3 min = content.localBounds.min;
                Vector3 max = content.localBounds.max;
                if (min.x > max.x || min.y > max.y || min.z > max.z)
                    report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_BOUNDS", path, "localBounds min must not exceed max.");
                if (min.x < -BoundsTolerance || min.z < -BoundsTolerance ||
                    max.x > chunkSize + BoundsTolerance || max.z > chunkSize + BoundsTolerance)
                    report.Add(BakeIssueSeverity.Warning, "WB_MANIFEST_CROSS_CHUNK", path,
                        "Local bounds cross the owning chunk. Split terrain/collision or explicitly classify it as region/global content.");
            }
        }

        private static void ValidateFile(ChunkFileReference file, string extension, string label,
            string path, WorldBakeReport report)
        {
            if (file == null || !file.IsPresent) return;
            if (Path.IsPathRooted(file.path) || file.path.Contains("..") || file.path.Contains(":"))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_UNSAFE_PATH", path, $"{label} path must be relative and remain inside the chunk folder.");
            if (!file.path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_FILE_EXTENSION", path, $"{label} must use {extension}.");
            if (!IsSha256(file.sha256))
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_FILE_HASH", path, $"{label} sha256 is invalid.");
            if (file.bytes < 0)
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_FILE_SIZE", path, $"{label} byte count cannot be negative.");
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        private static void AppendFile(StringBuilder payload, ChunkFileReference file)
        {
            file = file ?? new ChunkFileReference();
            payload.Append("{\"bytes\":").Append(file.bytes.ToString(CultureInfo.InvariantCulture))
                .Append(",\"path\":\"").Append(JsonEscape(file.path ?? string.Empty))
                .Append("\",\"sha256\":\"").Append(JsonEscape(file.sha256 ?? string.Empty)).Append("\"}");
        }

        private static string JsonEscape(string value)
        {
            StringBuilder result = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '\"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 32) result.Append("\\u").Append(((int)character).ToString("x4"));
                        else result.Append(character);
                        break;
                }
            }
            return result.ToString();
        }
    }
}
