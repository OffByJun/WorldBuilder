using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.EntityCatalogTool
{
    public sealed class EntityCatalogTool : IWorldBuilderTool
    {
        private EntityCatalogSnapshot snapshot;
        private VisualElement summary;
        private VisualElement issueList;
        private VisualElement distribution;

        public string ToolName => WorldBuilderLocalization.Get("tool.entityCatalog");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable() => snapshot = null;

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            root.Add(InspectorHelp.Build(ToolName, "help.entityCatalog"));

            Button refresh = new Button(Refresh) { text = "Scan Open Scenes" };
            root.Add(refresh);

            Button export = new Button(ExportCatalog) { text = "Export Catalog JSON for Blender" };
            root.Add(export);

            summary = new VisualElement();
            distribution = new VisualElement();
            issueList = new VisualElement();
            root.Add(summary);
            root.Add(distribution);
            root.Add(issueList);
            Refresh();
            return root;
        }

        private void ExportCatalog()
        {
            snapshot = EntityCatalogService.Collect();
            Rebuild();
            if (snapshot.CatalogCount == 0)
            {
                EditorUtility.DisplayDialog("WorldBuilder",
                    "No catalog entries were found. Open the scene holding WorldEntityRuntimeAuthoring first.", "OK");
                return;
            }
            string path = EditorUtility.SaveFilePanel("Export Entity Catalog", "",
                "WorldEntityCatalog.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, EntityCatalogService.Serialize(snapshot));
            AssetDatabase.Refresh();
            Debug.Log($"WorldBuilder: exported {snapshot.CatalogCount} entity catalog entries to {path}");
        }

        private void Refresh()
        {
            snapshot = EntityCatalogService.Collect();
            Rebuild();
        }

        private void Rebuild()
        {
            if (summary == null) return;
            summary.Clear();
            distribution.Clear();
            issueList.Clear();
            if (snapshot == null) return;

            summary.Add(new Label($"Catalog entries: {snapshot.CatalogCount}"));
            summary.Add(new Label($"Entity placements: {snapshot.PlacementCount}"));
            summary.Add(new Label($"Errors: {snapshot.ErrorCount}"));

            if (snapshot.PlacementsByKind.Count > 0)
            {
                Foldout kinds = new Foldout { text = "By kind", value = true };
                for (int i = 0; i < snapshot.PlacementsByKind.Count; i++)
                {
                    KeyValuePair<string, int> entry = snapshot.PlacementsByKind[i];
                    kinds.Add(new Label($"{entry.Key}: {entry.Value}"));
                }
                distribution.Add(kinds);
            }

            if (snapshot.PlacementsByLayer.Count > 0)
            {
                Foldout layers = new Foldout { text = "By authoring layer", value = true };
                for (int i = 0; i < snapshot.PlacementsByLayer.Count; i++)
                {
                    KeyValuePair<int, int> entry = snapshot.PlacementsByLayer[i];
                    layers.Add(new Label($"LV_{entry.Key:+0000;-0000;+0000}: {entry.Value}"));
                }
                distribution.Add(layers);
            }

            for (int i = 0; i < snapshot.Issues.Count; i++)
            {
                EntityCatalogIssue issue = snapshot.Issues[i];
                HelpBoxMessageType type = issue.Severity == EntityIssueSeverity.Error
                    ? HelpBoxMessageType.Error
                    : issue.Severity == EntityIssueSeverity.Warning
                        ? HelpBoxMessageType.Warning
                        : HelpBoxMessageType.Info;
                HelpBox box = new HelpBox($"[{issue.Code}] {issue.Message}", type);
                if (issue.Context != null)
                {
                    UnityEngine.Object context = issue.Context;
                    box.RegisterCallback<ClickEvent>(_ =>
                    {
                        Selection.activeObject = context;
                        EditorGUIUtility.PingObject(context);
                    });
                }
                issueList.Add(box);
            }
        }
    }
}
