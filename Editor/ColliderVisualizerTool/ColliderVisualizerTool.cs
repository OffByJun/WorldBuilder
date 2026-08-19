using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.ColliderVisualizerTool
{
    public sealed class ColliderVisualizerTool : IWorldBuilderTool
    {
        [SerializeField] private bool showAll;
        [SerializeField] private Color boxColor = Color.green;
        [SerializeField] private Color sphereColor = Color.cyan;
        [SerializeField] private Color capsuleColor = Color.yellow;
        [SerializeField] private Color meshColor = Color.magenta;
        [SerializeField] private float opacity = 0.8f;

        public string ToolName => WorldBuilderLocalization.Get("tool.colliderVisualizer");
        public string Category => WorldBuilderCategory.Debug;

        public Texture2D ToolIcon => null;

        private readonly SceneComponentCache<Collider> cache = new SceneComponentCache<Collider>();

        public void OnEnable() => cache.Hook();

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.colliderVisualizer"));

            Toggle show = new Toggle("Show All") { value = showAll };
            show.RegisterValueChangedCallback(evt =>
            {
                showAll = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(show);

            root.Add(ColorPicker("Box", boxColor, value => boxColor = value));
            root.Add(ColorPicker("Sphere", sphereColor, value => sphereColor = value));
            root.Add(ColorPicker("Capsule", capsuleColor, value => capsuleColor = value));
            root.Add(ColorPicker("Mesh", meshColor, value => meshColor = value));

            Slider opacityField = new Slider("Opacity", 0f, 1f) { value = opacity };
            opacityField.RegisterValueChangedCallback(evt =>
            {
                opacity = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(opacityField);

            return root;
        }

        private ColorField ColorPicker(string label, Color initial, System.Action<Color> setter)
        {
            ColorField field = new ColorField(label) { value = initial };
            field.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                SceneView.RepaintAll();
            });
            return field;
        }

        public void OnSceneGUI()
        {
            if (!showAll || !WorldBuilderSceneGUI.IsRepaint)
            {
                return;
            }

            Collider[] colliders = cache.Items;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                Draw(colliders[i]);
            }
        }

        private void Draw(Collider collider)
        {
            Transform t = collider.transform;

            if (collider is BoxCollider box)
            {
                Handles.color = WithOpacity(boxColor);
                Handles.matrix = Matrix4x4.TRS(t.position, t.rotation, t.lossyScale);
                Handles.DrawWireCube(box.center, box.size);
                Handles.matrix = Matrix4x4.identity;
                return;
            }

            if (collider is SphereCollider sphere)
            {
                Handles.color = WithOpacity(sphereColor);
                Vector3 center = t.TransformPoint(sphere.center);
                float radius = sphere.radius * MaxScale(t.lossyScale);
                Handles.DrawWireDisc(center, Vector3.up, radius);
                Handles.DrawWireDisc(center, Vector3.right, radius);
                Handles.DrawWireDisc(center, Vector3.forward, radius);
                return;
            }

            if (collider is CapsuleCollider capsule)
            {
                Handles.color = WithOpacity(capsuleColor);
                Vector3 center = t.TransformPoint(capsule.center);
                float radius = capsule.radius * MaxScale(t.lossyScale);
                Handles.DrawWireDisc(center, Vector3.up, radius);
                Handles.DrawWireDisc(center, t.right, radius);
                Handles.DrawWireDisc(center, t.forward, radius);
                return;
            }

            Handles.color = WithOpacity(meshColor);
            Bounds bounds = collider.bounds;
            Handles.DrawWireCube(bounds.center, bounds.size);
        }

        private Color WithOpacity(Color color)
        {
            return new Color(color.r, color.g, color.b, opacity);
        }

        private float MaxScale(Vector3 scale)
        {
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }
}
