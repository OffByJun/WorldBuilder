using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;

namespace WorldBuilder.Editor.POIPlacerTool
{
    public sealed class POIPlacerTool : IWorldBuilderTool, IRaycastConsumer
    {
        private enum MarkerType
        {
            POI,
            LootContainer
        }

        [SerializeField] private MarkerType markerType = MarkerType.POI;
        [SerializeField] private string displayName = "Point of Interest";
        [SerializeField] private Color poiColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color lootColor = new Color(0.4f, 0.9f, 1f);
        [SerializeField] private bool removeMode;

        public string ToolName => WorldBuilderLocalization.Get("tool.poiPlacer");
        public string Category => WorldBuilderCategory.World;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
        }

        public bool TryRaycast(out RaycastHit hit)
        {
            return SceneRaycaster.TryRaycast(Event.current.mousePosition, out hit);
        }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(InspectorHelp.Build(ToolName, "help.poiPlacer"));

            EnumField type = new EnumField("Marker Type", markerType);
            type.RegisterValueChangedCallback(evt =>
            {
                markerType = (MarkerType)evt.newValue;
                displayName = markerType == MarkerType.POI ? "Point of Interest" : "Loot Container";
            });
            root.Add(type);

            TextField name = new TextField("Display Name") { value = displayName };
            name.RegisterValueChangedCallback(evt => displayName = evt.newValue);
            root.Add(name);

            Toggle remove = new Toggle("Remove Mode") { value = removeMode };
            remove.RegisterValueChangedCallback(evt => removeMode = evt.newValue);
            root.Add(remove);

            Label hint = new Label(WorldBuilderLocalization.Get("hint.poiPlacer"));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 6f;
            hint.style.opacity = 0.75f;
            root.Add(hint);

            return root;
        }

        public void OnSceneGUI()
        {
            DrawMarkers();

            Event e = Event.current;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && TryRaycast(out RaycastHit hit))
            {
                if (removeMode) RemoveNearest(hit.point);
                else Place(hit.point);
                e.Use();
                SceneView.RepaintAll();
            }
        }

        private void DrawMarkers()
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store == null) return;

            IReadOnlyList<IWorldDataEntry> pois = store.GetAll<POIEntry>();
            for (int i = 0; i < pois.Count; i++)
            {
                if (pois[i] is POIEntry poi && poi.Enabled)
                    DrawMarker(poi.Position, poi.DisplayName, poiColor);
            }

            IReadOnlyList<IWorldDataEntry> loot = store.GetAll<LootContainerEntry>();
            for (int i = 0; i < loot.Count; i++)
            {
                if (loot[i] is LootContainerEntry container && container.Enabled)
                    DrawMarker(container.Position, container.DisplayName, lootColor);
            }
        }

        private static void DrawMarker(Vector3 position, string label, Color color)
        {
            Handles.color = color;
            Handles.DrawWireDisc(position, Vector3.up, 0.8f);
            Handles.DrawWireDisc(position, Vector3.up, 1.6f);
            Handles.Label(position + Vector3.up * 1.5f, label);
        }

        private void Place(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            Undo.IncrementCurrentGroup();

            GameObject go = new GameObject(SanitizeName(displayName));
            go.transform.position = point;

            Undo.RegisterCreatedObjectUndo(go, "Place " + markerType);

            if (store != null)
            {
                Undo.RecordObject(store, "Place " + markerType);
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                if (markerType == MarkerType.POI)
                    store.Add(new POIEntry(point, displayName));
                else
                    store.Add(new LootContainerEntry(point, displayName));
                EditorUtility.SetDirty(store);
                Debug.Log($"[WorldBuilder] {markerType} '{displayName}' placed at {point} ({globalId}).");
            }
            else
            {
                Debug.LogWarning("[WorldBuilder] No active WorldDataStore; marker object created without a data entry.");
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            UndoHistory.Push("Place " + markerType);
        }

        private void RemoveNearest(Vector3 point)
        {
            WorldDataStore store = WorldDataStoreLocator.Active;
            if (store == null) return;

            IWorldDataEntry nearest = null;
            float bestDistance = 3f;

            IReadOnlyList<IWorldDataEntry> pois = store.GetAll<POIEntry>();
            for (int i = 0; i < pois.Count; i++)
            {
                if (pois[i] is POIEntry poi)
                {
                    float distance = Vector3.Distance(poi.Position, point);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = poi;
                    }
                }
            }

            IReadOnlyList<IWorldDataEntry> loot = store.GetAll<LootContainerEntry>();
            for (int i = 0; i < loot.Count; i++)
            {
                if (loot[i] is LootContainerEntry container)
                {
                    float distance = Vector3.Distance(container.Position, point);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = container;
                    }
                }
            }

            if (nearest == null) return;

            Undo.RecordObject(store, "Remove " + markerType);
            if (nearest is POIEntry removedPoi) store.Remove<POIEntry>(removedPoi.Id);
            if (nearest is LootContainerEntry removedContainer) store.Remove<LootContainerEntry>(removedContainer.Id);
            EditorUtility.SetDirty(store);
            UndoHistory.Push("Remove " + markerType);
        }

        private static string SanitizeName(string value)
        {
            string name = string.IsNullOrWhiteSpace(value) ? "POI" : value.Trim();
            return name.Replace('/', '_').Replace('\\', '_');
        }
    }
}
