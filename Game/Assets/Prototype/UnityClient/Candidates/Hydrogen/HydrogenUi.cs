using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Hydrogen.HydrogenThemeTokens;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    internal static class HydrogenUi
    {
        internal static RectTransform Rect(string name, Transform parent) { return Ui.Rect(name, parent); }

        internal static Image Panel(string name, Transform parent, Color? background = null, bool border = true)
        {
            var image = Ui.Image(name, parent, background ?? T.Panel);
            image.raycastTarget = false;
            if (border)
            {
                var outline = image.gameObject.AddComponent<Outline>();
                outline.effectColor = T.Border;
                outline.effectDistance = new Vector2(1, -1);
            }
            return image;
        }

        internal static RectTransform Column(string name, Transform parent, float gap = 8, int padding = 0,
            Color? background = null)
        {
            return Ui.Column(name, parent, gap, padding, background);
        }

        internal static RectTransform Row(string name, Transform parent, float gap = 8)
        {
            return Ui.Row(name, parent, gap);
        }

        internal static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, string value = "",
            float size = 14, Color? color = null, bool singleLine = false)
        {
            var text = Ui.Text(name, parent, font, value, size, color ?? T.Ink);
            text.enableAutoSizing = false;
            text.textWrappingMode = singleLine ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = singleLine ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            return text;
        }

        internal static TMP_Text Heading(string name, Transform parent, TMP_FontAsset font, string title)
        {
            var text = Text(name, parent, font, title, 16, T.Gold);
            text.fontStyle = FontStyles.Bold;
            Ui.FixedHeight(text.gameObject, 27);
            return text;
        }

        internal static Button Button(string name, Transform parent, TMP_FontAsset font, string title,
            UnityAction action, float width = 0, bool primary = false, float height = 34)
        {
            var image = Panel(name, parent, primary ? T.Gold : T.Elevated);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.78f, .78f, .78f);
            colors.disabledColor = new Color(.55f, .55f, .55f, .45f);
            button.colors = colors;
            var text = Text(name + "_Label", image.transform, font, title, 14,
                primary ? T.Abyss : T.Ink, true);
            // A 24px remove button cannot fit its 14pt glyph inside 6px side pads.
            float sideInset = width > 0 && width <= 34 ? 1 : 6;
            Ui.Stretch(text.rectTransform, sideInset, 0, sideInset, 0);
            text.alignment = TextAlignmentOptions.Center;
            if (primary) text.fontStyle = FontStyles.Bold;
            button.onClick.AddListener(action);
            if (width > 0) Ui.Width(image.gameObject, width);
            else Ui.Flexible(image.gameObject, 1, 0);
            Ui.FixedHeight(image.gameObject, height);
            return button;
        }

        internal static void ButtonTitle(Button button, string title)
        {
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = title;
        }

        internal static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font,
            float width = 0, string placeholder = "")
        {
            var input = Ui.Input(name, parent, font, width);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.textComponent.fontSize = 14;
            input.textComponent.enableAutoSizing = false;
            input.textComponent.color = T.Ink;
            input.GetComponent<Image>().color = T.Input;
            input.caretColor = T.Cyan;
            if (width <= 0) Ui.Flexible(input.gameObject, 1, 0);
            Ui.FixedHeight(input.gameObject, 32);
            if (!string.IsNullOrEmpty(placeholder))
            {
                var hint = Text(name + "_Hint", input.textViewport, font, placeholder, 13, T.Muted, true);
                Ui.Stretch(hint.rectTransform);
                hint.alignment = TextAlignmentOptions.MidlineLeft;
                input.placeholder = hint;
            }
            return input;
        }

        internal static TMP_Dropdown Dropdown(string name, Transform parent, TMP_FontAsset font,
            float width = 0)
        {
            var dropdown = TacticsUiControls.Dropdown(name, parent, font, width);
            dropdown.GetComponent<Image>().color = T.Input;
            dropdown.template.GetComponent<Image>().color = T.Elevated;
            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize = 14;
                text.enableAutoSizing = false;
                text.color = T.Ink;
            }
            Ui.FixedHeight(dropdown.gameObject, 32);
            return dropdown;
        }

        internal static RectTransform ScrollColumn(string name, Transform parent, out ScrollRect scroll,
            float gap = 7, int padding = 5)
        {
            var content = TacticsUiControls.ScrollColumn(name, parent, out scroll);
            scroll.GetComponent<Image>().color = T.Input;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = gap;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            AddVerticalScrollbar(scroll);
            return content;
        }

        internal static TMP_Text ScrollText(string name, Transform parent, TMP_FontAsset font,
            out ScrollRect scroll, float size = 14)
        {
            var text = Ui.ScrollText(name, parent, font, out scroll, size);
            scroll.GetComponent<Image>().color = T.Input;
            text.color = T.Ink;
            text.enableAutoSizing = false;
            AddVerticalScrollbar(scroll);
            return text;
        }

        private static void AddVerticalScrollbar(ScrollRect scroll)
        {
            var track = Panel("Hydrogen_VerticalScrollTrack", scroll.transform, T.Border, false);
            track.raycastTarget = true;
            track.rectTransform.anchorMin = new Vector2(1, 0);
            track.rectTransform.anchorMax = new Vector2(1, 1);
            track.rectTransform.offsetMin = new Vector2(-8, 2);
            track.rectTransform.offsetMax = new Vector2(-2, -2);
            var handle = Panel("Hydrogen_VerticalScrollHandle", track.transform, T.Secondary, false);
            handle.raycastTarget = true;
            Ui.Stretch(handle.rectTransform);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.targetGraphic = handle;
            bar.handleRect = handle.rectTransform;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 2;
            // Reserve the thin rail even while it is hidden: no reflow when overflow changes.
            var right = scroll.viewport.offsetMax;
            right.x = -14;
            scroll.viewport.offsetMax = right;
        }

        internal static void FitWholeTextLines(ScrollRect scroll, TMP_Text text)
        {
            if (scroll == null || text?.font == null) return;
            float height = ((RectTransform)scroll.transform).rect.height;
            var face = text.font.faceInfo;
            float line = face.pointSize <= 0 ? text.fontSize * 1.5f : face.lineHeight / face.pointSize * text.fontSize;
            if (height < line + 8 || line <= 0) return;
            // Initial top-aligned explanations show whole Noto lines plus glyph fringe,
            // rather than a cut-off teaser of the next line. Scrolling stays continuous.
            float completeHeight = Mathf.Floor((height - 8) / line) * line + 4;
            var viewport = scroll.viewport;
            var lower = new Vector2(8, -2 - completeHeight);
            var upper = new Vector2(-14, -2);
            if (viewport.anchorMin == new Vector2(0, 1) && viewport.anchorMax == Vector2.one
                && (viewport.offsetMin - lower).sqrMagnitude < .01f && (viewport.offsetMax - upper).sqrMagnitude < .01f) return;
            viewport.anchorMin = new Vector2(0, 1);
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(.5f, 1);
            viewport.offsetMin = lower;
            viewport.offsetMax = upper;
        }

        internal static void Place(RectTransform rect, float left, float top, float right, float bottom)
        {
            Ui.Stretch(rect, left, top, right, bottom);
        }

        internal static void TopRect(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static string Esc(string value)
        {
            return (value ?? "").Replace("<", "＜").Replace(">", "＞");
        }

        internal static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}
