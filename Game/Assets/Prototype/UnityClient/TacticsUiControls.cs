using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    internal static class TacticsUiControls
    {
        internal static string PlayerTeam(CombatScenario scenario)
        {
            if (scenario == null || scenario.units == null || string.IsNullOrWhiteSpace(scenario.playerTeam)
                || !scenario.units.Any(unit => unit.team == scenario.playerTeam))
                throw new ArgumentException("战术战例需显式配置 playerTeam，并包含该队伍的可编排角色。");
            return scenario.playerTeam;
        }

        internal static TMP_Dropdown Dropdown(string name, Transform parent, TMP_FontAsset font,
            float width = 0)
        {
            var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            root.name = name;
            root.transform.SetParent(parent, false);
            var dropdown = root.GetComponent<TMP_Dropdown>();
            root.GetComponent<Image>().color = Ui.Background;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
                text.fontSize = 16;
                text.color = Ui.Ink;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.raycastTarget = false;
            }
            dropdown.captionText.rectTransform.offsetMin = new Vector2(9, 3);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-30, -3);
            dropdown.itemText.rectTransform.offsetMin = new Vector2(25, 2);
            dropdown.itemText.rectTransform.offsetMax = new Vector2(-8, -2);
            var arrow = root.transform.Find("Arrow");
            arrow.GetComponent<Image>().enabled = false;
            var arrowText = Ui.Text("ArrowLabel", arrow, font, "▼", 15, Ui.Accent);
            Ui.Stretch(arrowText.rectTransform);
            arrowText.alignment = TextAlignmentOptions.Center;
            var template = dropdown.template;
            template.sizeDelta = new Vector2(0, 224);
            template.GetComponent<Image>().color = Ui.Card;
            var content = (RectTransform)template.Find("Viewport/Content");
            content.sizeDelta = new Vector2(0, 36);
            var item = (RectTransform)content.Find("Item");
            item.sizeDelta = new Vector2(0, 36);
            item.Find("Item Background").GetComponent<Image>().color = Ui.Panel;
            var check = item.Find("Item Checkmark").GetComponent<Image>();
            check.color = Ui.Accent;
            check.rectTransform.sizeDelta = new Vector2(12, 12);
            foreach (var image in template.GetComponentsInChildren<Image>(true))
                if (image.name == "Sliding Area" || image.name == "Handle") image.color = Ui.Muted;
            dropdown.ClearOptions();
            if (width > 0) Ui.Width(root, width);
            else Ui.Flexible(root, 1, 0);
            Ui.FixedHeight(root, 36);
            return dropdown;
        }

        internal static void Options(TMP_Dropdown dropdown, IEnumerable<string> labels, int selected = 0)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(labels));
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, Mathf.Max(0, dropdown.options.Count - 1)));
            dropdown.RefreshShownValue();
        }

        internal static RectTransform ScrollColumn(string name, Transform parent, out ScrollRect scroll)
        {
            var background = Ui.Image(name, parent, Ui.Background);
            Ui.Flexible(background.gameObject);
            scroll = background.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var viewport = Ui.Rect("Viewport", background.transform);
            Ui.Stretch(viewport, 5, 5, 5, 5);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hitArea = viewport.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0, 0, 0, 0);
            var content = Ui.Column("Content", viewport, 7, 5);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            return content;
        }

        internal static TMP_Text Heading(string name, Transform parent, TMP_FontAsset font, string text)
        {
            var label = Ui.Text(name, parent, font, text, 16, Ui.Accent);
            Ui.FixedHeight(label.gameObject, 27);
            return label;
        }
    }
}
