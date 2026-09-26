using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        private const string SculptFolder = "Assets/008_WorldBuilder/Sculpted";
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
            { "scope", ("클릭한 오브젝트와 같은 루트(예: Map) 아래의 메시만 수정합니다.", "Only meshes under the same root as the clicked object (e.g. Map) are changed.") }
        };

        private static readonly SculptBrush[] Brushes =
        {
            SculptBrush.Raise, SculptBrush.Dig, SculptBrush.Smooth,
            SculptBrush.Flatten, SculptBrush.Inflate, SculptBrush.Hole
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

        private bool stroking;
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
            for (int row = 0; row < 2; row++)
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

            SelectBrush(brush);
            return root;
        }

        private void SelectBrush(SculptBrush value)
        {
            brush = value;
            foreach (Button button in brushButtons)
                button.EnableInClassList("pb-mode__button--active", (SculptBrush)button.userData == value);
            if (hintLabel != null) hintLabel.text = T("hint." + value);
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
                BeginStroke(hit);
                Dab(hit, active, e.control);
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

        private void DrawCursor(RaycastHit hit, SculptBrush active, bool invert)
        {
            Color color = active switch
            {
                SculptBrush.Raise => invert ? new Color(1f, 0.55f, 0.3f) : new Color(0.35f, 0.85f, 1f),
                SculptBrush.Dig => invert ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.55f, 0.3f),
                SculptBrush.Smooth => new Color(0.6f, 1f, 0.6f),
                SculptBrush.Flatten => new Color(1f, 0.9f, 0.4f),
                SculptBrush.Inflate => new Color(0.85f, 0.6f, 1f),
                _ => new Color(1f, 0.3f, 0.3f)
            };
            Handles.color = color;
            Handles.DrawWireDisc(hit.point, hit.normal, radius, 2f);
            Handles.color = new Color(color.r, color.g, color.b, 0.45f);
            Handles.DrawWireDisc(hit.point, hit.normal, Mathf.Max(0.05f, radius * hardness));
            Vector3 dir = worldUp ? Vector3.up : hit.normal;
            if (active == SculptBrush.Raise || active == SculptBrush.Dig)
            {
                float sign = (active == SculptBrush.Raise) != invert ? 1f : -1f;
                Handles.DrawLine(hit.point, hit.point + dir * (radius * 0.35f * sign), 2f);
            }
        }

        // ------------------------------------------------------------------ strokes

        private void BeginStroke(RaycastHit hit)
        {
            stroking = true;
            strokeTouched.Clear();
            dabsSinceCollider = 0;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            planePoint = hit.point;
            planeNormal = worldUp ? Vector3.up : hit.normal;
            // Only the clicked object's hierarchy (the map) is sculpted, never props or creatures near it.
            strokeCandidates = hit.collider.transform.root.GetComponentsInChildren<MeshFilter>();
        }

        private void Dab(RaycastHit hit, SculptBrush active, bool invert)
        {
            lastDab = hit.point;
            var dab = new SculptDab
            {
                Center = hit.point,
                Direction = worldUp ? Vector3.up : hit.normal,
                Radius = radius,
                Strength = strength,
                Hardness = hardness,
                Detail = autoDetail && active != SculptBrush.Smooth && active != SculptBrush.Hole ? detail : 0f,
                Invert = invert,
                PlanePoint = planePoint,
                PlaneNormal = planeNormal
            };

            var sphere = new Bounds(hit.point, Vector3.one * radius * 2f);
            foreach (MeshFilter filter in strokeCandidates)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.bounds.Intersects(sphere)) continue;
                SculptMesh mesh = Editable(filter);
                if (mesh == null) continue;
                if (strokeTouched.Add(mesh)) Undo.RegisterCompleteObjectUndo(mesh.Mesh, "Mesh Sculpt");
                if (mesh.Apply(active, dab)) mesh.Upload(false);
            }

            if (++dabsSinceCollider >= ColliderRefreshEvery)
            {
                dabsSinceCollider = 0;
                foreach (SculptMesh mesh in strokeTouched) RefreshCollider(mesh.Filter);
            }
        }

        private void EndStroke()
        {
            stroking = false;
            foreach (SculptMesh mesh in strokeTouched)
            {
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

        private static void EnsureFolder(string path)
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
