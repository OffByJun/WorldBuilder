using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Entities;
using WorldBuilder.Entities.Authoring;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.EntityCatalogTool
{
    public enum EntityIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class EntityCatalogIssue
    {
        public EntityIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }

        public EntityCatalogIssue(EntityIssueSeverity severity, string code, string message, UnityEngine.Object context)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Context = context;
        }
    }

    [Serializable]
    public sealed class EntityCatalogExportRecord
    {
        public int prefabId;
        public string name = string.Empty;
        public string kind = "Generic";
        public string[] flags = Array.Empty<string>();
        public float lifetimeSeconds;
    }

    [Serializable]
    public sealed class EntityCatalogExportDocument
    {
        public int schemaVersion = 1;
        public EntityCatalogExportRecord[] entities = Array.Empty<EntityCatalogExportRecord>();
    }

    public sealed class EntityCatalogSnapshot
    {
        public IReadOnlyList<EntityCatalogExportRecord> Catalog { get; }
        public int CatalogCount { get; }
        public int PlacementCount { get; }
        public IReadOnlyList<EntityCatalogIssue> Issues { get; }
        public IReadOnlyList<KeyValuePair<string, int>> PlacementsByKind { get; }
        public IReadOnlyList<KeyValuePair<int, int>> PlacementsByLayer { get; }

        public EntityCatalogSnapshot(List<EntityCatalogExportRecord> catalog, int placementCount,
            List<EntityCatalogIssue> issues, List<KeyValuePair<string, int>> byKind,
            List<KeyValuePair<int, int>> byLayer)
        {
            Catalog = catalog;
            CatalogCount = catalog.Count;
            PlacementCount = placementCount;
            Issues = issues;
            PlacementsByKind = byKind;
            PlacementsByLayer = byLayer;
        }

        public int ErrorCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == EntityIssueSeverity.Error) count++;
                return count;
            }
        }
    }

    public static class EntityCatalogService
    {
        public static EntityCatalogSnapshot Collect()
        {
            WorldEntityRuntimeAuthoring[] runtimes = UnityEngine.Object.FindObjectsByType<WorldEntityRuntimeAuthoring>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            ChunkEntityPlacement[] placements = UnityEngine.Object.FindObjectsByType<ChunkEntityPlacement>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            List<EntityCatalogIssue> issues = new List<EntityCatalogIssue>();
            List<EntityCatalogExportRecord> catalog = BuildCatalog(runtimes, issues);
            HashSet<int> catalogIds = new HashSet<int>();
            for (int i = 0; i < catalog.Count; i++) catalogIds.Add(catalog[i].prefabId);
            Dictionary<string, int> byKind = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<int, int> byLayer = new Dictionary<int, int>();

            if (runtimes.Length == 0)
                issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Warning, "WB_ENTITY_NO_RUNTIME",
                    "No WorldEntityRuntimeAuthoring is open. Entity placements cannot spawn without one.", null));
            else if (runtimes.Length > 1)
                issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Warning, "WB_ENTITY_MULTIPLE_RUNTIME",
                    $"{runtimes.Length} WorldEntityRuntimeAuthoring components are open. Only one should be baked.",
                    runtimes[0]));

            for (int i = 0; i < placements.Length; i++)
            {
                ChunkEntityPlacement placement = placements[i];
                string kind = string.IsNullOrWhiteSpace(placement.Kind) ? "Generic" : placement.Kind;
                byKind[kind] = byKind.TryGetValue(kind, out int kindCount) ? kindCount + 1 : 1;
                byLayer[placement.AuthoringLayer] =
                    byLayer.TryGetValue(placement.AuthoringLayer, out int layerCount) ? layerCount + 1 : 1;

                if (placement.GetComponent<WorldEntityAuthoring>() == null)
                    issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_PLACEMENT_AUTHORING",
                        $"'{placement.name}' has no WorldEntityAuthoring and will not bake into an entity.", placement));
                else if (runtimes.Length > 0 && !catalogIds.Contains(placement.PrefabId))
                    issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_PLACEMENT_UNKNOWN_ID",
                        $"'{placement.name}' uses prefab id {placement.PrefabId}, which is not in the runtime catalog.",
                        placement));
            }

            List<KeyValuePair<string, int>> kinds = new List<KeyValuePair<string, int>>(byKind);
            kinds.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            List<KeyValuePair<int, int>> lay = new List<KeyValuePair<int, int>>(byLayer);
            lay.Sort((left, right) => left.Key.CompareTo(right.Key));
            issues.Sort((left, right) => right.Severity.CompareTo(left.Severity));
            return new EntityCatalogSnapshot(catalog, placements.Length, issues, kinds, lay);
        }

        public static string Serialize(EntityCatalogSnapshot snapshot)
        {
            EntityCatalogExportDocument document = new EntityCatalogExportDocument();
            EntityCatalogExportRecord[] records = new EntityCatalogExportRecord[snapshot.Catalog.Count];
            for (int i = 0; i < snapshot.Catalog.Count; i++) records[i] = snapshot.Catalog[i];
            document.entities = records;
            return JsonUtility.ToJson(document, true);
        }

        private static string[] FlagNames(WorldEntityFlags flags)
        {
            List<string> names = new List<string>(3);
            if ((flags & WorldEntityFlags.Persistent) != 0) names.Add("Persistent");
            if ((flags & WorldEntityFlags.RegionStreamed) != 0) names.Add("RegionStreamed");
            if ((flags & WorldEntityFlags.Replicated) != 0) names.Add("Replicated");
            return names.ToArray();
        }

        private static List<EntityCatalogExportRecord> BuildCatalog(WorldEntityRuntimeAuthoring[] runtimes,
            List<EntityCatalogIssue> issues)
        {
            List<EntityCatalogExportRecord> catalog = new List<EntityCatalogExportRecord>();
            HashSet<int> registered = new HashSet<int>();
            for (int i = 0; i < runtimes.Length; i++)
            {
                WorldEntityRuntimeAuthoring runtime = runtimes[i];
                if (runtime.GridSettings == null)
                    issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_NO_GRID",
                        $"'{runtime.name}' has no WorldGridSettings and will not bake.", runtime));

                IReadOnlyList<WorldEntityPrefabEntry> entries = runtime.Prefabs;
                for (int entry = 0; entry < entries.Count; entry++)
                {
                    WorldEntityPrefabEntry value = entries[entry];
                    if (value.Prefab == null)
                    {
                        issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_CATALOG_NULL",
                            $"Catalog slot {entry} on '{runtime.name}' has no prefab.", runtime));
                        continue;
                    }
                    WorldEntityAuthoring authoring = value.Prefab.GetComponent<WorldEntityAuthoring>();
                    if (authoring == null)
                    {
                        issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_CATALOG_AUTHORING",
                            $"Catalog prefab '{value.Prefab.name}' requires WorldEntityAuthoring.", value.Prefab));
                        continue;
                    }
                    if (authoring.PrefabId != value.PrefabId)
                    {
                        issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_CATALOG_ID_MISMATCH",
                            $"Catalog id {value.PrefabId} does not match '{value.Prefab.name}' id {authoring.PrefabId}.",
                            value.Prefab));
                        continue;
                    }
                    if (!registered.Add(value.PrefabId))
                    {
                        issues.Add(new EntityCatalogIssue(EntityIssueSeverity.Error, "WB_ENTITY_CATALOG_DUPLICATE",
                            $"Prefab id {value.PrefabId} is registered more than once.", value.Prefab));
                        continue;
                    }
                    catalog.Add(new EntityCatalogExportRecord
                    {
                        prefabId = value.PrefabId,
                        name = value.Prefab.name,
                        kind = authoring.Kind.ToString(),
                        flags = FlagNames(authoring.Flags),
                        lifetimeSeconds = authoring.LifetimeSeconds
                    });
                }
            }
            catalog.Sort((left, right) => left.prefabId.CompareTo(right.prefabId));
            return catalog;
        }
    }
}
