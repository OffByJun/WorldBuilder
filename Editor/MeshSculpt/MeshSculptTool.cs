using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.PrefabBrush;

namespace WorldBuilder.Editor.MeshSculpt
{
    /// <summary>
    /// Blender-style sculpting on placed map meshes: raise, dig, smooth, flatten, inflate and punch
    /// holes, with the mesh subdivided under the brush so small strokes have vertices to move.
    /// The first stroke on a mesh swaps in an editable copy saved under Assets/008_WorldBuilder/Sculpted,
    /// so the source model is never touched; every stroke can be undone.
    /// </summary>
    public sealed class MeshSculptTool : IWorldBuilderTool
    {
        internal const string SculptFolder = "Assets/008_WorldBuilder/Sculpted";
        private const int ColliderRefreshEvery = 6;

        private static readonly Dictionary<string, (string ko, string en)> Text = new Dictionary<string, (string, string)>
        {
            { "brush.Raise", ("올리기", "Raise") },
            { "brush.Dig", ("파기", "Dig") },
            { "brush.Smooth", ("부드럽게", "Smooth") },
            { "brush.Flatten", ("평평하게", "Flatten") },
            { "brush.Inflate", ("부풀리기", "Inflate") },
            { "brush.Hole", ("구멍 뚫기", "Punch") },
            { "hint.Raise", ("지형을 솟게 합니다. Ctrl을 누르면 반대로 내려갑니다.", "Builds terrain up. Hold Ctrl to lower instead.") },
            { "hint.Dig", ("가파르게 파내 웅덩이·구덩이를 만듭니다. Ctrl을 누르면 반대로 쌓입니다.", "Carves steep pits and trenches. Hold Ctrl to pile up instead.") },
            { "hint.Smooth", ("울퉁불퉁한 곳을 고르게 풉니다. 다른 브러시에서도 Shift를 누르면 이 브러시가 됩니다.", "Relaxes bumps. Hold Shift with any brush to smooth.") },
            { "hint.Flatten", ("처음 누른 지점의 높이로 평평하게 맞춥니다.", "Levels the surface to the height where the stroke began.") },
            { "hint.Inflate", ("표면을 바깥쪽으로 부풀립니다(바위·언덕 볼륨). Ctrl을 누르면 오므립니다.", "Swells the surface outward along its normals. Hold Ctrl to shrink.") },
            { "hint.Hole", ("반경 안의 면을 지워 실제로 뚫린 구멍을 냅니다. 동굴 입구 같은 곳에 씁니다.", "Deletes faces inside the radius to open a real hole, e.g. a cave mouth.") },
            { "card.brush", ("브러시", "Brush") },
            { "radius", ("반경 (m)", "Radius (m)") },
            { "strength", ("세기", "Strength") },
            { "hardness", ("단단함", "Hardness") },
            { "hardness.tip", ("높을수록 가장자리까지 세게, 낮을수록 부드럽게 퍼집니다.", "Higher keeps full strength closer to the edge.") },
            { "card.detail", ("디테일", "Detail") },
            { "autoDetail", ("브러시 아래를 잘게 나누기", "Subdivide Under Brush") },
            { "autoDetail.tip", ("원본 메시는 정점이 듬성듬성해서, 브러시 아래 긴 모서리를 나눠 조각할 정점을 만듭니다(Blender의 Dyntopo).", "The source mesh is sparse, so long edges under the brush are split to give the stroke vertices to move (like Blender Dyntopo).") },
            { "detail", ("디테일 크기 (m)", "Detail Size (m)") },
            { "worldUp", ("지형처럼 위아래로", "Terrain Mode (World Up)") },
            { "worldUp.tip", ("켜면 올리기·파기·평평하게가 항상 위아래 방향으로 작동합니다. 끄면 표면 방향을 따릅니다.", "When on, raise/dig/flatten work straight up and down; off, they follow the surface.") },
            { "card.targets", ("편집 중인 메시", "Sculpted Meshes") },
            { "targets.none", ("아직 없습니다. 맵을 클릭하면 그 메시의 편집용 복사본이 만들어집니다(원본 모델은 그대로).", "None yet. The first stroke on a mesh makes an editable copy; the source model stays untouched.") },
            { "targets.meta", ("{0}개 · 정점 {1:N0}", "{0} · {1:N0} verts") },
            { "revert", ("선택한 오브젝트를 원본 메시로", "Revert Selection To Source") },
            { "revert.tip", ("선택한 오브젝트의 메시·콜라이더를 프리팹 원본으로 되돌립니다. 조각한 복사본 파일은 남습니다.", "Reverts the selected objects' mesh and collider to the prefab source. The sculpted copy asset stays.") },
            { "shortcuts", ("Ctrl 반대로 · Shift 부드럽게 · [ ] 반경 · Alt 카메라", "Ctrl invert · Shift smooth · [ ] radius · Alt camera") },
            { "scope", ("클릭한 오브젝트와 같은 루트(예: Map) 아래의 메시만 수정합니다.", "Only meshes under the same root as the clicked object (e.g. Map) are changed.") },
            { "brush.Grab", ("잡아 끌기", "Grab") },
            { "brush.SnakeHook", ("늘려 뽑기", "Snake Hook") },
            { "brush.Pinch", ("모으기", "Pinch") },
            { "brush.Crease", ("능선 긋기", "Crease") },
            { "brush.Noise", ("거칠게", "Noise") },
            { "brush.Terrace", ("계단", "Terrace") },
            { "hint.Grab", ("누른 자리를 잡고 끄는 대로 옮깁니다. 화면과 나란한 면을 따라 움직여서, 옆에서 보고 끌면 솟고 위에서 보고 끌면 옆으로 밀립니다.", "Grabs the area under the cursor and moves it with the mouse, in the plane facing the camera.") },
            { "hint.SnakeHook", ("끄는 대로 지형을 계속 늘려 뽑습니다. 봉우리·기둥·튀어나온 바위를 길게 끌어낼 때 씁니다. 브러시 아래는 계속 잘게 나뉩니다.", "Keeps pulling the surface along the stroke to draw out spires, pillars and overhangs.") },
            { "hint.Pinch", ("브러시 가운데로 모아 능선과 골을 날카롭게 합니다. Ctrl을 누르면 벌립니다.", "Pulls toward the brush centre to sharpen ridges and creases. Hold Ctrl to spread.") },
            { "hint.Crease", ("모으면서 올려 날카로운 능선을 긋습니다. Ctrl을 누르면 골짜기를 팝니다.", "Pinches and lifts to draw a sharp ridge. Hold Ctrl to cut a valley.") },
            { "hint.Noise", ("울퉁불퉁한 결을 더합니다. 결 크기로 자갈·바위·언덕 느낌을 고릅니다.", "Adds rough, rocky detail. Noise size picks pebbles, rocks or hills.") },
            { "hint.Terrace", ("계단 높이마다 평평한 단과 가파른 벽을 만듭니다(층진 절벽·계단식 비탈).", "Steps the surface into flat ledges and steep risers.") },
            { "card.options", ("브러시 모양", "Brush Shape") },
            { "falloff", ("가장자리", "Falloff") },
            { "falloff.Smooth", ("부드럽게", "Smooth") },
            { "falloff.Sphere", ("둥글게", "Sphere") },
            { "falloff.Sharp", ("뾰족하게", "Sharp") },
            { "falloff.Linear", ("곧게", "Linear") },
            { "falloff.Constant", ("일정하게", "Constant") },
            { "noiseScale", ("결 크기 (m)", "Noise Size (m)") },
            { "terraceStep", ("계단 높이 (m)", "Step Height (m)") },
            { "card.symmetry", ("대칭", "Symmetry") },
            { "mirrorX", ("X 대칭", "Mirror X") },
            { "mirrorZ", ("Z 대칭", "Mirror Z") },
            { "pivot", ("대칭 중심", "Pivot") },
            { "pivot.selection", ("선택한 오브젝트 위치로", "Use Selection") },
            { "symmetry.tip", ("켜면 대칭 중심을 지나는 면의 반대쪽도 같이 조각합니다.", "Also sculpts the mirrored side across the pivot.") },
            { "card.create", ("새 지형 만들기", "New Terrain") },
            { "create.shape", ("모양", "Shape") },
            { "shape.Flat", ("평지", "Flat") },
            { "shape.Hill", ("언덕", "Hill") },
            { "shape.Island", ("섬", "Island") },
            { "shape.Basin", ("분지", "Basin") },
            { "shape.Ridges", ("산맥", "Ridges") },
            { "create.size", ("크기 (m)", "Size (m)") },
            { "create.spacing", ("정점 간격 (m)", "Vertex Spacing (m)") },
            { "create.height", ("높이 (m)", "Height (m)") },
            { "create.seed", ("모양 번호", "Seed") },
            { "create.material", ("재질", "Material") },
            { "create.parent", ("넣을 곳", "Parent") },
            { "create.parent.tip", ("조각은 클릭한 오브젝트와 같은 루트 아래만 고칩니다. 맵과 이어서 다듬으려면 맵 루트를 고르세요. 비우면 장면 맨 위에 만듭니다.", "Strokes only change meshes under the clicked root; pick the map root to sculpt across both.") },
            { "create.parentSelection", ("선택한 것의 루트로", "Use Selection Root") },
            { "create.button", ("씬 뷰 가운데에 만들기", "Create At Scene View Centre") },
            { "create.hint", ("만든 지형은 바로 조각할 수 있습니다. 가장자리는 높이 0으로 내려앉아 주변 바닥과 잇기 쉽습니다. 재질을 비우면 넣을 곳 아래 메시의 재질을 씁니다.", "The new patch is ready to sculpt; its rim settles to height 0 so it blends into the ground.") }
        };

        private static readonly SculptBrush[] Brushes =
        {
            SculptBrush.Raise, SculptBrush.Dig, SculptBrush.Smooth,
            SculptBrush.Flatten, SculptBrush.Inflate, SculptBrush.Hole,
            SculptBrush.Grab, SculptBrush.SnakeHook, SculptBrush.Pinch,
            SculptBrush.Crease, SculptBrush.Noise, SculptBrush.Terrace
        };

        private static readonly SculptFalloff[] Falloffs =
        {
            SculptFalloff.Smooth, SculptFalloff.Sphere, SculptFalloff.Sharp, SculptFalloff.Linear, SculptFalloff.Constant
        };

        private static readonly SculptTerrainShape[] Shapes =
        {
            SculptTerrainShape.Flat, SculptTerrainShape.Hill, SculptTerrainShape.Island, SculptTerrainShape.Basin, SculptTerrainShape.Ridges
        };

        private readonly Dictionary<MeshFilter, SculptMesh> meshes = new Dictionary<MeshFilter, SculptMesh>();
        private readonly HashSet<SculptMesh> strokeTouched = new HashSet<SculptMesh>();

        private SculptBrush brush = SculptBrush.Raise;
        private float radius = 4f;
        private float strength = 0.5f;
        private float hardness = 0.3f;
        private bool autoDetail = true;
        private float detail = 0.8f;
        private bool worldUp = true;
        private SculptFalloff falloff = SculptFalloff.Smooth;
        private float noiseScale = 3f;
        private float terraceStep = 2f;
        private bool mirrorX;
        private bool mirrorZ;
        private Vector3 mirrorPivot;

        private SculptTerrainShape newShape = SculptTerrainShape.Island;
        private float newSize = 80f;
        private float newSpacing = 1f;
        private float newHeight = 12f;
        private int newSeed;
        private Material newMaterial;
        private Transform newParent;

        private bool stroking;
        private SculptBrush strokeBrush;
        private Plane dragPlane;
        private Vector3 dragOrigin;
        private Vector3 lastDragPoint;
        private readonly List<(SculptMesh mesh, int handle, int mirror)> grabbed = new List<(SculptMesh, int, int)>();
        private int undoGroup;
        private int dabsSinceCollider;
        private Vector3 lastDab;
        private Vector3 planePoint;
        private Vector3 planeNormal;
        private MeshFilter[] strokeCandidates;

        private Slider radiusSlider;
        private Label hintLabel;
        private Label targetsMeta;
        private readonly List<Button> brushButtons = new List<Button>();

        public string ToolName => WorldBuilderLocalization.Get("tool.meshSculpt");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        private static string T(string key)
        {
            if (!Text.TryGetValue(key, out (string ko, string en) value)) return key;
            return WorldBuilderLocalization.Current == WorldBuilderLocalization.Language.Korean ? value.ko : value.en;
        }

        public void OnEnable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        // ------------------------------------------------------------------ inspector

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            PrefabBrushUI.ApplyStyle(root);
            root.Add(InspectorHelp.Build(ToolName, "help.meshSculpt"));

            brushButtons.Clear();
            for (int row = 0; row < Brushes.Length / 3; row++)
            {
                VisualElement modes = new VisualElement();
                modes.AddToClassList("pb-mode");
                for (int c = 0; c < 3; c++)
                {
                    SculptBrush b = Brushes[row * 3 + c];
                    Button button = new Button(() => SelectBrush(b)) { text = T("brush." + b) };
                    button.AddToClassList("pb-mode__button");
                    if (b == SculptBrush.Hole) button.AddToClassList("pb-mode__button--erase");
                    button.userData = b;
                    brushButtons.Add(button);
                    modes.Add(button);
                }

                root.Add(modes);
            }

            hintLabel = PrefabBrushUI.Hint(string.Empty);
            root.Add(hintLabel);
            root.Add(PrefabBrushUI.Hint(T("shortcuts")));

            VisualElement brushCard = PrefabBrushUI.Card(T("card.brush"));
            radiusSlider = new Slider(T("radius"), 0.3f, 40f) { value = radius, showInputField = true };
            radiusSlider.RegisterValueChangedCallback(evt => radius = evt.newValue);
            brushCard.Add(radiusSlider);
            Slider strengthSlider = new Slider(T("strength"), 0.02f, 1f) { value = strength, showInputField = true };
            strengthSlider.RegisterValueChangedCallback(evt => strength = evt.newValue);
            brushCard.Add(strengthSlider);
            Slider hardnessSlider = new Slider(T("hardness"), 0f, 1f) { value = hardness, showInputField = true, tooltip = T("hardness.tip") };
            hardnessSlider.RegisterValueChangedCallback(evt => hardness = evt.newValue);
            brushCard.Add(hardnessSlider);
            Toggle upToggle = new Toggle(T("worldUp")) { value = worldUp, tooltip = T("worldUp.tip") };
            upToggle.RegisterValueChangedCallback(evt => worldUp = evt.newValue);
            brushCard.Add(upToggle);
            root.Add(brushCard);
            root.Add(BuildOptionsCard());
            root.Add(BuildSymmetryCard());

            VisualElement detailCard = PrefabBrushUI.Card(T("card.detail"));
            Slider detailSlider = new Slider(T("detail"), 0.15f, 4f) { value = detail, showInputField = true };
            Toggle detailToggle = new Toggle(T("autoDetail")) { value = autoDetail, tooltip = T("autoDetail.tip") };
            detailToggle.RegisterValueChangedCallback(evt =>
            {
                autoDetail = evt.newValue;
                detailSlider.SetEnabled(evt.newValue);
            });
            detailSlider.RegisterValueChangedCallback(evt => detail = evt.newValue);
            detailSlider.SetEnabled(autoDetail);
            detailCard.Add(detailToggle);
            detailCard.Add(detailSlider);
            root.Add(detailCard);

            VisualElement targetsCard = PrefabBrushUI.Card(T("card.targets"), out targetsMeta);
            Label none = PrefabBrushUI.Hint(T("targets.none"));
            targetsCard.Add(none);
            targetsCard.Add(PrefabBrushUI.Hint(T("scope")));
            Button revert = PrefabBrushUI.Button(T("revert"), RevertSelection, "danger");
            revert.tooltip = T("revert.tip");
            revert.style.marginRight = 0f;
            targetsCard.Add(PrefabBrushUI.Row(revert));
            targetsCard.schedule.Execute(() =>
            {
                int count = 0, verts = 0;
                foreach (SculptMesh mesh in meshes.Values)
                {
                    if (mesh.Filter == null) continue;
                    count++;
                    verts += mesh.VertexCount;
                }

                targetsMeta.text = string.Format(T("targets.meta"), count, verts);
                none.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(500);
            root.Add(targetsCard);
            root.Add(BuildCreateCard());

            SelectBrush(brush);
            return root;
        }

        private Slider noiseSlider;
        private Slider terraceSlider;

        private VisualElement BuildOptionsCard()
        {
            VisualElement card = PrefabBrushUI.Card(T("card.options"));
            var falloffField = new DropdownField(T("falloff"), Falloffs.Select(f => T("falloff." + f)).ToList(), System.Array.IndexOf(Falloffs, falloff));
            falloffField.RegisterValueChangedCallback(_ => falloff = Falloffs[Mathf.Max(0, falloffField.index)]);
            card.Add(falloffField);
            noiseSlider = new Slider(T("noiseScale"), 0.3f, 30f) { value = noiseScale, showInputField = true };
            noiseSlider.RegisterValueChangedCallback(evt => noiseScale = evt.newValue);
            card.Add(noiseSlider);
            terraceSlider = new Slider(T("terraceStep"), 0.2f, 20f) { value = terraceStep, showInputField = true };
            terraceSlider.RegisterValueChangedCallback(evt => terraceStep = evt.newValue);
            card.Add(terraceSlider);
            return card;
        }

        private VisualElement BuildSymmetryCard()
        {
            VisualElement card = PrefabBrushUI.Card(T("card.symmetry"));
            card.Add(PrefabBrushUI.Hint(T("symmetry.tip")));
            var x = new Toggle(T("mirrorX")) { value = mirrorX };
            x.RegisterValueChangedCallback(evt => mirrorX = evt.newValue);
            var z = new Toggle(T("mirrorZ")) { value = mirrorZ };
            z.RegisterValueChangedCallback(evt => mirrorZ = evt.newValue);
            card.Add(x);
            card.Add(z);
            var pivot = new Vector3Field(T("pivot")) { value = mirrorPivot };
            pivot.RegisterValueChangedCallback(evt => mirrorPivot = evt.newValue);
            card.Add(pivot);
            card.Add(PrefabBrushUI.Row(PrefabBrushUI.Button(T("pivot.selection"), () =>
            {
                if (Selection.activeTransform) pivot.value = Selection.activeTransform.position;
            })));
            return card;
        }

        private VisualElement BuildCreateCard()
        {
            VisualElement card = PrefabBrushUI.Card(T("card.create"));
            card.Add(PrefabBrushUI.Hint(T("create.hint")));
            var shape = new DropdownField(T("create.shape"), Shapes.Select(s => T("shape." + s)).ToList(), System.Array.IndexOf(Shapes, newShape));
            shape.RegisterValueChangedCallback(_ => newShape = Shapes[Mathf.Max(0, shape.index)]);
            card.Add(shape);
            var size = new Slider(T("create.size"), 10f, 400f) { value = newSize, showInputField = true };
            size.RegisterValueChangedCallback(evt => newSize = evt.newValue);
            card.Add(size);
            var spacing = new Slider(T("create.spacing"), 0.25f, 8f) { value = newSpacing, showInputField = true };
            spacing.RegisterValueChangedCallback(evt => newSpacing = evt.newValue);
            card.Add(spacing);
            var height = new Slider(T("create.height"), 0f, 120f) { value = newHeight, showInputField = true };
            height.RegisterValueChangedCallback(evt => newHeight = evt.newValue);
            card.Add(height);
            var seed = new IntegerField(T("create.seed")) { value = newSeed };
            seed.RegisterValueChangedCallback(evt => newSeed = evt.newValue);
            card.Add(seed);
            var material = new ObjectField(T("create.material")) { objectType = typeof(Material), allowSceneObjects = false, value = newMaterial };
            material.RegisterValueChangedCallback(evt => newMaterial = evt.newValue as Material);
            card.Add(material);
            if (!newParent && Selection.activeTransform) newParent = Selection.activeTransform.root;
            var parent = new ObjectField(T("create.parent")) { objectType = typeof(Transform), allowSceneObjects = true, value = newParent, tooltip = T("create.parent.tip") };
            parent.RegisterValueChangedCallback(evt => newParent = evt.newValue as Transform);
            card.Add(parent);
            card.Add(PrefabBrushUI.Row(
                PrefabBrushUI.Button(T("create.parentSelection"), () =>
                {
                    if (Selection.activeTransform) parent.value = Selection.activeTransform.root;
                }),
                PrefabBrushUI.Button(T("create.button"), CreateTerrain, "primary")));
            return card;
        }

        private void CreateTerrain()
        {
            SceneView view = SceneView.lastActiveSceneView;
            GameObject created = SculptTerrainFactory.Create(new SculptTerrainSettings
            {
                Size = newSize,
                Spacing = newSpacing,
                Shape = newShape,
                Height = newHeight,
                Seed = newSeed,
                Material = newMaterial,
                Parent = newParent,
                Center = view ? view.pivot : Vector3.zero
            });
            Selection.activeGameObject = created;
            UndoHistory.Push("Create Sculpt Terrain");
        }

        private void SelectBrush(SculptBrush value)
        {
            brush = value;
            foreach (Button button in brushButtons)
                button.EnableInClassList("pb-mode__button--active", (SculptBrush)button.userData == value);
            if (hintLabel != null) hintLabel.text = T("hint." + value);
            noiseSlider?.SetEnabled(value == SculptBrush.Noise);
            terraceSlider?.SetEnabled(value == SculptBrush.Terrace);
        }

        // ------------------------------------------------------------------ scene

        public void OnSceneGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket))
            {
                radius = Mathf.Clamp(radius * (e.keyCode == KeyCode.LeftBracket ? 0.85f : 1.18f), 0.3f, 40f);
                radiusSlider?.SetValueWithoutNotify(radius);
                e.Use();
            }

            // 잡아 끌기·늘려 뽑기는 끄는 동안 메시가 커서 밑에서 움직이므로, 메시 대신 화면과 나란한 면에 커서를 비춘다.
            if (stroking && IsDragBrush(strokeBrush))
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    if (TryDragPoint(e.mousePosition, out Vector3 point)) Drag(point);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    EndStroke();
                    e.Use();
                }

                DrawDragCursor();
                SceneView.RepaintAll();
                return;
            }

            if (!SceneRaycaster.TryRaycast(e.mousePosition, out RaycastHit hit))
            {
                if (e.type == EventType.MouseUp && stroking) EndStroke();
                return;
            }

            SculptBrush active = e.shift && brush != SculptBrush.Hole ? SculptBrush.Smooth : brush;
            DrawCursor(hit, active, e.control);
            SceneView.RepaintAll();

            if (e.alt) return;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                BeginStroke(hit, active);
                if (IsDragBrush(active)) BeginDrag(hit);
                else Dab(hit, active, e.control);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && stroking)
            {
                if (Vector3.Distance(lastDab, hit.point) >= radius * 0.18f) Dab(hit, active, e.control);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && stroking)
            {
                EndStroke();
                e.Use();
            }
        }

        private static bool IsDragBrush(SculptBrush value) => value == SculptBrush.Grab || value == SculptBrush.SnakeHook;

        private void DrawCursor(RaycastHit hit, SculptBrush active, bool invert)
        {
            Color color = active switch
            {
                SculptBrush.Raise => invert ? new Color(1f, 0.55f, 0.3f) : new Color(0.35f, 0.85f, 1f),
                SculptBrush.Dig => invert ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.55f, 0.3f),
                SculptBrush.Smooth => new Color(0.6f, 1f, 0.6f),
                SculptBrush.Flatten => new Color(1f, 0.9f, 0.4f),
                SculptBrush.Inflate => new Color(0.85f, 0.6f, 1f),
                SculptBrush.Grab or SculptBrush.SnakeHook => new Color(1f, 1f, 1f),
                SculptBrush.Pinch or SculptBrush.Crease => new Color(1f, 0.7f, 0.85f),
                SculptBrush.Noise => new Color(0.75f, 0.65f, 0.5f),
                SculptBrush.Terrace => new Color(0.55f, 0.8f, 0.55f),
                _ => new Color(1f, 0.3f, 0.3f)
            };
            for (int m = 0; m < 4; m++)
            {
                if (!UsesMirror(m)) continue;
                Vector3 point = MirrorPoint(hit.point, m), normal = MirrorVector(hit.normal, m);
                float alpha = m == 0 ? 1f : 0.45f;
                Handles.color = new Color(color.r, color.g, color.b, alpha);
                Handles.DrawWireDisc(point, normal, radius, 2f);
                Handles.color = new Color(color.r, color.g, color.b, 0.45f * alpha);
                Handles.DrawWireDisc(point, normal, Mathf.Max(0.05f, radius * hardness));
            }

            Vector3 dir = worldUp ? Vector3.up : hit.normal;
            if (active == SculptBrush.Raise || active == SculptBrush.Dig || active == SculptBrush.Crease)
            {
                float sign = (active == SculptBrush.Dig) == invert ? 1f : -1f;
                Handles.color = color;
                Handles.DrawLine(hit.point, hit.point + dir * (radius * 0.35f * sign), 2f);
            }
        }

        private void DrawDragCursor()
        {
            Handles.color = Color.white;
            Handles.DrawWireDisc(lastDragPoint, dragPlane.normal, radius, 2f);
            if (strokeBrush == SculptBrush.Grab) Handles.DrawDottedLine(dragOrigin, lastDragPoint, 4f);
        }

        // ------------------------------------------------------------------ symmetry

        // 거울 번호: 0 원래, 1 X, 2 Z, 3 X와 Z.
        private bool UsesMirror(int mirror) =>
            mirror == 0 || (mirror == 1 && mirrorX) || (mirror == 2 && mirrorZ) || (mirror == 3 && mirrorX && mirrorZ);

        private Vector3 MirrorPoint(Vector3 point, int mirror)
        {
            if ((mirror & 1) != 0) point.x = 2f * mirrorPivot.x - point.x;
            if ((mirror & 2) != 0) point.z = 2f * mirrorPivot.z - point.z;
            return point;
        }

        private static Vector3 MirrorVector(Vector3 vector, int mirror)
        {
            if ((mirror & 1) != 0) vector.x = -vector.x;
            if ((mirror & 2) != 0) vector.z = -vector.z;
            return vector;
        }

        private SculptDab Mirror(SculptDab dab, int mirror)
        {
            dab.Center = MirrorPoint(dab.Center, mirror);
            dab.Direction = MirrorVector(dab.Direction, mirror);
            dab.PlanePoint = MirrorPoint(dab.PlanePoint, mirror);
            dab.PlaneNormal = MirrorVector(dab.PlaneNormal, mirror);
            dab.Pull = MirrorVector(dab.Pull, mirror);
            return dab;
        }

        // ------------------------------------------------------------------ strokes

        private void BeginStroke(RaycastHit hit, SculptBrush active)
        {
            stroking = true;
            strokeBrush = active;
            strokeTouched.Clear();
            grabbed.Clear();
            dabsSinceCollider = 0;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            planePoint = hit.point;
            planeNormal = worldUp ? Vector3.up : hit.normal;
            // Only the clicked object's hierarchy (the map) is sculpted, never props or creatures near it.
            strokeCandidates = hit.collider.transform.root.GetComponentsInChildren<MeshFilter>();
        }

        private SculptDab NewDab(Vector3 center, Vector3 normal, SculptBrush active, bool invert) => new SculptDab
        {
            Center = center,
            Direction = worldUp ? Vector3.up : normal,
            Radius = radius,
            Strength = strength,
            Hardness = hardness,
            Detail = autoDetail && active != SculptBrush.Smooth && active != SculptBrush.Hole ? detail : 0f,
            Invert = invert,
            PlanePoint = planePoint,
            PlaneNormal = planeNormal,
            Falloff = falloff,
            NoiseScale = noiseScale,
            TerraceStep = terraceStep
        };

        private void Dab(RaycastHit hit, SculptBrush active, bool invert)
        {
            lastDab = hit.point;
            ApplyMirrored(active, NewDab(hit.point, hit.normal, active, invert));
        }

        /// <summary>한 번 찍기를 대칭 자리마다 브러시 구 안의 메시에 적용한다.</summary>
        private void ApplyMirrored(SculptBrush active, SculptDab dab)
        {
            for (int m = 0; m < 4; m++)
            {
                if (!UsesMirror(m)) continue;
                SculptDab mirrored = Mirror(dab, m);
                foreach (SculptMesh mesh in MeshesNear(mirrored.Center))
                    if (mesh.Apply(active, mirrored)) mesh.Upload(false);
            }

            RefreshCollidersNow(false);
        }

        /// <summary>브러시 구에 걸친 편집용 메시들. 처음 걸린 메시는 되돌리기에 등록한다.</summary>
        private IEnumerable<SculptMesh> MeshesNear(Vector3 center)
        {
            var sphere = new Bounds(center, Vector3.one * radius * 2f);
            foreach (MeshFilter filter in strokeCandidates)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.bounds.Intersects(sphere)) continue;
                SculptMesh mesh = Editable(filter);
                if (mesh == null) continue;
                if (strokeTouched.Add(mesh)) Undo.RegisterCompleteObjectUndo(mesh.Mesh, "Mesh Sculpt");
                yield return mesh;
            }
        }

        private void RefreshCollidersNow(bool force)
        {
            if (!force && ++dabsSinceCollider < ColliderRefreshEvery) return;
            dabsSinceCollider = 0;
            foreach (SculptMesh mesh in strokeTouched) RefreshCollider(mesh.Filter);
        }

        private void BeginDrag(RaycastHit hit)
        {
            Camera camera = SceneView.currentDrawingSceneView ? SceneView.currentDrawingSceneView.camera : SceneView.lastActiveSceneView.camera;
            dragPlane = new Plane(-camera.transform.forward, hit.point);
            dragOrigin = lastDragPoint = lastDab = hit.point;
            if (strokeBrush != SculptBrush.Grab) return;

            SculptDab dab = NewDab(hit.point, hit.normal, strokeBrush, false);
            for (int m = 0; m < 4; m++)
            {
                if (!UsesMirror(m)) continue;
                SculptDab mirrored = Mirror(dab, m);
                foreach (SculptMesh mesh in MeshesNear(mirrored.Center).ToList())
                {
                    grabbed.Add((mesh, mesh.BeginGrab(mirrored), m));
                    mesh.Upload(false);
                }
            }
        }

        private bool TryDragPoint(Vector2 mouse, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            point = default;
            if (!dragPlane.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private void Drag(Vector3 point)
        {
            if (strokeBrush == SculptBrush.Grab)
            {
                lastDragPoint = point;
                Vector3 offset = point - dragOrigin;
                var moved = new HashSet<SculptMesh>();
                foreach ((SculptMesh mesh, int handle, int mirror) in grabbed)
                    if (mesh.Grab(handle, MirrorVector(offset, mirror))) moved.Add(mesh);
                foreach (SculptMesh mesh in moved) mesh.Upload(false);
                RefreshCollidersNow(false);
                return;
            }

            // 늘려 뽑기: 지난 자리의 표면을 이번 자리까지 끌어오고, 면을 커서와 함께 옮겨 계속 뽑는다.
            Vector3 pull = point - lastDragPoint;
            if (pull.magnitude < radius * 0.12f) return;
            SculptDab dab = NewDab(lastDragPoint, dragPlane.normal, SculptBrush.SnakeHook, false);
            dab.Pull = pull;
            ApplyMirrored(SculptBrush.SnakeHook, dab);
            lastDragPoint = point;
            dragPlane = new Plane(dragPlane.normal, point);
        }

        private void EndStroke()
        {
            stroking = false;
            foreach (SculptMesh mesh in strokeTouched)
            {
                mesh.EndGrab();
                mesh.Upload(true);
                RefreshCollider(mesh.Filter);
                EditorUtility.SetDirty(mesh.Mesh);
            }

            if (strokeTouched.Count > 0)
            {
                Undo.SetCurrentGroupName("Mesh Sculpt");
                Undo.CollapseUndoOperations(undoGroup);
                UndoHistory.Push("Mesh Sculpt");
            }

            strokeTouched.Clear();
            grabbed.Clear();
        }

        /// <summary>The working copy for a filter, making the saved editable mesh on first use.</summary>
        private SculptMesh Editable(MeshFilter filter)
        {
            if (meshes.TryGetValue(filter, out SculptMesh cached) && cached.Mesh == filter.sharedMesh) return cached;

            Mesh source = filter.sharedMesh;
            if (!AssetDatabase.GetAssetPath(source).StartsWith(SculptFolder))
            {
                Mesh copy = CopyMesh(source);
                string folder = SculptFolder + "/" + (string.IsNullOrEmpty(filter.gameObject.scene.name) ? "Scene" : filter.gameObject.scene.name);
                EnsureFolder(folder);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{filter.name}_Sculpt.asset");
                AssetDatabase.CreateAsset(copy, path);

                Undo.RecordObject(filter, "Mesh Sculpt");
                filter.sharedMesh = copy;
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (collider != null && collider.sharedMesh == source)
                {
                    Undo.RecordObject(collider, "Mesh Sculpt");
                    collider.sharedMesh = copy;
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                if (collider != null) PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                EditorSceneManager.MarkSceneDirty(filter.gameObject.scene);
            }

            var mesh = new SculptMesh(filter);
            meshes[filter] = mesh;
            return mesh;
        }

        private static Mesh CopyMesh(Mesh source)
        {
            var copy = new Mesh { name = source.name + "_Sculpt", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            copy.SetVertices(source.vertices);
            copy.SetNormals(source.normals);
            var uvs = new List<Vector2>();
            for (int channel = 0; channel < 2; channel++)
            {
                source.GetUVs(channel, uvs);
                if (uvs.Count == source.vertexCount) copy.SetUVs(channel, uvs);
            }

            if (source.colors32.Length == source.vertexCount) copy.colors32 = source.colors32;
            if (source.tangents.Length == source.vertexCount) copy.tangents = source.tangents;
            copy.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) copy.SetTriangles(source.GetTriangles(s), s);
            copy.RecalculateBounds();
            return copy;
        }

        private static void RefreshCollider(MeshFilter filter)
        {
            if (filter == null) return;
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null || collider.sharedMesh != filter.sharedMesh) return;
            collider.sharedMesh = null;
            collider.sharedMesh = filter.sharedMesh;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private void OnUndoRedo()
        {
            // Undo restored mesh data (or swapped the mesh back); rebuild working copies lazily.
            foreach (MeshFilter filter in meshes.Keys) RefreshCollider(filter);
            meshes.Clear();
        }

        private void RevertSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(filter)) continue;
                    PrefabUtility.RevertObjectOverride(filter, InteractionMode.UserAction);
                    MeshCollider collider = filter.GetComponent<MeshCollider>();
                    if (collider != null) PrefabUtility.RevertObjectOverride(collider, InteractionMode.UserAction);
                    meshes.Remove(filter);
                }
            }
        }
    }
}
