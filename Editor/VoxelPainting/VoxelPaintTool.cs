using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.VoxelPainting
{
    public enum VoxelPaintMode
    {
        Add,
        Subtract,
        Smooth
    }

    public sealed class VoxelPaintTool : IWorldBuilderTool, IUndoable, IRaycastConsumer
    {
        [SerializeField] private float brushRadius = 2f;
        [SerializeField] private float brushStrength = 1f;
        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private VoxelPaintMode mode = VoxelPaintMode.Add;

        private readonly VoxelStoreAsset store;

        public VoxelPaintTool(VoxelStoreAsset store)
        {
            this.store = store;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.voxelPaint");
        public string Category => WorldBuilderCategory.AstraNope;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public void RecordUndo(string label)
        {
            if (store != null)
            {
                Undo.RecordObject(store, label);
                UndoHistory.Push(label);
            }
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.voxelPaint"));

            EnumField modeField = new EnumField("Mode", mode);
            modeField.RegisterValueChangedCallback(evt => mode = (VoxelPaintMode)evt.newValue);
            root.Add(modeField);

            Slider radius = new Slider("Brush Radius", 0.1f, 50f) { value = brushRadius };
            radius.RegisterValueChangedCallback(evt => brushRadius = evt.newValue);
            root.Add(radius);

            Slider strength = new Slider("Brush Strength", 0f, 10f) { value = brushStrength };
            strength.RegisterValueChangedCallback(evt => brushStrength = evt.newValue);
            root.Add(strength);

            FloatField size = new FloatField("Chunk Size") { value = chunkSize };
            size.RegisterValueChangedCallback(evt => chunkSize = evt.newValue);
            root.Add(size);

            if (store == null)
            {
                root.Add(new HelpBox("Voxel store is missing.", HelpBoxMessageType.Warning));
                return root;
            }

            Label resolution = new Label("Resolution: " + store.Resolution);
            root.Add(resolution);

            return root;
        }

        public void OnSceneGUI()
        {
            if (store == null)
            {
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            Handles.color = ModeColor();
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
            SceneView.RepaintAll();

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                Paint(hit.point);
                e.Use();
            }
        }

        private void Paint(Vector3 worldPoint)
        {
            int resolution = store.Resolution;
            if (resolution <= 0)
            {
                return;
            }

            Vector3Int coord = new Vector3Int(
                Mathf.FloorToInt(worldPoint.x / chunkSize),
                Mathf.FloorToInt(worldPoint.y / chunkSize),
                Mathf.FloorToInt(worldPoint.z / chunkSize));

            VoxelChunkEntry entry = store.GetOrCreate(coord);

            RecordUndo("Voxel Paint");

            Vector3 origin = new Vector3(coord.x * chunkSize, coord.y * chunkSize, coord.z * chunkSize);
            float spacing = chunkSize / resolution;
            float sqrRadius = brushRadius * brushRadius;
            float delta = brushStrength * Time.deltaTime;

            for (int x = 0; x < entry.sizeX; x++)
            {
                for (int y = 0; y < entry.sizeY; y++)
                {
                    for (int z = 0; z < entry.sizeZ; z++)
                    {
                        Vector3 voxelWorld = origin + new Vector3(x * spacing, y * spacing, z * spacing);
                        if ((voxelWorld - worldPoint).sqrMagnitude > sqrRadius)
                        {
                            continue;
                        }

                        float current = store.GetDensity(entry, x, y, z);
                        float result = Apply(entry, x, y, z, current, delta);
                        store.SetDensity(entry, x, y, z, Mathf.Clamp01(result));
                    }
                }
            }

            EditorUtility.SetDirty(store);
        }

        private float Apply(VoxelChunkEntry entry, int x, int y, int z, float current, float delta)
        {
            switch (mode)
            {
                case VoxelPaintMode.Add:
                    return current + delta;
                case VoxelPaintMode.Subtract:
                    return current - delta;
                default:
                    return AverageNeighbors(entry, x, y, z);
            }
        }

        private float AverageNeighbors(VoxelChunkEntry entry, int x, int y, int z)
        {
            float sum = 0f;
            int count = 0;

            Accumulate(entry, x - 1, y, z, ref sum, ref count);
            Accumulate(entry, x + 1, y, z, ref sum, ref count);
            Accumulate(entry, x, y - 1, z, ref sum, ref count);
            Accumulate(entry, x, y + 1, z, ref sum, ref count);
            Accumulate(entry, x, y, z - 1, ref sum, ref count);
            Accumulate(entry, x, y, z + 1, ref sum, ref count);

            if (count == 0)
            {
                return store.GetDensity(entry, x, y, z);
            }

            return sum / count;
        }

        private void Accumulate(VoxelChunkEntry entry, int x, int y, int z, ref float sum, ref int count)
        {
            if (x < 0 || x >= entry.sizeX || y < 0 || y >= entry.sizeY || z < 0 || z >= entry.sizeZ)
            {
                return;
            }

            sum += store.GetDensity(entry, x, y, z);
            count++;
        }

        private Color ModeColor()
        {
            switch (mode)
            {
                case VoxelPaintMode.Add:
                    return Color.green;
                case VoxelPaintMode.Subtract:
                    return Color.red;
                default:
                    return Color.cyan;
            }
        }
    }
}
