using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WorldBuilder.Baking.BlenderBridge;
using WorldBuilder.Baking.Core;

namespace WorldBuilder.Editor.BlenderBridge
{
    public static class ChunkBakePrefabAssembler
    {
        public const string GeneratedRootName = "__WorldBuilderGenerated";

        public static BakeManifest LoadAdjacent(string chunkManifestAssetPath, string worldId, int chunkX, int chunkZ,
            WorldBuilder.Runtime.Grid.WorldGridSettings settings, WorldBakeReport report)
        {
            string absolute = ToAbsolutePath(chunkManifestAssetPath);
            string directory = Path.GetDirectoryName(absolute) ?? string.Empty;
            if (!Directory.Exists(directory)) return null;
            string[] files = Directory.GetFiles(directory, "*.bake.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            BakeManifest selected = null;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    BakeManifest candidate = BakeManifestCodec.Parse(File.ReadAllText(files[i]));
                    if (!string.Equals(candidate.worldId, worldId, StringComparison.Ordinal) || candidate.chunk == null ||
                        candidate.chunk.x != chunkX || candidate.chunk.z != chunkZ) continue;
                    if (selected != null)
                    {
                        report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_MULTIPLE", chunkManifestAssetPath,
                            "More than one bake manifest targets this chunk.");
                        return null;
                    }
                    selected = candidate;
                }
                catch (Exception exception)
                {
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_MANIFEST_PARSE", files[i], exception.Message);
                }
            }
            if (selected != null) report.Merge(BakeManifestCodec.Validate(selected, settings));
            return selected;
        }

        public static string ComputeSourceHash(string chunkHash, BakeManifest manifest, float[] transitions)
        {
            if (manifest == null) return chunkHash ?? string.Empty;
            StringBuilder input = new StringBuilder(chunkHash ?? string.Empty).Append('|').Append(manifest.profileHash ?? string.Empty);
            transitions = BlenderBridgeSettings.SanitizeLodTransitions(transitions);
            for (int i = 0; i < transitions.Length; i++) input.Append('|').Append(transitions[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input.ToString()));
                StringBuilder output = new StringBuilder(64);
                for (int i = 0; i < bytes.Length; i++) output.Append(bytes[i].ToString("x2"));
                return output.ToString();
            }
        }

        public static Transform ResetGeneratedRoot(Transform root)
        {
            Transform previous = root.Find(GeneratedRootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            GameObject generated = new GameObject(GeneratedRootName);
            generated.transform.SetParent(root, false);
            return generated.transform;
        }

        public static void Assemble(BakeManifest manifest, GameObject geometryRoot, GameObject collisionRoot,
            float[] transitions, WorldBakeReport report)
        {
            if (manifest == null || geometryRoot == null) return;
            Dictionary<string, Transform> geometryByName = BuildNameLookup(geometryRoot.transform, report);
            Dictionary<string, Transform> collisionByName = collisionRoot != null
                ? BuildNameLookup(collisionRoot.transform, report)
                : new Dictionary<string, Transform>(StringComparer.Ordinal);
            BakeManifestObject[] objects = manifest.objects ?? Array.Empty<BakeManifestObject>();
            Array.Sort(objects, (a, b) => string.CompareOrdinal(a?.stableId, b?.stableId));
            transitions = BlenderBridgeSettings.SanitizeLodTransitions(transitions);
            Transform assemblyRoot = new GameObject("BakeAssembly").transform;
            assemblyRoot.SetParent(geometryRoot.transform.parent, false);
            for (int i = 0; i < objects.Length; i++)
            {
                BakeManifestObject item = objects[i];
                if (item == null) continue;
                GameObject group = new GameObject("WB_" + SafeName(item.stableId));
                group.transform.SetParent(assemblyRoot, false);
                AssembleLods(item, group, geometryByName, transitions, report);
                AssembleCollider(item, collisionByName, geometryByName, report);
            }
        }

        private static void AssembleLods(BakeManifestObject item, GameObject group, Dictionary<string, Transform> byName,
            float[] transitions, WorldBakeReport report)
        {
            BakeManifestLod[] source = item.lods ?? Array.Empty<BakeManifestLod>();
            Array.Sort(source, (a, b) => (a?.level ?? int.MaxValue).CompareTo(b?.level ?? int.MaxValue));
            List<LOD> lods = new List<LOD>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                BakeManifestLod entry = source[i];
                if (entry == null || !byName.TryGetValue(entry.fileObject ?? string.Empty, out Transform target))
                {
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_LOD_OBJECT_MISSING", item.stableId,
                        $"LOD object '{entry?.fileObject}' was not found in the geometry model.");
                    continue;
                }
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_LOD_RENDERER_MISSING", item.stableId,
                        $"LOD object '{entry.fileObject}' has no Renderer.");
                    continue;
                }
                float height = transitions[Math.Min(i, transitions.Length - 1)];
                lods.Add(new LOD(height, renderers));
            }
            if (lods.Count == 0) return;
            LODGroup lodGroup = group.AddComponent<LODGroup>();
            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();
        }

        private static void AssembleCollider(BakeManifestObject item, Dictionary<string, Transform> collisionByName,
            Dictionary<string, Transform> geometryByName, WorldBakeReport report)
        {
            BakeManifestCollider source = item.collider;
            if (source == null) return;
            if (!collisionByName.TryGetValue(source.fileObject ?? string.Empty, out Transform target) &&
                !geometryByName.TryGetValue(source.fileObject ?? string.Empty, out target))
            {
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_COLLIDER_OBJECT_MISSING", item.stableId,
                    $"Collider object '{source.fileObject}' was not found in the geometry model.");
                return;
            }
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
            MeshFilter filter = target.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                report.Add(BakeIssueSeverity.Error, "WB_BAKE_COLLIDER_MESH_MISSING", item.stableId,
                    $"Collider object '{source.fileObject}' has no MeshFilter mesh.");
                return;
            }
            string type = (source.type ?? string.Empty).ToUpperInvariant();
            Bounds bounds = mesh.bounds;
            switch (type)
            {
                case "BOX":
                    BoxCollider box = target.gameObject.AddComponent<BoxCollider>();
                    box.center = bounds.center; box.size = bounds.size;
                    break;
                case "SPHERE":
                    SphereCollider sphere = target.gameObject.AddComponent<SphereCollider>();
                    sphere.center = bounds.center; sphere.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    break;
                case "CAPSULE":
                    CapsuleCollider capsule = target.gameObject.AddComponent<CapsuleCollider>();
                    ConfigureCapsule(capsule, bounds);
                    break;
                case "CONVEX_HULL":
                    MeshCollider convex = target.gameObject.AddComponent<MeshCollider>();
                    convex.sharedMesh = mesh; convex.convex = true;
                    break;
                case "DECIMATED_MESH":
                case "COPY_VISUAL":
                    MeshCollider collider = target.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    break;
                default:
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_COLLIDER_TYPE", item.stableId,
                        $"Unsupported collider type '{source.type}'.");
                    break;
            }
        }

        private static void ConfigureCapsule(CapsuleCollider collider, Bounds bounds)
        {
            Vector3 size = bounds.size;
            collider.center = bounds.center;
            collider.direction = size.x >= size.y && size.x >= size.z ? 0 : size.z >= size.y ? 2 : 1;
            float height = collider.direction == 0 ? size.x : collider.direction == 1 ? size.y : size.z;
            float radiusA = collider.direction == 0 ? size.y : size.x;
            float radiusB = collider.direction == 2 ? size.y : size.z;
            collider.radius = Mathf.Max(0.0001f, Mathf.Max(radiusA, radiusB) * 0.5f);
            collider.height = Mathf.Max(height, collider.radius * 2f);
        }

        private static Dictionary<string, Transform> BuildNameLookup(Transform root, WorldBakeReport report)
        {
            Dictionary<string, Transform> result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] values = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == root) continue;
                if (!result.TryAdd(values[i].name, values[i]))
                    report.Add(BakeIssueSeverity.Error, "WB_BAKE_OBJECT_NAME_DUPLICATE", values[i].name,
                        "Imported bake object names must be unique within the geometry model.");
            }
            return result;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static string ToAbsolutePath(string path) => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }
}
