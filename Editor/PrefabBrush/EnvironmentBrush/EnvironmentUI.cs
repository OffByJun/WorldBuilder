using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush
{
    internal static class EnvironmentUI
    {
        private const float LabelWidth = 118f;

        private static readonly Color Accent = new Color(0.16f, 0.62f, 0.6f);
        private static readonly Color AccentHover = new Color(0.22f, 0.74f, 0.71f);
        private static readonly Color HeaderColor = new Color(0.55f, 0.82f, 0.84f);
        private static readonly Color CardBackground = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color CardBorder = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color FaintLine = new Color(1f, 1f, 1f, 0.08f);

        public static FloatField Float(VisualElement parent, string label, float value, Action<float> onChange)
        {
            FloatField field = new FloatField(label) { value = value };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static Slider Slider(VisualElement parent, string label, float min, float max, float value, Action<float> onChange)
        {
            Slider field = new Slider(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static SliderInt SliderInt(VisualElement parent, string label, int min, int max, int value, Action<int> onChange)
        {
            SliderInt field = new SliderInt(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static IntegerField Int(VisualElement parent, string label, int value, Action<int> onChange)
        {
            IntegerField field = new IntegerField(label) { value = value };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static Toggle Bool(VisualElement parent, string label, bool value, Action<bool> onChange)
        {
            Toggle field = new Toggle(label) { value = value };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static EnumField Enum(VisualElement parent, string label, System.Enum value, Action<System.Enum> onChange)
        {
            EnumField field = new EnumField(label, value);
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static ColorField Color(VisualElement parent, string label, Color value, Action<Color> onChange)
        {
            ColorField field = new ColorField(label) { value = value };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static Vector2Field Vec2(VisualElement parent, string label, Vector2 value, Action<Vector2> onChange)
        {
            Vector2Field field = new Vector2Field(label) { value = value };
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            Decorate(field, field.labelElement);
            parent.Add(field);
            return field;
        }

        public static void Header(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = HeaderColor;
            label.style.fontSize = 11f;
            label.style.letterSpacing = 0.5f;
            label.style.marginTop = 10f;
            label.style.marginBottom = 3f;
            label.style.paddingBottom = 2f;
            label.style.borderBottomWidth = 1f;
            label.style.borderBottomColor = FaintLine;
            parent.Add(label);
        }

        public static VisualElement Card(VisualElement parent)
        {
            VisualElement card = new VisualElement();
            card.style.backgroundColor = CardBackground;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.paddingTop = 6f;
            card.style.paddingBottom = 8f;
            card.style.marginTop = 2f;
            card.style.marginBottom = 4f;
            SetBorderRadius(card, 6f);
            SetBorderWidth(card, 1f);
            SetBorderColor(card, CardBorder);
            parent.Add(card);
            return card;
        }

        public static Button AccentButton(VisualElement parent, string text, Action onClick)
        {
            Button button = new Button(onClick) { text = text };
            button.style.height = 28f;
            button.style.marginTop = 10f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.color = UnityEngine.Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.backgroundColor = Accent;
            SetBorderRadius(button, 6f);
            SetBorderWidth(button, 0f);
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = AccentHover);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = Accent);
            parent.Add(button);
            return button;
        }

        public static void StyleBrushFoldout(Foldout foldout)
        {
            foldout.style.marginBottom = 6f;
            Toggle toggle = foldout.Q<Toggle>();
            if (toggle != null)
            {
                toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            VisualElement content = foldout.contentContainer;
            content.style.backgroundColor = CardBackground;
            content.style.paddingLeft = 12f;
            content.style.paddingRight = 10f;
            content.style.paddingTop = 4f;
            content.style.paddingBottom = 8f;
            content.style.marginTop = 2f;
            content.style.borderLeftWidth = 3f;
            content.style.borderLeftColor = Accent;
            content.style.borderBottomLeftRadius = 4f;
            content.style.borderBottomRightRadius = 4f;
        }

        private static void Decorate(VisualElement field, Label label)
        {
            field.style.marginTop = 2f;
            field.style.marginBottom = 2f;
            field.style.flexShrink = 1f;
            if (label != null)
            {
                label.style.minWidth = LabelWidth;
                label.style.width = LabelWidth;
                label.style.flexShrink = 0f;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.color = new Color(0.78f, 0.8f, 0.82f);
            }
        }

        private static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }
    }
}
