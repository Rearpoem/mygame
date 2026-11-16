using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Prototype.UnityClient.Candidates.Argon
{
    // Presentation primitives only. No session, rule validation or combat calculation lives here.
    internal static class ArgonUi
    {
        internal static RectTransform Rect(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false);
            return result;
        }

        internal static void Fill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static Image Image(string name, Transform parent, Color color, bool hit = false)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = hit;
            return image;
        }

        internal static RectTransform Frame(string name, Transform parent, Color? edge = null, bool corners = true)
        {
            var border = Image(name, parent, edge ?? ArgonTheme.Border);
            var inside = Image("Slate", border.transform, ArgonTheme.Panel);
            Fill(inside.rectTransform, 1, 1, 1, 1);
            if (corners)
            {
                for (var i = 0; i < 4; i++)
                {
                    var corner = Image("BrassRivet_" + i, border.transform, ArgonTheme.Brass);
                    corner.rectTransform.anchorMin = corner.rectTransform.anchorMax = new Vector2(i % 2, i / 2);
                    corner.rectTransform.pivot = corner.rectTransform.anchorMin;
                    corner.rectTransform.sizeDelta = new Vector2(4, 4);
                }
            }
            var content = Rect("Content", border.transform);
            Fill(content, 9, 8, 9, 8);
            return content;
        }

        internal static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, string value,
            float size = 14, Color? color = null, bool bold = false)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color ?? ArgonTheme.Ink;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.text = value;
            text.raycastTarget = false;
            text.extraPadding = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            // Noto has a 1.448em line box: single-line controls need their full
            // authored height. Panel/scroll padding supplies the paragraph inset.
            text.margin = new Vector4(3, 0, 3, 0);
            return text;
        }

        internal static void Size(GameObject go, float width = -1, float height = -1,
            float flexibleWidth = 0, float flexibleHeight = 0)
        {
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minWidth = width < 0 ? 0 : width;
            layout.preferredWidth = width;
            layout.minHeight = height < 0 ? 0 : height;
            layout.preferredHeight = height;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
        }

        internal static RectTransform Column(string name, Transform parent, float spacing = 6, int padding = 0)
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = spacing;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return rect;
        }

        internal static RectTransform Row(string name, Transform parent, float spacing = 6)
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return rect;
        }

        internal static Button Button(string name, Transform parent, TMP_FontAsset font, string value,
            UnityAction click = null, float width = -1, float height = 32, bool primary = false)
        {
            var image = Image(name, parent, primary ? new Color32(100, 72, 32, 255) : ArgonTheme.Card, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
            colors.pressedColor = new Color(.75f, .75f, .75f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.48f, .48f, .48f);
            button.colors = colors;
            var border = image.gameObject.AddComponent<Outline>();
            border.effectColor = primary ? ArgonTheme.Brass : ArgonTheme.Border;
            border.effectDistance = new Vector2(1, -1);
            var text = Text("Label", image.transform, font, value, 14, primary ? ArgonTheme.Ink : ArgonTheme.Secondary, primary);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Fill(text.rectTransform, 4, 0, 4, 0);
            if (value != null && value.Length == 1 && width > 0 && width <= 36)
            {
                // 24px arrow/remove buttons cannot fit a full-width Noto glyph after
                // the ordinary 8px inset plus text margins. Keep the same font size.
                Fill(text.rectTransform, 0, 0, 0, 0);
                text.margin = new Vector4(1, 0, 1, 0);
            }
            if (click != null) button.onClick.AddListener(click);
            Size(image.gameObject, width, height, width < 0 ? 1 : 0);
            return button;
        }

        internal static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font,
            string placeholder = "", float width = -1, float height = 30)
        {
            var image = Image(name, parent, ArgonTheme.Well, true);
            var input = image.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect("Viewport", image.transform);
            Fill(viewport, 6, 2, 6, 2);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Text("Text", viewport, font, "", 14);
            Fill(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var hint = Text("Placeholder", viewport, font, placeholder, 14, ArgonTheme.Muted);
            Fill(hint.rectTransform);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = image;
            input.caretColor = ArgonTheme.Focus;
            input.selectionColor = new Color(.22f, .74f, .97f, .32f);
            Size(image.gameObject, width, height, width < 0 ? 1 : 0);
            return input;
        }

        internal static TMP_Dropdown Dropdown(string name, Transform parent, TMP_FontAsset font,
            float width = -1, float height = 30)
        {
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            var dropdown = go.GetComponent<TMP_Dropdown>();
            go.GetComponent<Image>().color = ArgonTheme.Well;
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
                text.fontSize = 14;
                text.color = ArgonTheme.Secondary;
                text.raycastTarget = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }
            dropdown.captionText.rectTransform.offsetMin = new Vector2(7, 2);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-23, -2);
            var arrow = go.transform.Find("Arrow");
            arrow.GetComponent<Image>().enabled = false;
            var glyph = Text("Chevron", arrow, font, "▼", 12, ArgonTheme.Brass);
            Fill(glyph.rectTransform);
            glyph.alignment = TextAlignmentOptions.Center;
            dropdown.template.GetComponent<Image>().color = ArgonTheme.Card;
            dropdown.template.sizeDelta = new Vector2(0, 240);
            var item = (RectTransform)dropdown.template.Find("Viewport/Content/Item");
            item.sizeDelta = new Vector2(0, 34);
            item.Find("Item Background").GetComponent<Image>().color = ArgonTheme.Selected;
            item.Find("Item Checkmark").GetComponent<Image>().color = ArgonTheme.Brass;
            Size(go, width, height, width < 0 ? 1 : 0);
            return dropdown;
        }

        internal static void Options(TMP_Dropdown dropdown, IEnumerable<string> values, int selected)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(values.ToList());
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, Mathf.Max(0, dropdown.options.Count - 1)));
            dropdown.RefreshShownValue();
        }

        internal static RectTransform Scroll(string name, Transform parent, out ScrollRect scroll,
            bool column = true, int padding = 3, float gap = 6)
        {
            var root = Image(name, parent, ArgonTheme.Well, true).rectTransform;
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;
            var viewport = Rect("Viewport", root);
            Fill(viewport, 4, 4, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = column ? Column("Content", viewport, gap, padding) : Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            Size(root.gameObject, -1, -1, 1, 1);
            return content;
        }

        internal static TMP_Text Readout(string name, Transform parent, TMP_FontAsset font, out ScrollRect scroll)
        {
            var content = Scroll(name, parent, out scroll);
            var text = Text("Text", content, font, "", 14, ArgonTheme.Secondary);
            Size(text.gameObject, -1, -1, 1);
            return text;
        }

        internal static void Clear(Transform root)
        {
            foreach (Transform child in root)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        internal static void Selected(Button button, bool selected)
        {
            if (button == null) return;
            button.GetComponent<Image>().color = selected ? ArgonTheme.Selected : ArgonTheme.Card;
            var outline = button.GetComponent<Outline>();
            if (outline != null) outline.effectColor = selected ? ArgonTheme.Brass : ArgonTheme.Border;
        }
    }
}
