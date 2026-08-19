using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBuilder.Editor.ZoneEntries;
using Cursor = UnityEngine.UIElements.Cursor;

namespace WorldBuilder.Editor.WorldStatisticsTool
{
    public sealed class WorldStatisticsTool : IWorldBuilderTool
    {
        private WorldDataStore store;
        private StatisticsSnapshot snapshot;
        private bool snapshotCollected;

        private VisualElement statsRoot;

        public string ToolName => WorldBuilderLocalization.Get("tool.worldStatistics");
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
            root.Add(InspectorHelp.Build(ToolName, "help.worldStatistics"));

            ObjectField storeField = new ObjectField("World Data Store")
            {
                objectType = typeof(WorldDataStore),
                value = store
            };
            storeField.RegisterValueChangedCallback(evt =>
            {
                store = evt.newValue as WorldDataStore;
                WorldDataStoreLocator.Active = store;
            });
            root.Add(storeField);

            Button refreshBtn = new Button(() =>
            {
                snapshot = WorldStatisticsCollector.Collect(store);
                snapshotCollected = true;
                RefreshStatsUI();
            }) { text = "Refresh" };
            refreshBtn.style.marginBottom = 8;
            root.Add(refreshBtn);

            statsRoot = new VisualElement();
            root.Add(statsRoot);

            return root;
        }

        private void RefreshStatsUI()
        {
            if (statsRoot == null) return;
            statsRoot.Clear();

            statsRoot.Add(BuildCards());
            statsRoot.Add(BuildObjectsSection());
            statsRoot.Add(BuildRenderingSection());
            statsRoot.Add(BuildPhysicsSection());
            statsRoot.Add(BuildLightingSection());
            statsRoot.Add(BuildMemorySection());
            statsRoot.Add(BuildWorldBuilderSection());
        }

        private VisualElement BuildCards()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 8;

            row.Add(BuildCard("FPS 목표", Application.targetFrameRate < 0 ? "무제한" : Application.targetFrameRate.ToString()));
            row.Add(BuildCard("삼각형", FormatNumber(snapshot.totalTriangles)));
            row.Add(BuildCard("오브젝트", FormatNumber(snapshot.totalObjects)));
            row.Add(BuildCard("메모리", FormatBytes(snapshot.textureMemoryBytes + snapshot.meshMemoryBytes)));

            return row;
        }

        private static VisualElement BuildCard(string title, string value)
        {
            VisualElement card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.alignItems = Align.Center;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.marginRight = 2;
            card.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            Label titleLabel = new Label(title);
            titleLabel.style.fontSize = 9;
            titleLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            card.Add(titleLabel);

            Label valueLabel = new Label(value);
            valueLabel.style.fontSize = 14;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(valueLabel);

            return card;
        }

        private VisualElement BuildObjectsSection()
        {
            Foldout foldout = new Foldout { text = "오브젝트" };
            foldout.Add(BuildStatRow("메시", snapshot.meshCount.ToString(), null));
            foldout.Add(BuildStatRow("씬 오브젝트 총계", snapshot.totalObjects.ToString(), null));
            return foldout;
        }

        private VisualElement BuildRenderingSection()
        {
            Foldout foldout = new Foldout { text = "렌더링" };
            foldout.Add(BuildStatRow("메시", snapshot.meshCount.ToString(), null));
            foldout.Add(BuildStatRow("삼각형", FormatNumber(snapshot.totalTriangles), null));
            foldout.Add(BuildStatRow("버텍스", FormatNumber(snapshot.totalVertices), null));
            foldout.Add(BuildStatRow("머티리얼", snapshot.materialCount.ToString(), null));
            foldout.Add(BuildStatRow("텍스처", snapshot.textureCount.ToString(), null));
            return foldout;
        }

        private VisualElement BuildPhysicsSection()
        {
            Foldout foldout = new Foldout { text = "물리" };
            foldout.Add(BuildStatRow("Rigidbody", snapshot.rigidbodyCount.ToString(), null));
            foldout.Add(BuildStatRow("콜라이더", snapshot.colliderCount.ToString(), null));
            return foldout;
        }

        private VisualElement BuildLightingSection()
        {
            Foldout foldout = new Foldout { text = "라이팅" };
            foldout.Add(BuildStatRow("라이트", snapshot.lightCount.ToString(), null));
            foldout.Add(BuildStatRow("리플렉션 프로브", snapshot.reflectionProbeCount.ToString(), null));
            return foldout;
        }

        private VisualElement BuildMemorySection()
        {
            Foldout foldout = new Foldout { text = "메모리" };
            foldout.Add(BuildStatRow("텍스처 메모리", FormatBytes(snapshot.textureMemoryBytes), null));
            foldout.Add(BuildStatRow("메시 메모리", FormatBytes(snapshot.meshMemoryBytes), null));
            return foldout;
        }

        private VisualElement BuildWorldBuilderSection()
        {
            Foldout foldout = new Foldout { text = "WorldBuilder" };

            Dictionary<string, int> counts = snapshot.worldDataCounts ?? new Dictionary<string, int>();

            AddWbStat(foldout, counts, "BioluminescenceEntry", "생물발광 존", typeof(BioluminescenceEntry));
            AddWbStat(foldout, counts, "AirPocketEntry", "에어포켓", typeof(AirPocketEntry));
            AddWbStat(foldout, counts, "WaterCurrentEntry", "수중 조류", typeof(WaterCurrentEntry));
            AddWbStat(foldout, counts, "PressureZoneEntry", "수압 존", typeof(PressureZoneEntry));
            AddWbStat(foldout, counts, "TemperatureZoneEntry", "온도 존", typeof(TemperatureZoneEntry));
            AddWbStat(foldout, counts, "ToxicZoneEntry", "독성 존", typeof(ToxicZoneEntry));
            AddWbStat(foldout, counts, "VisibilityZoneEntry", "가시거리 존", typeof(VisibilityZoneEntry));
            AddWbStat(foldout, counts, "WreckageEntry", "잔해", typeof(WreckageEntry));
            AddWbStat(foldout, counts, "CreatureSpawnZoneEntry", "크리처 스폰 존", typeof(CreatureSpawnZoneEntry));
            AddWbStat(foldout, counts, "EventTriggerZoneEntry", "이벤트 트리거", typeof(EventTriggerZoneEntry));
            AddWbStat(foldout, counts, "BiomeEntry", "바이옴 청크", typeof(BiomeEntry));
            AddWbStat(foldout, counts, "SpawnPointEntry", "스폰 포인트", typeof(SpawnPointEntry));

            return foldout;
        }

        private void AddWbStat(VisualElement parent, Dictionary<string, int> counts, string key, string label, Type entryType)
        {
            counts.TryGetValue(key, out int count);
            parent.Add(BuildStatRow(label, count.ToString(), entryType));
        }

        private VisualElement BuildStatRow(string label, string value, Type filterType)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            Label nameLabel = new Label(label);
            nameLabel.style.flexGrow = 1;
            row.Add(nameLabel);

            Label valueLabel = new Label(value);
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(valueLabel);

            if (filterType != null)
            {
                row.RegisterCallback<PointerDownEvent>(_ => NavigateToBrowser(filterType));
            }

            return row;
        }

        private static void NavigateToBrowser(Type entryType)
        {
            IWorldDataBrowser browser = WorldBuilderToolRegistry.GetByInterface<IWorldDataBrowser>();
            if (browser != null)
                browser.FilterByType(entryType);
        }

        private static string FormatNumber(int value)
        {
            return value.ToString("N0");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024f * 1024 * 1024)).ToString("F2") + " GB";
            if (bytes >= 1024L * 1024)
                return (bytes / (1024f * 1024)).ToString("F2") + " MB";
            if (bytes >= 1024L)
                return (bytes / 1024f).ToString("F1") + " KB";
            return bytes + " B";
        }
    }
}
