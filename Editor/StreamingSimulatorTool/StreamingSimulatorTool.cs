using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Grid;
using WorldBuilder.Runtime.Streaming;

namespace WorldBuilder.Editor.StreamingSimulatorTool
{
    /// <summary>
    /// Previews RegionStreaming in the editor: drives ChunkStreamingService from the
    /// scene view camera so load/unload behaviour can be inspected before play.
    /// Instances are grouped under a __WB_StreamingPreview root (not undo-tracked,
    /// use Unload All or delete the root to clean up).
    /// </summary>
    public sealed class StreamingSimulatorTool : IWorldBuilderTool
    {
        [SerializeField] private WorldGridSettings gridSettings;
        [SerializeField] private int regionRadius = 1;
        [SerializeField] private bool followSceneCamera = true;

        private ChunkStreamingService service;
        private Transform previewRoot;
        private bool running;
        private Label status;
        private int inFlight;

        public string ToolName => WorldBuilderLocalization.Get("tool.streamingSim");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.streamingSim"));

            ObjectField gridField = new ObjectField("World Grid Settings")
            {
                objectType = typeof(WorldGridSettings),
                value = gridSettings
            };
            gridField.RegisterValueChangedCallback(evt => gridSettings = evt.newValue as WorldGridSettings);
            root.Add(gridField);

            SliderInt radius = new SliderInt("Region Radius", 0, 4) { value = regionRadius };
            radius.RegisterValueChangedCallback(evt =>
            {
                regionRadius = evt.newValue;
                if (running) FocusNow();
            });
            root.Add(radius);

            Toggle follow = new Toggle("Follow Scene Camera") { value = followSceneCamera };
            follow.RegisterValueChangedCallback(evt =>
            {
                followSceneCamera = evt.newValue;
                if (running && followSceneCamera) FocusNow();
            });
            root.Add(follow);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            Button start = new Button(StartPreview) { text = WorldBuilderLocalization.Get("btn.startPreview") };
            Button stop = new Button(StopPreview) { text = WorldBuilderLocalization.Get("btn.stopPreview") };
            Button focus = new Button(FocusNow) { text = WorldBuilderLocalization.Get("btn.refocus") };
            foreach (Button button in new[] { start, focus, stop })
            {
                button.style.flexGrow = 1;
                buttons.Add(button);
            }
            root.Add(buttons);

            status = new Label();
            status.style.marginTop = 6f;
            status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(status);

            root.schedule.Execute(RefreshStatus).Every(300);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!running || !followSceneCamera) return;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Vector3 pivot = view.pivot;
            FocusAt(pivot);
        }

        private void EnsureService()
        {
            if (service != null) return;

            string[] catalogGuids = AssetDatabase.FindAssets("t:DirectRegionCatalog");
            DirectRegionCatalog catalog = null;
            foreach (string guid in catalogGuids)
            {
                catalog = AssetDatabase.LoadAssetAtPath<DirectRegionCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null) break;
            }
            if (catalog == null)
            {
                Debug.LogWarning("[WorldBuilder] No DirectRegionCatalog asset found. " +
                                 "Run 'Tools > WorldBuilder > Chunks > Rebuild Region Catalog' first.");
                return;
            }

            previewRoot = GameObject.Find("__WB_StreamingPreview")?.transform;
            if (previewRoot == null) previewRoot = new GameObject("__WB_StreamingPreview").transform;

            service = new ChunkStreamingService(gridSettings, new DirectReferenceRegionLoader(catalog, gridSettings, previewRoot));
            running = true;
        }

        private void StartPreview()
        {
            if (gridSettings == null)
            {
                Debug.LogWarning("[WorldBuilder] Assign WorldGridSettings first.");
                return;
            }
            EnsureService();
            FocusNow();
        }

        private void FocusNow()
        {
            if (!running) return;
            SceneView view = SceneView.lastActiveSceneView;
            FocusAt(view != null ? view.pivot : Vector3.zero);
        }

        private async void FocusAt(Vector3 position)
        {
            if (service == null || inFlight > 0) return;
            inFlight++;
            try
            {
                await service.SetFocusAsync(position, regionRadius, CancellationToken.None);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[WorldBuilder] Streaming preview failed: {exception.Message}");
            }
            finally
            {
                inFlight--;
            }
        }

        private void StopPreview()
        {
            if (service == null) return;
            service.UnloadAllAsync(CancellationToken.None);
            running = false;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (status == null) return;
            if (service == null || !running)
            {
                status.text = WorldBuilderLocalization.Get("hint.streamingIdle");
                return;
            }
            status.text = $"{WorldBuilderLocalization.Get("hint.streamingActive")} regions={CountLoaded()}, radius={regionRadius}";
        }

        private static int CountLoaded()
        {
            GameObject root = GameObject.Find("__WB_StreamingPreview");
            return root != null ? root.transform.childCount : 0;
        }
    }
}
