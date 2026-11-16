using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Prototype.UnityClient
{
    internal static class PrototypeUiKit
    {
        internal static readonly Color Background = new Color32(11, 20, 32, 255);
        internal static readonly Color Panel = new Color32(22, 37, 54, 255);
        internal static readonly Color Card = new Color32(31, 49, 68, 255);
        internal static readonly Color Ink = new Color32(232, 239, 244, 255);
        internal static readonly Color Muted = new Color32(158, 180, 196, 255);
        internal static readonly Color Accent = new Color32(231, 190, 116, 255);
        internal static readonly Color Good = new Color32(131, 206, 167, 255);
        internal static readonly Color Bad = new Color32(246, 145, 136, 255);

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void Stretch(RectTransform rect, float left = 0, float top = 0,
            float right = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        internal static Image Image(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        internal static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font,
            string content = "", float size = 18, Color? color = null)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color ?? Ink;
            text.text = content;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.extraPadding = true;
            return text;
        }

        internal static void FixedHeight(GameObject go, float height)
        {
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0;
        }

        internal static void Width(GameObject go, float width)
        {
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0;
        }

        internal static void Flexible(GameObject go, float width = 1, float height = 1)
        {
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minWidth = 0;
            layout.minHeight = 0;
            layout.flexibleWidth = width;
            layout.flexibleHeight = height;
        }

        internal static RectTransform Column(string name, Transform parent, float spacing = 8,
            int padding = 0, Color? background = null)
        {
            var rect = background.HasValue ? Image(name, parent, background.Value).rectTransform : Rect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        internal static RectTransform Row(string name, Transform parent, float spacing = 8)
        {
            var rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return rect;
        }

        internal static Button Button(string name, Transform parent, TMP_FontAsset font,
            string title, UnityAction action, float width = 138, bool accent = false)
        {
            var image = Image(name, parent, accent ? new Color32(109, 81, 39, 255) : Card);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
            colors.pressedColor = new Color(.75f, .75f, .75f);
            colors.disabledColor = new Color(.5f, .5f, .5f, .7f);
            button.colors = colors;
            var label = Text(name + "_Label", image.transform, font, title, 17);
            Stretch(label.rectTransform, 7, 3, 7, 3);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 13;
            label.fontSizeMax = 17;
            button.onClick.AddListener(action);
            Width(image.gameObject, width);
            return button;
        }

        internal static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font, float width)
        {
            var image = Image(name, parent, Background);
            // TMP creates its caret and subscriptions in OnEnable, after the text references exist.
            image.gameObject.SetActive(false);
            var input = image.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect("Text Area", image.transform);
            Stretch(viewport, 9, 4, 9, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var value = Text("Value", viewport, font, "", 19);
            Stretch(value.rectTransform);
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            input.textViewport = viewport;
            input.textComponent = value;
            input.targetGraphic = image;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.selectionColor = new Color(.85f, .65f, .35f, .5f);
            input.caretColor = Ink;
            input.customCaretColor = true;
            Width(image.gameObject, width);
            image.gameObject.SetActive(true);
            return input;
        }

        internal static TextMeshProUGUI ScrollText(string name, Transform parent,
            TMP_FontAsset font, out ScrollRect scroll, float size = 17)
        {
            var image = Image(name, parent, Background);
            Flexible(image.gameObject);
            scroll = image.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;
            var viewport = Rect("Viewport", image.transform);
            Stretch(viewport, 10, 8, 10, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0);
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var text = content.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = Ink;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.extraPadding = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return text;
        }

        internal static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font,
            string text, float width, float size = 17)
        {
            var label = Text(name, parent, font, text, size, Muted);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Width(label.gameObject, width);
            return label;
        }
    }
}
