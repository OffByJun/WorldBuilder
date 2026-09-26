using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush
{
    /// <summary>
    /// Shared building blocks for the Prefab Brush inspector: the stylesheet, the card and foldout-card
    /// sections, buttons, and the Korean/English text that follows the window's language toggle.
    /// </summary>
    internal static class PrefabBrushUI
    {
        private const string StylePath = "Packages/com.emiteat.worldbuilder/Editor/PrefabBrush/PrefabBrush.uss";

        private static readonly Dictionary<string, (string ko, string en)> Text = new Dictionary<string, (string, string)>
        {
            { "mode.paint", ("칠하기", "Paint") },
            { "mode.erase", ("지우기", "Erase") },
            { "mode.paintHint", ("씬을 클릭하거나 드래그하면 반경 안에 팔레트 모델을 흩뿌립니다.", "Click or drag in the scene to scatter palette models inside the radius.") },
            { "mode.eraseHint", ("씬을 클릭하면 반경 안의 인스턴스를 지웁니다.", "Click in the scene to remove instances inside the radius.") },
            { "eraseOnly", ("팔레트 모델만 지우기", "Erase Palette Models Only") },
            { "eraseOnly.tip", ("켜 두면 손으로 배치한 코어·시설 같은 오브젝트는 지우지 않습니다.", "Leaves hand-placed objects such as the core or stations alone.") },

            { "card.brush", ("브러시", "Brush") },
            { "radius", ("반경", "Radius") },
            { "density", ("밀도", "Density") },
            { "paintOnDrag", ("드래그로 칠하기", "Paint On Drag") },
            { "dragSpacing", ("드래그 간격", "Drag Spacing") },
            { "seed", ("시드", "Seed") },
            { "randomize", ("무작위", "Randomize") },

            { "card.palette", ("팔레트", "Palette") },
            { "palette.meta", ("{0}개 · 찍히는 모델 {1}개", "{0} models · {1} active") },
            { "palette.hint", ("타일을 눌러 비중·크기·회전을 정합니다. 프로젝트 창에서 끌어다 놓아도 추가됩니다.", "Click a tile to set its weight, scale and rotation. Drag assets in from the Project window to add them.") },
            { "palette.empty", ("아직 비어 있습니다.\n아래 라이브러리에서 모델을 누르거나 프로젝트 창에서 끌어다 놓으세요.", "The palette is empty.\nClick a model in the library below or drag one in from the Project window.") },
            { "tile.off", ("끔", "Off") },
            { "tile.env", ("환경", "ENV") },
            { "tile.missing", ("(비어 있음)", "(missing)") },
            { "tip.missing", ("프리팹이 빠진 칸", "Entry without a prefab") },
            { "tip.env", ("환경 브러시가 만들어 등록한 모델", "Registered by an environment brush") },

            { "detail.ping", ("프로젝트에서 보기", "Show In Project") },
            { "detail.remove", ("팔레트에서 빼기", "Remove") },
            { "detail.weight", ("비중", "Weight") },
            { "detail.weightTip", ("다른 모델과 비교한 상대값입니다. 2는 1보다 두 배 자주 나옵니다.", "Relative to the other models: 2 appears twice as often as 1.") },
            { "detail.share", ("한 번 찍을 때 이 모델이 나올 확률 약 {0}%", "About {0}% of placed instances") },
            { "detail.shareOff", ("비중이 0이라 찍히지 않습니다.", "Weight is 0, so this model is never placed.") },
            { "detail.height", ("높이 보정 (m)", "Height Offset (m)") },
            { "detail.heightTip", ("양수면 띄우고 음수면 바닥에 묻습니다. 피벗이 바닥 중앙이 아닐 때 씁니다.", "Positive lifts, negative sinks. Useful when the pivot is not at the base.") },
            { "detail.custom", ("이 모델만 따로 설정", "Override Transform") },
            { "detail.customTip", ("끄면 공통 배치의 크기·회전 설정을 따릅니다.", "When off, the model follows the shared placement settings.") },
            { "detail.inherit", ("공통 설정 사용 중 · 크기 {0}~{1}배 · 무작위 회전 {2} · 기울기 맞춤 {3}", "Using shared settings · scale {0}–{1}× · random yaw {2} · align {3}") },
            { "detail.scale", ("크기 (배)", "Scale (×)") },
            { "detail.scaleRange", ("{0} ~ {1}배 사이에서 무작위", "Random between {0}× and {1}×") },
            { "on", ("켬", "on") },
            { "off", ("끔", "off") },

            { "card.library", ("모델 라이브러리", "Model Library") },
            { "library.meta", ("{0}개", "{0} models") },
            { "library.folders", ("폴더", "Folders") },
            { "library.foldersTip", ("쉼표로 구분한 폴더 목록. 이 폴더 안의 프리팹과 3D 모델을 보여 줍니다.", "Comma-separated folders to list prefabs and 3D models from.") },
            { "library.refresh", ("새로고침", "Refresh") },
            { "library.empty", ("폴더에 모델이 없습니다.", "No models in these folders.") },
            { "library.noMatch", ("검색 결과가 없습니다.", "No models match the search.") },
            { "library.tipAdd", ("눌러서 팔레트에 추가", "Click to add to the palette") },
            { "library.tipRemove", ("눌러서 팔레트에서 빼기", "Click to remove from the palette") },
            { "library.size", ("썸네일 크기", "Thumbnail size") },

            { "card.placement", ("공통 배치", "Placement") },
            { "align", ("지면 기울기에 맞추기", "Align To Normal") },
            { "yaw", ("무작위 회전 (Y축)", "Random Yaw") },
            { "scaleRange", ("크기 범위", "Scale Range") },
            { "chunkSize", ("청크 크기", "Chunk Size") },
            { "waterData", ("수중 런타임 데이터", "Water Runtime Data") },
            { "waterData.tip", ("모디파이어 그래프의 수심 마스크 노드를 켭니다.", "Enables the Water Depth Mask node in Modifier Graphs.") },
            { "duplicateSO", ("공유 SO 복제", "Duplicate Shared SO") },
            { "duplicateSO.tip", ("배치할 때 프리팹이 참조하는 ScriptableObject를 인스턴스마다 복제합니다.", "Clones ScriptableObjects the prefab references for every placed instance.") },

            { "card.mask", ("마스크", "Mask") },
            { "useHeight", ("높이 제한", "Use Height Mask") },
            { "minHeight", ("최소 높이", "Min Height") },
            { "maxHeight", ("최대 높이", "Max Height") },
            { "useSlope", ("경사 제한", "Use Slope Mask") },
            { "maxSlope", ("최대 경사각", "Max Slope Angle") },
            { "useBiome", ("바이옴 제한", "Use Biome Mask") },
            { "allowedBiome", ("허용 바이옴", "Allowed Biome") },

            { "card.environment", ("환경 브러시", "Environment Brush") },
            { "card.modifier", ("모디파이어 그래프", "Modifier Graph") },
            { "modifierGraph", ("그래프", "Graph") },
            { "openGraph", ("그래프 열기", "Open Graph") },

            { "card.preset", ("프리셋", "Preset") },
            { "preset", ("프리셋", "Preset") },
            { "preset.load", ("불러오기", "Load") },
            { "preset.save", ("프리셋에 저장", "Save To Preset") },

            { "card.strokes", ("기록된 스트로크", "Recorded Strokes") },
            { "strokes.meta", ("{0}개", "{0}") },
            { "strokes.hint", ("찍을 때마다 기록되어 다시 찍기와 청크 굽기에 쓰입니다.", "Every stroke is recorded for replay and chunk baking.") },
            { "strokes.replay", ("다시 찍기", "Replay") },
            { "strokes.export", ("내보내기", "Export") },
            { "strokes.import", ("가져오기", "Import") },
            { "strokes.clear", ("비우기", "Clear") }
        };

        private static StyleSheet styleSheet;

        public static string T(string key)
        {
            if (!Text.TryGetValue(key, out (string ko, string en) value)) return key;
            return WorldBuilderLocalization.Current == WorldBuilderLocalization.Language.Korean ? value.ko : value.en;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);

        public static void ApplyStyle(VisualElement root)
        {
            if (styleSheet == null) styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList("pb-root");
        }

        /// <summary>A titled card; add content to the returned element. <paramref name="meta"/> sits right of the title.</summary>
        public static VisualElement Card(string title, out Label meta)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("pb-card");

            VisualElement header = new VisualElement();
            header.AddToClassList("pb-card__header");
            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("pb-card__title");
            meta = new Label();
            meta.AddToClassList("pb-card__meta");
            header.Add(titleLabel);
            header.Add(meta);
            card.Add(header);
            return card;
        }

        public static VisualElement Card(string title) => Card(title, out _);

        public static Foldout FoldoutCard(string title, bool open)
        {
            Foldout foldout = new Foldout { text = title, value = open };
            foldout.AddToClassList("pb-foldout");
            return foldout;
        }

        public static Button Button(string text, Action onClick, string modifier = null)
        {
            Button button = new Button(onClick) { text = text };
            button.AddToClassList("pb-button");
            if (!string.IsNullOrEmpty(modifier)) button.AddToClassList("pb-button--" + modifier);
            return button;
        }

        public static Label Hint(string text)
        {
            Label label = new Label(text);
            label.AddToClassList("pb-hint");
            return label;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("pb-row");
            foreach (VisualElement child in children) row.Add(child);
            return row;
        }

        public static TElement WithTooltip<TElement>(this TElement element, string tooltip) where TElement : VisualElement
        {
            element.tooltip = tooltip;
            return element;
        }
    }
}
