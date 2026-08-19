using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.TerrainSculptTool
{
    public enum TerrainSculptMode
    {
        Add,
        Subtract,
        Smooth
    }

    public sealed class TerrainSculptTool : IWorldBuilderTool
    {
        [SerializeField] private float brushRadius = 2f;
        [SerializeField] private float brushStrength = 1f;
        [SerializeField] private float chunkSize = 16f;
        [SerializeField] private TerrainSculptMode mode = TerrainSculptMode.Add;

        private readonly IVoxelStore store;

        public TerrainSculptTool(IVoxelStore store)
        {
            this.store = store;
        }

        public string ToolName => WorldBuilderLocalization.Get("tool.terrainSculpt");
        public string Category => WorldBuilderCategory.World;

        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.terrainSculpt"));

            EnumField modeField = new EnumField("Mode", mode);
            modeField.RegisterValueChangedCallback(evt => mode = (TerrainSculptMode)evt.newValue);
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
            }

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

            if (!SceneRaycaster.TryRaycast(Event.current.mousePosition, out RaycastHit hit))
            {
                return;
            }

            Handles.color = ModeColor();
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
            SceneView.RepaintAll();

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                Sculpt(hit.point);
                e.Use();
            }
        }

        private void Sculpt(Vector3 worldPoint)
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

            if (!store.TryGetVoxelData(coord, out VoxelData data))
            {
                data = new VoxelData(resolution, resolution, resolution);
            }

            if (store is Object storeObject)
            {
                Undo.RecordObject(storeObject, "Terrain Sculpt");
            }

            Vector3 origin = new Vector3(coord.x * chunkSize, coord.y * chunkSize, coord.z * chunkSize);
            float spacing = chunkSize / resolution;
            float sqrRadius = brushRadius * brushRadius;
            float delta = brushStrength * Time.deltaTime;

            for (int x = 0; x < data.sizeX; x++)
            {
                for (int y = 0; y < data.sizeY; y++)
                {
                    for (int z = 0; z < data.sizeZ; z++)
                    {
                        Vector3 voxelWorld = origin + new Vector3(x * spacing, y * spacing, z * spacing);
                        if ((voxelWorld - worldPoint).sqrMagnitude > sqrRadius)
                        {
                            continue;
                        }

                        float result = Apply(data, x, y, z, delta);
                        data.SetDensity(x, y, z, Mathf.Clamp01(result));
                    }
                }
            }

            store.SetVoxelData(coord, data);

            if (store is Object dirtyObject)
            {
                EditorUtility.SetDirty(dirtyObject);
            }
        }

        private float Apply(VoxelData data, int x, int y, int z, float delta)
        {
            float current = data.GetDensity(x, y, z);
            switch (mode)
            {
                case TerrainSculptMode.Add:
                    return current + delta;
                case TerrainSculptMode.Subtract:
                    return current - delta;
                default:
                    return AverageNeighbors(data, x, y, z);
            }
        }

        private float AverageNeighbors(VoxelData data, int x, int y, int z)
        {
            float sum = 0f;
            int count = 0;

            Accumulate(data, x - 1, y, z, ref sum, ref count);
            Accumulate(data, x + 1, y, z, ref sum, ref count);
            Accumulate(data, x, y - 1, z, ref sum, ref count);
            Accumulate(data, x, y + 1, z, ref sum, ref count);
            Accumulate(data, x, y, z - 1, ref sum, ref count);
            Accumulate(data, x, y, z + 1, ref sum, ref count);

            if (count == 0)
            {
                return data.GetDensity(x, y, z);
            }

            return sum / count;
        }

        private void Accumulate(VoxelData data, int x, int y, int z, ref float sum, ref int count)
        {
            if (x < 0 || x >= data.sizeX || y < 0 || y >= data.sizeY || z < 0 || z >= data.sizeZ)
            {
                return;
            }

            sum += data.GetDensity(x, y, z);
            count++;
        }

        private Color ModeColor()
        {
            switch (mode)
            {
                case TerrainSculptMode.Add:
                    return Color.green;
                case TerrainSculptMode.Subtract:
                    return Color.red;
                default:
                    return Color.cyan;
            }
        }
    }
}
