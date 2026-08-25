using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using WorldBuilder.Authoring.Chunks;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;
using WorldBuilder.Entities.Authoring;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.BlenderBridge
{
    public sealed class ChunkImportResult
    {
        public ChunkManifest Manifest { get; }
        public string ChunkPrefabPath { get; }
        public bool WasRebuilt { get; }
        public WorldBakeReport Report { get; }

        public ChunkImportResult(ChunkManifest manifest, string prefabPath, bool rebuilt, WorldBakeReport report)
        {
            Manifest = manifest;
            ChunkPrefabPath = prefabPath;
            WasRebuilt = rebuilt;
            Report = report;
        }
    }

    public static class ChunkImportPipeline
    {
        public static ChunkImportResult Import(string manifestAssetPath, BlenderBridgeSettings bridge)
        {
            WorldBakeReport report = Validate(manifestAssetPath, bridge, out ChunkManifest manifest);
            if (report.HasErrors) return new ChunkImportResult(manifest, string.Empty, false, report);

            BakeManifest bakeManifest = ChunkBakePrefabAssembler.LoadAdjacent(manifestAssetPath, manifest.worldId,
                manifest.chunk.x, manifest.chunk.z, bridge.WorldGrid, report);
            Dictionary<string, string> contentAssets = ValidateFiles(manifestAssetPath, manifest, report);
            ChunkPlacementDocument placements = LoadPlacements(manifest, contentAssets, report);
            if (placements != null) report.Merge(ChunkManifestCodec.ValidatePlacements(placements, manifest));
            ValidateRegistry(placements, bridge.AssetRegistry, report);
            report.Sort();
            if (report.HasErrors) return new ChunkImportResult(manifest, string.Empty, false, report);

            string worldRoot = bridge.GeneratedRoot + "/" + manifest.worldId;
            string chunksFolder = worldRoot + "/Chunks";
            string regionsFolder = worldRoot + "/Regions";
            EnsureFolder(chunksFolder);
            EnsureFolder(regionsFolder);

            ChunkCoord coordinate = new ChunkCoord(manifest.chunk.x, manifest.chunk.z);
            string prefabPath = chunksFolder + "/" + WorldCoordNaming.ChunkName(coordinate) + ".prefab";
            string sourceHash = ChunkBakePrefabAssembler.ComputeSourceHash(manifest.contentHash, bakeManifest,
                bridge.LodTransitionHeights);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ChunkRoot existingRoot = existing != null ? existing.GetComponent<ChunkRoot>() : null;
            if (existingRoot != null && string.Equals(existingRoot.SourceHash, sourceHash, StringComparison.Ordinal))
                return new ChunkImportResult(manifest, prefabPath, false, report);

            bool loadedPrefabContents = existing != null;
            GameObject root = loadedPrefabContents
                ? PrefabUtility.LoadPrefabContents(prefabPath)
                : new GameObject(WorldCoordNaming.ChunkName(coordinate));
            try
            {
                ChunkRoot chunkRoot = root.GetComponent<ChunkRoot>();
                if (chunkRoot == null) chunkRoot = root.AddComponent<ChunkRoot>();
                chunkRoot.Configure(coordinate, sourceHash, manifestAssetPath);
                Transform generated = ChunkBakePrefabAssembler.ResetGeneratedRoot(root.transform);
                GameObject geometry = AddModel(contentAssets, "geometry", generated, false, false, report);
                ValidateGeometryBounds(geometry, bridge, manifestAssetPath, report);
                GameObject collision = AddModel(contentAssets, "collision", generated, true, bakeManifest == null, report);
                AddPlacements(placements, bridge.AssetRegistry, generated, report);
                ChunkBakePrefabAssembler.Assemble(bakeManifest, geometry, collision, bridge.LodTransitionHeights, report);
                if (report.HasErrors) return new ChunkImportResult(manifest, string.Empty, false, report);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                if (loadedPrefabContents) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }

            RegionCatalogBuilder.RebuildRegion(bridge, bridge.WorldGrid.CreateGrid().ChunkToRegion(coordinate));
            AssetDatabase.SaveAssets();
            return new ChunkImportResult(manifest, prefabPath, true, report);
        }

        /// <summary>
        /// Dry-run validation: parses and validates the manifest, content files and placements
        /// without importing or rebuilding anything.
        /// </summary>
        public static WorldBakeReport Validate(string manifestAssetPath, BlenderBridgeSettings bridge,
            out ChunkManifest manifest)
        {
            WorldBakeReport report = new WorldBakeReport();
            if (bridge == null || bridge.WorldGrid == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_BRIDGE_SETTINGS", manifestAssetPath,
                    "BlenderBridgeSettings with WorldGridSettings is required.");
                manifest = null;
                return report;
            }

            try
            {
                manifest = ChunkManifestCodec.Parse(File.ReadAllText(ToAbsolutePath(manifestAssetPath)));
            }
            catch (Exception exception)
            {
                report.Add(BakeIssueSeverity.Error, "WB_MANIFEST_PARSE", manifestAssetPath, exception.Message);
                manifest = null;
                return report;
            }

            report.Merge(ChunkManifestCodec.Validate(manifest, bridge.WorldGrid));
            Dictionary<string, string> contentAssets = ValidateFiles(manifestAssetPath, manifest, report);
            ChunkPlacementDocument placements = LoadPlacements(manifest, contentAssets, report);
            if (placements != null) report.Merge(ChunkManifestCodec.ValidatePlacements(placements, manifest));
            ValidateRegistry(placements, bridge.AssetRegistry, report);
            report.Sort();
            return report;
        }

        private static void ValidateGeometryBounds(GameObject geometry, BlenderBridgeSettings bridge,
            string manifestPath, WorldBakeReport report)
        {
            if (geometry == null || bridge?.WorldGrid == null) return;
            Renderer[] renderers = geometry.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float chunkSize = bridge.WorldGrid.AuthoringChunkSize;
            if (bounds.size.x > chunkSize || bounds.size.z > chunkSize)
                report.Add(BakeIssueSeverity.Warning, "WB_IMPORT_GEOMETRY_BOUNDS", manifestPath,
                    $"Geometry footprint {bounds.size.x:F1}x{bounds.size.z:F1} exceeds the chunk size {chunkSize:F0}; " +
                    "verify the Blender export origin or the chunk assignment.");
        }

        private static Dictionary<string, string> ValidateFiles(string manifestPath, ChunkManifest manifest,
            WorldBakeReport report)
        {
            Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
            ValidateFile("geometry", manifestPath, manifest.content?.geometry, report, paths);
            ValidateFile("collision", manifestPath, manifest.content?.collision, report, paths);
            ValidateFile("placements", manifestPath, manifest.content?.placements, report, paths);
            return paths;
        }

        private static void ValidateFile(string key, string manifestPath, ChunkFileReference reference,
            WorldBakeReport report, Dictionary<string, string> paths)
        {
            if (reference == null || !reference.IsPresent) return;
            string manifestAbsolute = ToAbsolutePath(manifestPath);
            string fullPath = ChunkManifestCodec.ResolveContentPath(manifestAbsolute, reference);
            string manifestDirectory = Path.GetFullPath(Path.GetDirectoryName(manifestAbsolute) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(manifestDirectory, StringComparison.OrdinalIgnoreCase))
            {
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_PATH_ESCAPE", manifestPath,
                    $"{key} escapes the chunk directory.");
                return;
            }
            if (!File.Exists(fullPath))
            {
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_MISSING_FILE", manifestPath,
                    $"{key} file '{reference.path}' does not exist.");
                return;
            }
            FileInfo info = new FileInfo(fullPath);
            if (info.Length != reference.bytes)
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_FILE_SIZE", manifestPath,
                    $"{key} size differs from the manifest.");
            string hash = HashFile(fullPath);
            if (!string.Equals(hash, reference.sha256, StringComparison.OrdinalIgnoreCase))
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_FILE_HASH", manifestPath,
                    $"{key} SHA-256 differs from the manifest.");
            string assetPath = FileUtil.GetProjectRelativePath(fullPath).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_OUTSIDE_ASSETS", manifestPath,
                    $"{key} must be exported below the Unity project's Assets folder.");
            else
                paths[key] = assetPath;
        }

        private static ChunkPlacementDocument LoadPlacements(ChunkManifest manifest,
            Dictionary<string, string> paths, WorldBakeReport report)
        {
            if (!paths.TryGetValue("placements", out string assetPath)) return null;
            try { return ChunkManifestCodec.ParsePlacements(File.ReadAllText(ToAbsolutePath(assetPath))); }
            catch (Exception exception)
            {
                report.Add(BakeIssueSeverity.Error, "WB_PLACEMENTS_PARSE", manifest.worldId, exception.Message);
                return null;
            }
        }

        private static void ValidateRegistry(ChunkPlacementDocument placements, BlenderAssetRegistry registry,
            WorldBakeReport report)
        {
            if (placements?.objects == null) return;
            for (int i = 0; i < placements.objects.Length; i++)
            {
                ChunkPlacementRecord item = placements.objects[i];
                if (item == null || !item.UsesAsset) continue;
                if (registry == null || !registry.TryGetPrefab(item.assetId, out GameObject prefab))
                {
                    report.Add(BakeIssueSeverity.Error, "WB_IMPORT_UNKNOWN_ASSET", item.stableId,
                        $"No Unity prefab is registered for Blender assetId '{item.assetId}'.");
                    continue;
                }
                if (!item.IsEntity) continue;
                WorldEntityAuthoring authoring = prefab.GetComponent<WorldEntityAuthoring>();
                if (authoring == null)
                {
                    report.Add(BakeIssueSeverity.Error, "WB_IMPORT_ENTITY_AUTHORING", item.stableId,
                        $"Prefab '{prefab.name}' is placed as an ENTITY but has no WorldEntityAuthoring.");
                    continue;
                }
                if (item.entity != null && authoring.PrefabId != item.entity.prefabId)
                    report.Add(BakeIssueSeverity.Error, "WB_IMPORT_ENTITY_PREFAB_ID", item.stableId,
                        $"Blender entity prefabId {item.entity.prefabId} does not match '{prefab.name}' id {authoring.PrefabId}.");
            }
        }

        private static GameObject AddModel(Dictionary<string, string> paths, string key, Transform parent,
            bool collision, bool generateMeshColliders, WorldBakeReport report)
        {
            if (!paths.TryGetValue(key, out string assetPath)) return null;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_MODEL", assetPath, "Unity did not import this FBX as a model.");
                return null;
            }
            GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_IMPORT_MODEL_INSTANCE", assetPath, "Could not instantiate the imported model.");
                return null;
            }
            instance.name = collision ? "Collision" : "Geometry";
            instance.transform.SetParent(parent, false);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (!collision) return instance;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
            if (!generateMeshColliders) return instance;
            MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null) continue;
                MeshCollider collider = filters[i].gameObject.GetComponent<MeshCollider>();
                if (collider == null) collider = filters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filters[i].sharedMesh;
            }
            return instance;
        }

        private static void AddPlacements(ChunkPlacementDocument document, BlenderAssetRegistry registry,
            Transform parent, WorldBakeReport report)
        {
            if (document?.objects == null) return;
            Array.Sort(document.objects, (left, right) => string.CompareOrdinal(left?.stableId, right?.stableId));
            Transform entityRoot = null;
            for (int i = 0; i < document.objects.Length; i++)
            {
                ChunkPlacementRecord item = document.objects[i];
                if (item == null) continue;
                GameObject instance;
                Transform target = parent;
                if (item.UsesAsset)
                {
                    if (registry == null || !registry.TryGetPrefab(item.assetId, out GameObject prefab)) continue;
                    instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (item.IsEntity && instance != null)
                    {
                        if (entityRoot == null) entityRoot = CreateEntityRoot(parent);
                        target = entityRoot;
                        ChunkEntityRecord entity = item.entity ?? new ChunkEntityRecord();
                        instance.AddComponent<ChunkEntityPlacement>()
                            .Configure(item.stableId, item.assetId, entity.prefabId, entity.kind, item.layer);
                    }
                }
                else
                {
                    instance = new GameObject(string.IsNullOrWhiteSpace(item.name) ? "Marker" : item.name);
                    ChunkPropertyRecord[] source = item.properties ?? Array.Empty<ChunkPropertyRecord>();
                    ChunkMarkerProperty[] values = new ChunkMarkerProperty[source.Length];
                    for (int property = 0; property < source.Length; property++)
                        values[property] = new ChunkMarkerProperty(source[property]?.key, source[property]?.value);
                    string markerType = FindProperty(source, "marker_type");
                    instance.AddComponent<ChunkMarker>().Configure(item.stableId, markerType, values);
                }
                if (instance == null)
                {
                    report.Add(BakeIssueSeverity.Error, "WB_IMPORT_PLACEMENT", item.stableId, "Could not instantiate placement.");
                    continue;
                }
                instance.name = string.IsNullOrWhiteSpace(item.name) ? item.stableId : item.name;
                instance.transform.SetParent(target, false);
                ApplyMatrix(instance.transform, item.matrix);
            }
        }

        private static Transform CreateEntityRoot(Transform parent)
        {
            GameObject root = new GameObject("Entities");
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static string FindProperty(ChunkPropertyRecord[] values, string key)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] != null && string.Equals(values[i].key, key, StringComparison.Ordinal)) return values[i].value;
            return string.Empty;
        }

        internal static void ApplyMatrix(Transform target, float[] values)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                matrix[row, column] = values[row * 4 + column];
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            Vector3 scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) scale.x = -scale.x;
            Vector3 forward = scale.z > 0.000001f ? z / scale.z : Vector3.forward;
            Vector3 up = scale.y > 0.000001f ? y / scale.y : Vector3.up;
            target.localPosition = matrix.GetColumn(3);
            target.localRotation = Quaternion.LookRotation(forward, up);
            target.localScale = scale;
        }

        internal static void EnsureFolder(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath)) return Path.GetFullPath(assetPath);
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }
    }
}
