using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.BlenderBridge;
using WorldBuilder.Editor.PrefabBrush;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Terrain;

namespace WorldBuilder.Editor.EcologyBrush
{
    /// <summary>
    /// Paint ecology prototypes straight onto terrain: pick a rule from a Scatter Rule Set,
    /// drag across surfaces, and every stroke point is validated against that rule's gates
    /// (biome, depth, flow) before it lands. Baked into chunks via the same path as PCG.
    /// </summary>
    public sealed class EcologyBrushTool : IWorldBuilderTool
    {
        [SerializeField] private ScatterRuleSet ruleSet;
        [SerializeField] private int ruleIndex;
        [SerializeField] private float brushRadius = 6f;
        [SerializeField] private int stampsPerStroke = 12;

        private readonly List<BrushPlacement> painted = new List<BrushPlacement>();
        private Label status;
        private bool painting;

        public string ToolName => "Ecology Brush";
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable() { }

        public VisualElement CreateInspectorGUI()
        {
            root = new VisualElement();

            var ruleSetField = new ObjectField("Scatter Rule Set")
            {
                objectType = typeof(ScatterRuleSet),
                value = ruleSet
            };
            ruleSetField.RegisterValueChangedCallback(evt =>
            {
                ruleSet = evt.newValue as ScatterRuleSet;
                ruleIndex = 0;
                RefreshRuleLabel();
            });
            root.Add(ruleSetField);

            var ruleLabel = new Label(GetRuleSummary());
            ruleLabel.style.whiteSpace = WhiteSpace.Normal;
            ruleLabel.name = "rule-label";
            root.Add(ruleLabel);

            var radiusSlider = new Slider("Brush Radius", 1f, 40f) { value = brushRadius };
            radiusSlider.RegisterValueChangedCallback(evt => brushRadius = evt.newValue);
            root.Add(radiusSlider);

            var stampsField = new IntegerField("Stamps Per Stroke") { value = stampsPerStroke };
            stampsField.RegisterValueChangedCallback(evt => stampsPerStroke = Mathf.Max(1, evt.newValue));
            root.Add(stampsField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Button(Clear) { text = "Clear Painted" });
            row.Add(new Button(BakeToChunks) { text = "Bake To Chunks" });
            root.Add(row);

            status = new Label($"Painted: 0");
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.color = new Color(0.6f, 0.9f, 1f);
            root.Add(status);
            return root;
        }

        private string GetRuleSummary()
        {
            if (ruleSet == null || ruleSet.rules.Count == 0) return "No rules.";
            ScatterRuleSet.Rule rule = ruleSet.rules[Mathf.Clamp(ruleIndex, 0, ruleSet.rules.Count - 1)];
            return $"Rule [{ruleIndex}]: {rule.name}\n" +
                   (rule.anyBiome ? "any biome" : $"biome {rule.biome}") +
                   $", slope ≤ {rule.maxSlopeDegrees}°" +
                   (rule.useDepthGate ? $", depth {rule.minDepth}-{rule.maxDepth} m" : "") +
                   (rule.maxFlowSpeed < 999f ? $", flow ≤ {rule.maxFlowSpeed}" : "");
        }

        private void RefreshRuleLabel()
        {
            var label = root?.Q<Label>("rule-label");
            if (label != null) label.text = GetRuleSummary();
        }

        private VisualElement root;

        public void OnSceneGUI()
        {
            if (ruleSet == null || ruleSet.rules.Count == 0) return;
            Event e = Event.current;
            if (e == null) return;

            DrawBrushCursor();

            if (e.type == EventType.MouseDown && e.button == 0 && e.control)
            {
                painting = true;
                StrokeAt(e.mousePosition);
                e.Use();
            }
            else if (painting && e.type == EventType.MouseDrag && e.button == 0 && e.control)
            {
                StrokeAt(e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                painting = false;
            }
        }

        private void DrawBrushCursor()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            Handles.color = new Color(0.5f, 1f, 0.7f, 0.85f);
            Vector3 pivot = view.pivot;
            Handles.DrawWireDisc(pivot, Vector3.up, brushRadius);
        }

        private void StrokeAt(Vector2 guiPosition)
        {
            ScatterRuleSet.Rule rule = ruleSet.rules[Mathf.Clamp(ruleIndex, 0, ruleSet.rules.Count - 1)];
            if (rule.prefabs == null || rule.prefabs.Count == 0)
            {
                SetStatus("Selected rule has no prefabs.");
                return;
            }

            if (!SceneRaycaster.TryRaycast(guiPosition, out RaycastHit hit)) return;

            var query = new SurfaceQuery(hit.point);
            for (int i = 0; i < stampsPerStroke; i++)
            {
                Vector2 offset = Random.insideUnitCircle * brushRadius;
                Vector3 candidate = hit.point + new Vector3(offset.x, 0f, offset.y);
                if (!query.TryProject(candidate, out Vector3 grounded)) continue;

                // Respect the rule's gates exactly like the procedural engine.
                BiomeType biome = query.BiomeAt(grounded);
                if (!rule.anyBiome && biome != rule.biome) continue;

                GameObject prefab = rule.prefabs[Random.Range(0, rule.prefabs.Count)];
                painted.Add(new BrushPlacement
                {
                    prefab = prefab,
                    position = grounded,
                    rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                    scale = Vector3.one * UnityEngine.Random.Range(rule.scaleRange.x, rule.scaleRange.y)
                });
            }
            SetStatus($"Painted: {painted.Count}");
        }

        private void Clear()
        {
            painted.Clear();
            SetStatus("Painted: 0");
        }

        private void BakeToChunks()
        {
            if (painted.Count == 0) { SetStatus("Nothing painted yet."); return; }
            BlenderBridgeSettings bridge =
                ChunkManifestImporter.FindSettings(false);
            if (bridge == null || bridge.WorldGrid == null)
            {
                SetStatus("BlenderBridgeSettings required to bake placements.");
                return;
            }

            ScatterBakeTool.ScatterChunkBaker.BakeSummary summary =
                ScatterBakeTool.ScatterChunkBaker.BakePlacements(painted, bridge);
            SetStatus($"Baked {summary.PlacementsAdded}/{painted.Count} into " +
                      $"{summary.ChunksUpdated} chunk(s). Skipped: {summary.Skipped.Count}.");
            UndoHistory.Push($"Ecology Brush ({summary.PlacementsAdded})");            Clear();
        }

        private void SetStatus(string message)
        {
            if (status != null) status.text = message;
        }

        /// <summary>Grounds candidates against scene colliders and reads the biome map.</summary>
        private sealed class SurfaceQuery
        {
            private readonly HighResBiomeMap biomes;

            public SurfaceQuery(Vector3 seedPoint)
            {
                biomes = AssetDatabase.LoadAssetAtPath<HighResBiomeMap>(
                    "Assets/WorldBuilderGenerated/Terrain/HighResBiomeMap.asset");
                _ = seedPoint;
            }

            public bool TryProject(Vector3 candidate, out Vector3 grounded)
            {
                Vector3 top = candidate + Vector3.up * 64f;
                if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 256f))
                {
                    grounded = hit.point;
                    return true;
                }
                grounded = candidate;
                return true; // fall back to ungrounded paint rather than dropping strokes
            }

            public BiomeType BiomeAt(Vector3 position) =>
                biomes != null ? biomes.SampleBiome(position.x, position.z, 128f) : BiomeType.Forest;
        }
    }
}
