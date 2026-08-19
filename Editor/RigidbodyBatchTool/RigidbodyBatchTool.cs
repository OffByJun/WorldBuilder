using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.RigidbodyBatchTool
{
    public sealed class RigidbodyBatchTool : IWorldBuilderTool
    {
        [SerializeField] private bool sceneWide = true;
        private RigidbodyBatchSettings settings = new RigidbodyBatchSettings
        {
            mass = 1f,
            constraints = RigidbodyConstraints.None
        };

        private Label resultLabel;

        public string ToolName => WorldBuilderLocalization.Get("tool.rigidbodyBatch");
        public string Category => WorldBuilderCategory.Physics;

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

            root.Add(InspectorHelp.Build(ToolName, "help.rigidbodyBatch"));

            Toggle scope = new Toggle("Scene Wide") { value = sceneWide };
            scope.RegisterValueChangedCallback(evt => sceneWide = evt.newValue);
            root.Add(scope);

            root.Add(BuildFloatRow("Mass", settings.mass, v => settings.mass = v, v => settings.applyMass = v));
            root.Add(BuildFloatRow("Linear Damping", settings.linearDamping, v => settings.linearDamping = v, v => settings.applyLinearDamping = v));
            root.Add(BuildFloatRow("Angular Damping", settings.angularDamping, v => settings.angularDamping = v, v => settings.applyAngularDamping = v));
            root.Add(BuildToggleRow("Use Gravity", settings.useGravity, v => settings.useGravity = v, v => settings.applyUseGravity = v));
            root.Add(BuildToggleRow("Is Kinematic", settings.isKinematic, v => settings.isKinematic = v, v => settings.applyIsKinematic = v));
            root.Add(BuildConstraintsRow());

            Button apply = new Button(Apply) { text = "Apply" };
            root.Add(apply);

            resultLabel = new Label(string.Empty);
            root.Add(resultLabel);

            return root;
        }

        private VisualElement BuildFloatRow(string label, float initial, System.Action<float> setter, System.Action<bool> toggleSetter)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Toggle apply = new Toggle { value = false };
            apply.RegisterValueChangedCallback(evt => toggleSetter(evt.newValue));
            row.Add(apply);

            FloatField field = new FloatField(label) { value = initial };
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
            row.Add(field);

            return row;
        }

        private VisualElement BuildToggleRow(string label, bool initial, System.Action<bool> setter, System.Action<bool> toggleSetter)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Toggle apply = new Toggle { value = false };
            apply.RegisterValueChangedCallback(evt => toggleSetter(evt.newValue));
            row.Add(apply);

            Toggle field = new Toggle(label) { value = initial };
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
            row.Add(field);

            return row;
        }

        private VisualElement BuildConstraintsRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Toggle apply = new Toggle { value = false };
            apply.RegisterValueChangedCallback(evt => settings.applyConstraints = evt.newValue);
            row.Add(apply);

            EnumFlagsField field = new EnumFlagsField("Constraints", settings.constraints);
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt => settings.constraints = (RigidbodyConstraints)evt.newValue);
            row.Add(field);

            return row;
        }

        private void Apply()
        {
            int count = RigidbodyBatchService.Apply(sceneWide, settings);
            if (resultLabel != null)
            {
                resultLabel.text = "Applied: " + count;
            }
        }
    }
}
