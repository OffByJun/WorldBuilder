using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.AudioVisualizerTool
{
    public sealed class AudioVisualizerTool : IWorldBuilderTool
    {
        [SerializeField] private bool showAll;
        [SerializeField] private Color color2D = Color.cyan;
        [SerializeField] private Color color3D = Color.yellow;
        [SerializeField] private float opacity = 0.4f;

        public string ToolName => WorldBuilderLocalization.Get("tool.audioVisualizer");
        public string Category => WorldBuilderCategory.Audio;

        public Texture2D ToolIcon => null;

        private readonly SceneComponentCache<AudioSource> cache = new SceneComponentCache<AudioSource>();

        public void OnEnable() => cache.Hook();

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.audioVisualizer"));

            Toggle show = new Toggle("Show All") { value = showAll };
            show.RegisterValueChangedCallback(evt =>
            {
                showAll = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(show);

            ColorField field2D = new ColorField("2D Color") { value = color2D };
            field2D.RegisterValueChangedCallback(evt =>
            {
                color2D = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(field2D);

            ColorField field3D = new ColorField("3D Color") { value = color3D };
            field3D.RegisterValueChangedCallback(evt =>
            {
                color3D = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(field3D);

            Slider opacityField = new Slider("Opacity", 0f, 1f) { value = opacity };
            opacityField.RegisterValueChangedCallback(evt =>
            {
                opacity = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(opacityField);

            return root;
        }

        public void OnSceneGUI()
        {
            if (!showAll || !WorldBuilderSceneGUI.IsRepaint)
            {
                return;
            }

            AudioSource[] sources = cache.Items;
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) continue;
                Draw(sources[i]);
            }
        }

        private void Draw(AudioSource source)
        {
            Vector3 position = source.transform.position;

            if (source.spatialBlend > 0f)
            {
                Handles.color = new Color(color3D.r, color3D.g, color3D.b, 1f);
                Handles.DrawWireDisc(position, Vector3.up, source.minDistance);

                Handles.color = new Color(color3D.r, color3D.g, color3D.b, opacity);
                Handles.DrawWireDisc(position, Vector3.up, source.maxDistance);
                return;
            }

            Handles.color = new Color(color2D.r, color2D.g, color2D.b, 1f);
            Handles.Label(position, "2D");
        }
    }
}
