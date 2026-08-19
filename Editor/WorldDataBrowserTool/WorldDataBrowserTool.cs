using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.WorldDataBrowserTool
{
    public sealed class WorldDataBrowserTool : IWorldBuilderTool, IWorldDataBrowser
    {
        private WorldDataStore store;
        private string searchText = "";
        private Type filteredType;
        private BiomeType biomeFilter = BiomeType.Ocean;
        private bool filterByBiome;
        private Func<IWorldDataEntry, IComparable> sortSelector;

        private VisualElement categoryContainer;
        private Label totalCountLabel;
        private DropdownField typeDropdownField;
        private List<Type> categoryTypes = new List<Type>();

        public string ToolName => WorldBuilderLocalization.Get("tool.worldDataBrowser");
        public string Category => WorldBuilderCategory.Hub;
        public Texture2D ToolIcon => null;

        public void OnEnable()
        {
            if (store == null)
                store = WorldDataStoreLocator.Active;
        }

        public void OnSceneGUI() { }

        public VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            root.Add(InspectorHelp.Build(ToolName, "help.worldDataBrowser"));

            ObjectField storeField = new ObjectField("World Data Store")
            {
                objectType = typeof(WorldDataStore),
                value = store
            };
            storeField.RegisterValueChangedCallback(evt =>
            {
                store = evt.newValue as WorldDataStore;
                WorldDataStoreLocator.Active = store;
                RebuildAll(root);
            });
            root.Add(storeField);

            if (store == null)
            {
                root.Add(new HelpBox(
                    "WorldDataStore 에셋을 위 필드에 지정하거나 Assets > Create > WorldBuilder > WorldDataStore 로 생성하세요.",
                    HelpBoxMessageType.Warning));
                return root;
            }

            BuildBrowserContent(root);
            return root;
        }

        public void FilterByType(Type entryType)
        {
            filteredType = entryType;
            if (typeDropdownField != null)
            {
                string name = entryType != null ? entryType.Name : "전체";
                typeDropdownField.SetValueWithoutNotify(name);
            }
            RebuildEntries();
        }

        public void SortBy(Func<IWorldDataEntry, IComparable> keySelector)
        {
            sortSelector = keySelector;
            RebuildEntries();
        }

        private void BuildBrowserContent(VisualElement root)
        {
            TextField searchField = new TextField("검색") { value = searchText };
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue;
                RebuildEntries();
            });
            root.Add(searchField);

            root.Add(BuildTypeDropdown());

            Toggle biomeToggle = new Toggle("바이옴 필터") { value = filterByBiome };
            biomeToggle.RegisterValueChangedCallback(evt =>
            {
                filterByBiome = evt.newValue;
                RebuildEntries();
            });
            root.Add(biomeToggle);

            EnumField biomeField = new EnumField("바이옴", biomeFilter);
            biomeField.RegisterValueChangedCallback(evt =>
            {
                biomeFilter = (BiomeType)evt.newValue;
                if (filterByBiome) RebuildEntries();
            });
            root.Add(biomeField);

            ScrollView scrollView = new ScrollView { style = { minHeight = 200 } };
            categoryContainer = scrollView;
            root.Add(scrollView);

            RebuildEntries();

            totalCountLabel = new Label();
            totalCountLabel.style.marginTop = 4;
            root.Add(totalCountLabel);

            UpdateTotalCount();
        }

        private DropdownField BuildTypeDropdown()
        {
            List<string> options = new List<string> { "전체" };
            categoryTypes.Clear();

            if (store != null)
            {
                foreach (KeyValuePair<Type, List<IWorldDataEntry>> kvp in store.GetAllCategories())
                {
                    options.Add(kvp.Key.Name);
                    categoryTypes.Add(kvp.Key);
                }
            }

            int initialIndex = 0;
            if (filteredType != null)
            {
                int found = categoryTypes.IndexOf(filteredType);
                if (found >= 0) initialIndex = found + 1;
            }

            typeDropdownField = new DropdownField("타입 필터", options, initialIndex);
            typeDropdownField.RegisterValueChangedCallback(evt =>
            {
                int idx = typeDropdownField.index;
                filteredType = idx > 0 && idx - 1 < categoryTypes.Count ? categoryTypes[idx - 1] : null;
                RebuildEntries();
            });
            return typeDropdownField;
        }

        private void RebuildAll(VisualElement root)
        {
            root.Clear();
            root.Add(InspectorHelp.Build(ToolName, "help.worldDataBrowser"));

            ObjectField storeField = new ObjectField("World Data Store")
            {
                objectType = typeof(WorldDataStore),
                value = store
            };
            storeField.RegisterValueChangedCallback(evt =>
            {
                store = evt.newValue as WorldDataStore;
                WorldDataStoreLocator.Active = store;
                RebuildAll(root);
            });
            root.Add(storeField);

            if (store == null)
            {
                root.Add(new HelpBox(
                    "WorldDataStore 에셋을 위 필드에 지정하거나 Assets > Create > WorldBuilder > WorldDataStore 로 생성하세요.",
                    HelpBoxMessageType.Warning));
                return;
            }

            BuildBrowserContent(root);
        }

        private void RebuildEntries()
        {
            if (categoryContainer == null || store == null) return;
            categoryContainer.Clear();

            IReadOnlyDictionary<Type, List<IWorldDataEntry>> allCategories = store.GetAllCategories();
            int totalShown = 0;

            foreach (KeyValuePair<Type, List<IWorldDataEntry>> kvp in allCategories)
            {
                if (filteredType != null && kvp.Key != filteredType) continue;

                List<IWorldDataEntry> filtered = FilterEntries(kvp.Value);
                if (filtered.Count == 0) continue;

                if (sortSelector != null)
                    filtered.Sort((a, b) => Comparer<IComparable>.Default.Compare(sortSelector(a), sortSelector(b)));

                Foldout foldout = new Foldout { text = kvp.Key.Name + " (" + filtered.Count + ")" };
                for (int i = 0; i < filtered.Count; i++)
                    foldout.Add(BuildEntryRow(filtered[i]));

                categoryContainer.Add(foldout);
                totalShown += filtered.Count;
            }

            if (totalCountLabel != null)
                totalCountLabel.text = "총 " + totalShown.ToString("N0") + "개 항목";
        }

        private List<IWorldDataEntry> FilterEntries(List<IWorldDataEntry> entries)
        {
            List<IWorldDataEntry> result = new List<IWorldDataEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                IWorldDataEntry entry = entries[i];
                if (entry == null) continue;

                if (!string.IsNullOrEmpty(searchText) &&
                    entry.DisplayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (filterByBiome && entry is IBiomeAware biomeAware && biomeAware.Biome != biomeFilter)
                    continue;

                result.Add(entry);
            }
            return result;
        }

        private VisualElement BuildEntryRow(IWorldDataEntry entry)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            Toggle enableToggle = new Toggle { value = entry.Enabled };
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                if (store != null) Undo.RecordObject(store, "Toggle Entry Enabled");
                entry.Enabled = evt.newValue;
                if (store != null) EditorUtility.SetDirty(store);
            });
            enableToggle.style.marginRight = 4;
            row.Add(enableToggle);

            Label nameLabel = new Label(entry.DisplayName);
            nameLabel.style.flexGrow = 1;
            row.Add(nameLabel);

            string shortId = entry.Id.Length > 8 ? entry.Id.Substring(0, 8) + "…" : entry.Id;
            Label idLabel = new Label(shortId);
            idLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            idLabel.style.fontSize = 9;
            idLabel.style.marginLeft = 4;
            row.Add(idLabel);

            if (HasMissingReference(entry))
            {
                Label warning = new Label("⚠");
                warning.style.color = Color.red;
                warning.style.marginLeft = 4;
                row.Add(warning);
            }

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount >= 2)
                    OnEntryDoubleClick(entry);
                else if (evt.button == 0 && evt.clickCount == 1)
                    OnEntrySingleClick(entry);
            });

            row.AddManipulator(new ContextualMenuManipulator(menuEvent =>
            {
                menuEvent.menu.AppendAction("복제", _ => DuplicateEntry(entry));
                menuEvent.menu.AppendAction("삭제", _ => DeleteEntry(entry));
                menuEvent.menu.AppendAction("Ping", _ => PingEntry(entry));
            }));

            return row;
        }

        private static bool HasMissingReference(IWorldDataEntry entry)
        {
            string globalId = GetSceneObjectGlobalId(entry);
            if (string.IsNullOrEmpty(globalId)) return false;
            if (!GlobalObjectId.TryParse(globalId, out GlobalObjectId goid)) return true;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(goid) == null;
        }

        private static string GetSceneObjectGlobalId(IWorldDataEntry entry)
        {
            if (entry is AirPocketEntry ap) return ap.SceneObjectGlobalId;
            if (entry is WaterCurrentEntry wc) return wc.SceneObjectGlobalId;
            if (entry is BioluminescenceEntry bl) return bl.SceneObjectGlobalId;
            if (entry is PressureZoneEntry pz) return pz.SceneObjectGlobalId;
            if (entry is TemperatureZoneEntry tz) return tz.SceneObjectGlobalId;
            if (entry is ToxicZoneEntry tox) return tox.SceneObjectGlobalId;
            if (entry is VisibilityZoneEntry vz) return vz.SceneObjectGlobalId;
            if (entry is WreckageEntry wr) return wr.SceneObjectGlobalId;
            if (entry is CreatureSpawnZoneEntry cs) return cs.SceneObjectGlobalId;
            if (entry is EventTriggerZoneEntry et) return et.SceneObjectGlobalId;
            if (entry is SpawnPointEntry sp) return sp.SceneObjectGlobalId;
            return string.Empty;
        }

        private static void OnEntrySingleClick(IWorldDataEntry entry)
        {
            string globalId = GetSceneObjectGlobalId(entry);
            if (string.IsNullOrEmpty(globalId)) return;
            if (!GlobalObjectId.TryParse(globalId, out GlobalObjectId goid)) return;
            UnityEngine.Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(goid);
            if (obj != null) Selection.activeObject = obj;
        }

        private static void OnEntryDoubleClick(IWorldDataEntry entry)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;
            sceneView.Frame(new Bounds(entry.Position, Vector3.one * 5f), false);
        }

        private static void PingEntry(IWorldDataEntry entry)
        {
            string globalId = GetSceneObjectGlobalId(entry);
            if (string.IsNullOrEmpty(globalId)) return;
            if (!GlobalObjectId.TryParse(globalId, out GlobalObjectId goid)) return;
            UnityEngine.Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(goid);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }

        private void DuplicateEntry(IWorldDataEntry entry)
        {
            if (store == null) return;
            Undo.RecordObject(store, "Duplicate Entry");
            Vector3 offset = entry.Position + Vector3.right;
            IWorldDataEntry duplicate = CreateDuplicate(entry, offset);
            if (duplicate == null) return;
            AddEntryToStore(duplicate);
            EditorUtility.SetDirty(store);
            RebuildEntries();
        }

        private static IWorldDataEntry CreateDuplicate(IWorldDataEntry entry, Vector3 newPos)
        {
            if (entry is AirPocketEntry ap) return new AirPocketEntry(newPos, ap.Size, ap.Label);
            if (entry is WaterCurrentEntry wc) return new WaterCurrentEntry(newPos, wc.Direction, wc.Strength);
            if (entry is BioluminescenceEntry bl) return new BioluminescenceEntry(newPos, bl.Radius, bl.Intensity, bl.Color);
            if (entry is PressureZoneEntry pz) return new PressureZoneEntry(newPos, pz.Radius, pz.Pressure, pz.DamagePerSecond);
            if (entry is TemperatureZoneEntry tz) return new TemperatureZoneEntry(newPos, tz.Radius, tz.Temperature);
            if (entry is ToxicZoneEntry tox) return new ToxicZoneEntry(newPos, tox.Radius, tox.Intensity, tox.DamagePerSecond);
            if (entry is VisibilityZoneEntry vz) return new VisibilityZoneEntry(newPos, vz.Radius, vz.Visibility, vz.FogColor);
            if (entry is WreckageEntry wr) return new WreckageEntry(newPos, wr.PrefabGuid, wr.LogNumber);
            if (entry is CreatureSpawnZoneEntry cs) return new CreatureSpawnZoneEntry(newPos, cs.Biome, cs.PrefabId, cs.Density, cs.Radius);
            if (entry is EventTriggerZoneEntry et) return new EventTriggerZoneEntry(newPos, et.EventId, et.Radius, et.OneShot);
            if (entry is BiomeEntry bm) return new BiomeEntry(newPos, bm.Biome, bm.ChunkCoord, bm.ChunkSize);
            if (entry is SpawnPointEntry sp) return new SpawnPointEntry(newPos, sp.PrefabId);
            if (entry is ResourceEntry re) return new ResourceEntry(newPos, re.DisplayName);
            if (entry is LootContainerEntry lc) return new LootContainerEntry(newPos, lc.DisplayName);
            if (entry is POIEntry poi) return new POIEntry(newPos, poi.DisplayName);
            if (entry is PathEntry pe) return new PathEntry(newPos, pe.DisplayName);
            return null;
        }

        private void AddEntryToStore(IWorldDataEntry entry)
        {
            if (entry is AirPocketEntry ap) { store.Add(ap); return; }
            if (entry is WaterCurrentEntry wc) { store.Add(wc); return; }
            if (entry is BioluminescenceEntry bl) { store.Add(bl); return; }
            if (entry is PressureZoneEntry pz) { store.Add(pz); return; }
            if (entry is TemperatureZoneEntry tz) { store.Add(tz); return; }
            if (entry is ToxicZoneEntry tox) { store.Add(tox); return; }
            if (entry is VisibilityZoneEntry vz) { store.Add(vz); return; }
            if (entry is WreckageEntry wr) { store.Add(wr); return; }
            if (entry is CreatureSpawnZoneEntry cs) { store.Add(cs); return; }
            if (entry is EventTriggerZoneEntry et) { store.Add(et); return; }
            if (entry is BiomeEntry bm) { store.Add(bm); return; }
            if (entry is SpawnPointEntry sp) { store.Add(sp); return; }
            if (entry is ResourceEntry re) { store.Add(re); return; }
            if (entry is LootContainerEntry lc) { store.Add(lc); return; }
            if (entry is POIEntry poi) { store.Add(poi); return; }
            if (entry is PathEntry pe) { store.Add(pe); return; }
        }

        private void DeleteEntry(IWorldDataEntry entry)
        {
            if (store == null) return;
            Undo.RecordObject(store, "Delete Entry");
            RemoveEntryFromStore(entry);
            EditorUtility.SetDirty(store);
            RebuildEntries();
            UpdateTotalCount();
        }

        private void RemoveEntryFromStore(IWorldDataEntry entry)
        {
            if (entry is AirPocketEntry ap) { store.Remove<AirPocketEntry>(ap.Id); return; }
            if (entry is WaterCurrentEntry wc) { store.Remove<WaterCurrentEntry>(wc.Id); return; }
            if (entry is BioluminescenceEntry bl) { store.Remove<BioluminescenceEntry>(bl.Id); return; }
            if (entry is PressureZoneEntry pz) { store.Remove<PressureZoneEntry>(pz.Id); return; }
            if (entry is TemperatureZoneEntry tz) { store.Remove<TemperatureZoneEntry>(tz.Id); return; }
            if (entry is ToxicZoneEntry tox) { store.Remove<ToxicZoneEntry>(tox.Id); return; }
            if (entry is VisibilityZoneEntry vz) { store.Remove<VisibilityZoneEntry>(vz.Id); return; }
            if (entry is WreckageEntry wr) { store.Remove<WreckageEntry>(wr.Id); return; }
            if (entry is CreatureSpawnZoneEntry cs) { store.Remove<CreatureSpawnZoneEntry>(cs.Id); return; }
            if (entry is EventTriggerZoneEntry et) { store.Remove<EventTriggerZoneEntry>(et.Id); return; }
            if (entry is BiomeEntry bm) { store.Remove<BiomeEntry>(bm.Id); return; }
            if (entry is SpawnPointEntry sp) { store.Remove<SpawnPointEntry>(sp.Id); return; }
            if (entry is ResourceEntry re) { store.Remove<ResourceEntry>(re.Id); return; }
            if (entry is LootContainerEntry lc) { store.Remove<LootContainerEntry>(lc.Id); return; }
            if (entry is POIEntry poi) { store.Remove<POIEntry>(poi.Id); return; }
            if (entry is PathEntry pe) { store.Remove<PathEntry>(pe.Id); return; }
        }

        private void UpdateTotalCount()
        {
            if (totalCountLabel == null || store == null) return;
            totalCountLabel.text = "총 " + store.GetTotalCount().ToString("N0") + "개 항목";
        }
    }
}
