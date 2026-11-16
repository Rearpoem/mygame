using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    internal static class OpusTheme
    {
        internal static readonly Color Background = Hex(0x10141A), Top = Hex(0x0B0E12), Panel = Hex(0x171D25),
            Raised = Hex(0x1F2731), Sunken = Hex(0x0F1318), Line = Hex(0x2C3644), StrongLine = Hex(0x3E4B5C),
            Ink = Hex(0xECE6D6), Secondary = Hex(0xA9AFB8), Disabled = Hex(0x5E6672), Green = Hex(0x3FB8A0),
            Amber = Hex(0xE8A93A), Red = Hex(0xE05A5F), Hp = Hex(0x6BCB77), Blue = Hex(0x5B9BF0),
            Purple = Hex(0xB57BE8), History = Hex(0xC8A46A), HistoryBackground = Hex(0x2A2418);
        private static Color Hex(uint x) { return new Color32((byte)(x >> 16), (byte)(x >> 8), (byte)x, 255); }
    }

    internal static class OpusUi
    {
        internal static RectTransform Rect(string name, Transform parent) { return Ui.Rect(name, parent); }
        internal static Image Panel(string name, Transform parent, Color? color = null)
        {
            var image = Ui.Image(name, parent, color ?? OpusTheme.Panel);
            image.raycastTarget = false;
            return image;
        }
        internal static RectTransform Column(string name, Transform parent, float gap = 6, int padding = 8, Color? color = null)
        { return Ui.Column(name, parent, gap, padding, color); }
        internal static RectTransform Row(string name, Transform parent, float gap = 6) { return Ui.Row(name, parent, gap); }
        internal static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, string value = "", float size = 13,
            Color? color = null, bool single = false)
        {
            var text = Ui.Text(name, parent, font, value, size, color ?? OpusTheme.Ink);
            text.enableAutoSizing = false;
            text.textWrappingMode = single ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = single ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            return text;
        }
        internal static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size = 15, float height = 26)
        {
            var text = Text(name, parent, font, value, size);
            Ui.FixedHeight(text.gameObject, height);
            return text;
        }
        internal static Button Button(string name, Transform parent, TMP_FontAsset font, string value, UnityAction action,
            float width = 0, float height = 34, bool primary = false)
        {
            var image = Panel(name, parent, primary ? OpusTheme.Amber : OpusTheme.Raised);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(.8f, .8f, .8f); colors.disabledColor = new Color(.5f, .5f, .5f, .6f);
            button.colors = colors;
            var label = Text("Label", image.transform, font, value, 15, primary ? OpusTheme.Top : OpusTheme.Ink, true);
            Ui.Stretch(label.rectTransform, 6, 0, 6, 0);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(action);
            if (width > 0) Ui.Width(image.gameObject, width); else Ui.Flexible(image.gameObject, 1, 0);
            Ui.FixedHeight(image.gameObject, height);
            return button;
        }
        internal static void Title(Button button, string text) { button.GetComponentInChildren<TMP_Text>().text = text; }
        internal static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font, float width = 0, string hint = "")
        {
            var input = Ui.Input(name, parent, font, width);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.textComponent.fontSize = 13;
            input.textComponent.enableAutoSizing = false;
            input.textComponent.color = OpusTheme.Ink;
            input.GetComponent<Image>().color = OpusTheme.Sunken;
            if (width <= 0) Ui.Flexible(input.gameObject, 1, 0);
            Ui.FixedHeight(input.gameObject, 32);
            if (!string.IsNullOrEmpty(hint))
            {
                var text = Text("Placeholder", input.textViewport, font, hint, 13, OpusTheme.Secondary, true);
                Ui.Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.MidlineLeft;
                input.placeholder = text;
            }
            return input;
        }
        internal static TMP_Dropdown Dropdown(string name, Transform parent, TMP_FontAsset font, float width = 0)
        {
            var dropdown = TacticsUiControls.Dropdown(name, parent, font, width);
            dropdown.GetComponent<Image>().color = OpusTheme.Sunken;
            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true)) { text.color = OpusTheme.Ink; text.fontSize = 13; text.enableAutoSizing = false; }
            Ui.FixedHeight(dropdown.gameObject, 32);
            return dropdown;
        }
        internal static RectTransform Scroll(string name, Transform parent, out ScrollRect scroll, float gap = 6)
        {
            var content = TacticsUiControls.ScrollColumn(name, parent, out scroll);
            scroll.GetComponent<Image>().color = OpusTheme.Sunken;
            content.GetComponent<VerticalLayoutGroup>().spacing = gap;
            return content;
        }
        internal static TMP_Text ScrollText(string name, Transform parent, TMP_FontAsset font, out ScrollRect scroll, float size = 13)
        {
            var text = Ui.ScrollText(name, parent, font, out scroll, size);
            text.color = OpusTheme.Ink; text.enableAutoSizing = false;
            scroll.GetComponent<Image>().color = OpusTheme.Sunken;
            return text;
        }
        internal static void ContentText(TMP_Text text)
        { text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; }
        internal static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) { var child = parent.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
        }
        internal static void FixedRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        internal static void Band(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top);
        }
        internal static void Sprite(string name, Transform parent, TMP_FontAsset font, Sprite sprite, string fallback, float size)
        {
            var image = Panel(name, parent, sprite == null ? OpusTheme.Raised : Color.white);
            image.sprite = sprite; image.preserveAspect = true;
            Ui.Width(image.gameObject, size); Ui.FixedHeight(image.gameObject, size);
            if (sprite == null)
            {
                var letter = Text("Placeholder", image.transform, font, fallback, Mathf.Min(24, size / 2), OpusTheme.Green, true);
                Ui.Stretch(letter.rectTransform); letter.alignment = TextAlignmentOptions.Center;
            }
        }
        internal static string Esc(string text) { return (text ?? "").Replace("<", "＜").Replace(">", "＞"); }
        internal static string N(double n) { return n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
