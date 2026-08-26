#if WB_UGUI
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WorldBuilder.Runtime.Saves;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// Programmatically assembles a working save-menu panel (scroll list of slots with
    /// Save/Load/Delete + refresh) under any Canvas — a ready sample wired to
    /// <see cref="SaveSlotMenuService"/>. Delete this file if you ship custom UI.
    /// </summary>
    public static class SaveMenuUIBuilder
    {
        public static RectTransform Build(RectTransform parent, SaveSlotMenuService service)
        {
            var root = new GameObject("WB_SaveMenu", typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 520f);

            Image background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);

            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childForceExpandHeight = false;

            Text title = CreateText(rect, "Save / Load", 26, TextAnchor.MiddleCenter);
            title.color = new Color(0.65f, 0.9f, 1f);

            InputField nameField = CreateInputField(rect, "slot_name");

            Button saveButton = CreateButton(rect, "Save Snapshot");
            saveButton.onClick.AddListener(() => service.Save(nameField.text));

            ScrollRect list = CreateScrollList(rect);
            RefreshSlots(list.content, service, nameField);

            Button closeButton = CreateButton(rect, "Close");
            closeButton.onClick.AddListener(() => Object.Destroy(root));

            service.Changed += () => RefreshSlots(list.content, service, nameField);
            return rect;
        }

        private static void RefreshSlots(RectTransform content, SaveSlotMenuService service,
            InputField nameField)
        {
            foreach (Transform child in content)
                Object.Destroy(child.gameObject);

            foreach (WorldSaveService.SaveInfo info in service.Refresh())
            {
                var row = new GameObject(info.Slot, typeof(RectTransform));
                var rowRect = row.GetComponent<RectTransform>();
                rowRect.SetParent(content, false);
                rowRect.sizeDelta = new Vector2(0f, 56f);
                HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 4f;
                rowLayout.childForceExpandWidth = true;

                Text label = CreateText(rowRect,
                    $"{info.Slot}\n{info.TimestampUtc:yyyy-MM-dd HH:mm}", 16, TextAnchor.MiddleLeft);
                label.alignment = TextAnchor.MiddleLeft;

                Button loadButton = CreateButton(rowRect, "Load", width: 90f);
                string loadSlot = info.Slot;
                loadButton.onClick.AddListener(() => service.Load(loadSlot));

                Button deleteButton = CreateButton(rowRect, "X", width: 44f);
                deleteButton.onClick.AddListener(() => service.Delete(loadSlot));
            }
        }

        private static ScrollRect CreateScrollList(RectTransform parent)
        {
            var scrollGo = new GameObject("slots", typeof(RectTransform));
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.SetParent(parent, false);
            ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();

            Image viewportImage = null;
            var viewport = new GameObject("viewport", typeof(RectTransform));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRect, false);
            viewportImage = viewport.AddComponent<Image>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("content", typeof(RectTransform));
            var content = contentGo.GetComponent<RectTransform>();
            content.SetParent(viewportRect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.vertical = true;
            scroll.horizontal = false;
            return scroll;
        }

        private static Text CreateText(RectTransform parent, string value, int size,
            TextAnchor anchor)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = size * 1.4f;
            return text;
        }

        private static Button CreateButton(RectTransform parent, string label, float width = 0f)
        {
            var go = new GameObject("btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.32f, 0.42f);
            Button button = go.AddComponent<Button>();

            Text text = go.AddComponent<Text>();
            text.text = label;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;

            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 40f;
            element.preferredWidth = width > 0 ? width : -1f;
            return button;
        }

        private static InputField CreateInputField(RectTransform parent, string initial)
        {
            var go = new GameObject("input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image background = go.AddComponent<Image>();
            background.color = new Color(0.1f, 0.14f, 0.2f);

            InputField field = go.AddComponent<InputField>();
            field.text = initial;
            Text text = go.AddComponent<Text>();
            text.supportRichText = false;
            text.fontSize = 18;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            field.textComponent = text;
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 40f;
            return field;
        }
    }
}
#endif
