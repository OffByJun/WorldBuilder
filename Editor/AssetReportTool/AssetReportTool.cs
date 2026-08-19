using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.AssetReportTool
{
    public sealed class AssetReportTool : IWorldBuilderTool
    {
        private const string OutputPath = "Assets/WorldBuilder/AssetReport.csv";

        private readonly List<string> textureRows = new List<string>();
        private readonly List<string> meshRows = new List<string>();

        private AssetReport report = new AssetReport();
        private ListView textureList;
        private ListView meshList;
        private Label textureTotal;
        private Label meshTotal;
        private Label statusLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.assetReport");
        public string Category => WorldBuilderCategory.Build;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public void OnSceneGUI()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.assetReport"));

            Button scan = new Button(Scan) { text = "Scan" };
            root.Add(scan);

            TabView tabs = new TabView();
            tabs.Add(BuildTextureTab());
            tabs.Add(BuildMeshTab());
            root.Add(tabs);

            Button export = new Button(Export) { text = "CSV Export" };
            root.Add(export);

            statusLabel = new Label(string.Empty);
            root.Add(statusLabel);

            return root;
        }

        private Tab BuildTextureTab()
        {
            Tab tab = new Tab("Textures");

            textureList = new ListView(textureRows, 18, () => new Label(), (e, i) => ((Label)e).text = textureRows[i])
            {
                selectionType = SelectionType.None
            };
            textureList.style.minHeight = 200f;
            tab.Add(textureList);

            textureTotal = new Label("Total: 0 MB");
            tab.Add(textureTotal);

            return tab;
        }

        private Tab BuildMeshTab()
        {
            Tab tab = new Tab("Meshes");

            meshList = new ListView(meshRows, 18, () => new Label(), (e, i) => ((Label)e).text = meshRows[i])
            {
                selectionType = SelectionType.None
            };
            meshList.style.minHeight = 200f;
            tab.Add(meshList);

            meshTotal = new Label("Total: 0 MB");
            tab.Add(meshTotal);

            return tab;
        }

        private void Scan()
        {
            report = AssetReportService.Scan();

            textureRows.Clear();
            for (int i = 0; i < report.textures.Count; i++)
            {
                TextureReportRow row = report.textures[i];
                textureRows.Add(row.name + "  |  " + row.resolution + "  |  " + row.format + "  |  " + row.memoryMb.ToString("F2") + " MB");
            }

            meshRows.Clear();
            for (int i = 0; i < report.meshes.Count; i++)
            {
                MeshReportRow row = report.meshes[i];
                meshRows.Add(row.name + "  |  v" + row.vertices + "  |  t" + row.triangles + "  |  " + row.memoryMb.ToString("F2") + " MB");
            }

            textureList?.Rebuild();
            meshList?.Rebuild();

            if (textureTotal != null)
            {
                textureTotal.text = "Total: " + report.TotalTextureMb.ToString("F2") + " MB";
            }

            if (meshTotal != null)
            {
                meshTotal.text = "Total: " + report.TotalMeshMb.ToString("F2") + " MB";
            }
        }

        private void Export()
        {
            string directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(OutputPath, AssetReportService.BuildCsv(report));
            AssetDatabase.Refresh();

            if (statusLabel != null)
            {
                statusLabel.text = "Exported: " + OutputPath;
            }
        }
    }
}
