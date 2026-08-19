using System;
using UnityEngine;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Baking.BlenderBridge
{
    public static class BakeManifestCodec
    {
        public static BakeManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Bake manifest JSON is empty.", nameof(json));
            BakeManifest value = JsonUtility.FromJson<BakeManifest>(json);
            if (value == null) throw new FormatException("Bake manifest JSON could not be parsed.");
            return value;
        }

        public static string Serialize(BakeManifest value, bool prettyPrint = true)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return JsonUtility.ToJson(value, prettyPrint);
        }

        public static WorldBakeReport Validate(BakeManifest value, WorldGridSettings settings)
        {
            WorldBakeReport report = new WorldBakeReport();
            if (value == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_NULL", string.Empty, "Bake manifest is null.");
                return report;
            }
            string path = value.worldId ?? string.Empty;
            if (value.schemaVersion != BakeManifest.CurrentSchemaVersion)
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_VERSION", path,
                    $"Expected schema {BakeManifest.CurrentSchemaVersion}, got {value.schemaVersion}.");
            if (settings == null)
                report.Add(BakeIssueSeverity.Error, "WB_GRID_SETTINGS_NULL", path, "WorldGridSettings is required.");
            else if (!string.Equals(value.worldId, settings.WorldId, StringComparison.Ordinal))
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_WORLD", path,
                    $"Expected worldId '{settings.WorldId}', got '{value.worldId}'.");
            if (value.chunk == null)
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_CHUNK", path, "chunk is required.");
            if (!IsSha256(value.profileHash))
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_PROFILE_HASH", path, "profileHash must be SHA-256.");

            BakeManifestObject[] objects = value.objects ?? Array.Empty<BakeManifestObject>();
            Array.Sort(objects, CompareObjects);
            string previous = null;
            for (int i = 0; i < objects.Length; i++)
            {
                BakeManifestObject item = objects[i];
                if (item == null || string.IsNullOrWhiteSpace(item.stableId))
                {
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_OBJECT_ID", path, "Every baked object requires stableId.");
                    continue;
                }
                if (string.Equals(previous, item.stableId, StringComparison.Ordinal))
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_OBJECT_DUPLICATE", item.stableId, "Baked stable IDs must be unique.");
                previous = item.stableId;
                ValidateLods(item, report);
                if (item.collider != null && (string.IsNullOrWhiteSpace(item.collider.type) || string.IsNullOrWhiteSpace(item.collider.fileObject)))
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_COLLIDER", item.stableId, "Collider type and fileObject are required together.");
            }
            ValidateVertexAttributes(value.vertexAttributes, path, report);
            report.Sort();
            return report;
        }

        private static int CompareObjects(BakeManifestObject left, BakeManifestObject right) =>
            string.CompareOrdinal(left?.stableId, right?.stableId);

        private static void ValidateLods(BakeManifestObject item, WorldBakeReport report)
        {
            BakeManifestLod[] lods = item.lods ?? Array.Empty<BakeManifestLod>();
            Array.Sort(lods, (left, right) => (left?.level ?? int.MinValue).CompareTo(right?.level ?? int.MinValue));
            int previous = -1;
            for (int i = 0; i < lods.Length; i++)
            {
                BakeManifestLod lod = lods[i];
                if (lod == null || lod.level < 0 || lod.level <= previous || string.IsNullOrWhiteSpace(lod.fileObject) || lod.triangles < 0)
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_LOD", item.stableId,
                        "LOD levels must be unique, ascending, non-negative, and reference a fileObject with non-negative triangles.");
                if (lod != null) previous = lod.level;
            }
        }

        private static void ValidateVertexAttributes(VertexAttributeContract[] values, string path, WorldBakeReport report)
        {
            values = values ?? Array.Empty<VertexAttributeContract>();
            for (int i = 0; i < values.Length; i++)
            {
                VertexAttributeContract value = values[i];
                if (value == null || string.IsNullOrWhiteSpace(value.name) ||
                    (value.domain != "POINT" && value.domain != "CORNER") || value.channels == null)
                    report.Add(BakeIssueSeverity.Error, "WB_VERTEX_ATTRIBUTE_CONTRACT", path,
                        "Vertex attributes require name, POINT/CORNER domain, and RGBA channels.");
            }
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
    }
}
