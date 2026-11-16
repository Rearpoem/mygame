using System;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    /// <summary>Search/navigation of real catalog options only; no workbench or gameplay state.</summary>
    public sealed class OpusCatalogPopup : MonoBehaviour
    {
        private TMP_FontAsset font;
        private CombatTacticsOption[] options = Array.Empty<CombatTacticsOption>();
        private CombatTacticsOption[] visible = Array.Empty<CombatTacticsOption>();
        private Action<string> select;
        private RectTransform list;
        private int highlighted;
        public TMP_InputField SearchInput { get; private set; }
        public Button CloseButton { get; private set; }
        public void Initialize(TMP_FontAsset uiFont)
        {
            font = uiFont;
            var shade = gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .6f);
            var panel = OpusUi.Column("Opus_SearchableCatalogPanel", transform, 8, 12, OpusTheme.Panel);
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.pivot = new Vector2(.5f, .5f); panel.sizeDelta = new Vector2(460, 420);
            var top = OpusUi.Row("CatalogSearch", panel); Ui.FixedHeight(top.gameObject, 34);
            SearchInput = OpusUi.Input("Opus_CatalogQuery", top, font, 0, "搜索目录中文名称 / 说明");
            CloseButton = OpusUi.Button("Opus_CloseCatalog", top, font, "×", Close, 32, 32);
            SearchInput.onValueChanged.AddListener(_ => Rebuild());
            list = OpusUi.Scroll("Opus_FilteredCatalog", panel, out _);
            gameObject.SetActive(false);
        }
        public void Open(CombatTacticsOption[] actualOptions, Action<string> onSelect)
        {
            options = actualOptions ?? Array.Empty<CombatTacticsOption>(); select = onSelect;
            highlighted = 0; SearchInput.SetTextWithoutNotify(""); gameObject.SetActive(true); transform.SetAsLastSibling();
            Rebuild(); SearchInput.ActivateInputField();
        }
        public void Close() { gameObject.SetActive(false); }
        private void Rebuild()
        {
            string query = SearchInput.text ?? "";
            visible = options.Where(option => string.IsNullOrEmpty(query) || (option.label ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (option.description ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            highlighted = Mathf.Clamp(highlighted, 0, Mathf.Max(0, visible.Length - 1));
            OpusUi.Clear(list);
            if (visible.Length == 0) OpusUi.Label("NoMatch", list, font, "无匹配目录项", 15, 32);
            for (int i = 0; i < visible.Length; i++)
            {
                var option = visible[i]; int at = i;
                var button = OpusUi.Button("Opus_Catalog_" + option.id, list, font,
                    option.label + (option.usesThreshold ? " · " + Unit(option.thresholdUnit) : ""), () => Choose(at), 0, 32);
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                if (i == highlighted) { button.GetComponent<Image>().color = OpusTheme.Raised; button.GetComponentInChildren<TMP_Text>().color = OpusTheme.Green; }
            }
        }
        private static string Unit(string unit) { return unit == "ratio" ? "%" : unit == "count" ? "人" : unit == "points" ? "点" : unit == "row" ? "排" : unit; }
        private void Choose(int at) { if (at < 0 || at >= visible.Length) return; string id = visible[at].id; Close(); select?.Invoke(id); }
        private void Update()
        {
            bool down = false, up = false, enter = false, escape = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null) { down = keyboard.downArrowKey.wasPressedThisFrame; up = keyboard.upArrowKey.wasPressedThisFrame; enter = keyboard.enterKey.wasPressedThisFrame; escape = keyboard.escapeKey.wasPressedThisFrame; }
#elif ENABLE_LEGACY_INPUT_MANAGER
            down = Input.GetKeyDown(KeyCode.DownArrow); up = Input.GetKeyDown(KeyCode.UpArrow); enter = Input.GetKeyDown(KeyCode.Return); escape = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (escape) { Close(); return; }
            if (enter) { Choose(highlighted); return; }
            if (down || up) { highlighted = Mathf.Clamp(highlighted + (down ? 1 : -1), 0, Mathf.Max(0, visible.Length - 1)); Rebuild(); }
        }
    }
}
