using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Opus.OpusTheme;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    public sealed partial class OpusCandidateView
    {
        private TMP_Text codexTitle, matchText, skillDetail;
        private TMP_Dropdown effectFilter, modeFilter;
        private RectTransform skillRows;
        private string previewSkillId, codexKey;
        private readonly Dictionary<string, Button> skillButtons = new Dictionary<string, Button>();
        public TMP_InputField ActionSearchInput { get; private set; }
        public Button ClearSearchButton { get; private set; }
        public Button SetActionButton { get; private set; }
        public IReadOnlyDictionary<string, Button> SkillButtons { get { return skillButtons; } }
        public TMP_Text SkillDetailsText { get { return skillDetail; } }
        private void BuildCodex(RectTransform parent)
        {
            codexTitle = OpusUi.Label("Opus_CodexTitle", parent, font, "技能目录", 17, 28);
            var search = OpusUi.Row("Opus_CodexSearch", parent, 4); Ui.FixedHeight(search.gameObject, 32);
            ActionSearchInput = OpusUi.Input("Opus_ActionSearch", search, font, 0, "搜索 名称 / 效果 / 范围");
            ActionSearchInput.onValueChanged.AddListener(query => { if (!rendering) shared.SetActionSearch(query); });
            ClearSearchButton = OpusUi.Button("Opus_ClearSearch", search, font, "×", () => shared.SetActionSearch(""), 30, 32);
            effectFilter = OpusUi.Dropdown("Opus_EffectFilter", parent, font);
            TacticsUiControls.Options(effectFilter, new[] { "效果 · 全部", "伤害", "治疗", "回蓝", "汲魔", "状态增益", "状态削弱", "净化 / 驱散" });
            effectFilter.onValueChanged.AddListener(_ => { codexKey = null; pendingRefresh = true; });
            modeFilter = OpusUi.Dropdown("Opus_ModeFilter", parent, font);
            TacticsUiControls.Options(modeFilter, new[] { "范围 · 全部", "单体", "整排", "纵列", "全体" });
            modeFilter.onValueChanged.AddListener(_ => { codexKey = null; pendingRefresh = true; });
            matchText = OpusUi.Label("Opus_SearchResult", parent, font, "仅当前队员白名单", 12, 24);
            skillRows = OpusUi.Scroll("Opus_SkillRows", parent, out _, 4);
            var details = OpusUi.Column("Opus_SkillDetailCard", parent, 5, 6, T.Raised); Ui.FixedHeight(details.gameObject, 190);
            skillDetail = OpusUi.ScrollText("Opus_ActualSkillDetails", details, font, out _, 13);
            SetActionButton = OpusUi.Button("Opus_SetAsAction", details, font, "设为行动", () =>
            { if (previewSkillId == null) return; activeSegment = 3; Mutate(form => form.actionId = previewSkillId); }, 0, 34);
        }
        private void RenderCodex()
        {
            var unit = state.workbench.draftScenario.units.First(item => item.id == state.form.selectedUnitId);
            codexTitle.text = "技能目录 · " + unit.name + " · " + state.catalog.actions.Length;
            SetText(ActionSearchInput, state.form.actionSearch);
            if (state.catalog.actions.All(item => item.id != previewSkillId)) previewSkillId = state.form.actionId;
            if (string.IsNullOrEmpty(previewSkillId)) previewSkillId = state.form.actionId;
            var preview = state.catalog.actions.FirstOrDefault(item => item.id == previewSkillId);
            skillDetail.text = DescribeAction(preview);
            SetActionButton.interactable = preview != null;
            string key = state.workbench.draftScenario.id + ":" + unit.id + ":" + state.workbench.draftRevision + ":" + state.form.actionSearch + ":"
                + state.form.actionId + ":" + previewSkillId + ":" + effectFilter.value + ":" + modeFilter.value;
            if (key == codexKey) return; codexKey = key;
            OpusUi.Clear(skillRows); skillButtons.Clear();
            var actions = (state.visibleActions ?? Array.Empty<CombatActionConfig>()).Where(MatchesLocalFilters).ToArray();
            bool empty = state.matchingActionCount == 0 && !string.IsNullOrEmpty(state.form.actionSearch);
            matchText.text = empty ? "无匹配 · 已选行动保留，草稿不变" : state.matchingActionCount + " 项匹配 / 白名单 " + state.catalog.actions.Length;
            if (empty || actions.Length == 0)
            {
                var none = OpusUi.Text("Opus_NoSkillMatch", skillRows, font, "没有匹配「" + OpusUi.Esc(state.form.actionSearch)
                    + "」的技能\n只在「" + unit.name + "」的 " + state.catalog.actions.Length + " 项中搜索。", 15, T.Secondary);
                Ui.FixedHeight(none.gameObject, 90);
                OpusUi.Button("Opus_ClearEmptySearch", skillRows, font, "清除搜索", () => { shared.SetActionSearch(""); effectFilter.SetValueWithoutNotify(0); modeFilter.SetValueWithoutNotify(0); codexKey = null; pendingRefresh = true; });
                return;
            }
            foreach (var action in actions)
            {
                string id = action.id;
                var button = OpusUi.Button("Opus_Skill_" + id, skillRows, font, "", () => { previewSkillId = id; pendingRefresh = true; }, 0, 52);
                button.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
                var mark = OpusUi.Text("ScopeGlyph", button.transform, font, ModeShort(action.targetMode), 15, T.Green, true);
                OpusUi.FixedRect(mark.rectTransform, 3, 13, 26, 26); mark.alignment = TextAlignmentOptions.Center;
                var name = OpusUi.Text("SkillName", button.transform, font, action.displayName, 15, T.Ink, true); OpusUi.Band(name.rectTransform, 32, 1, 92, 24);
                var cost = OpusUi.Text("SkillCost", button.transform, font, "MP " + action.mpCost + " W " + OpusUi.N(action.baseWait), 12, T.Secondary, true);
                cost.rectTransform.anchorMin = cost.rectTransform.anchorMax = Vector2.one; cost.rectTransform.pivot = Vector2.one;
                cost.rectTransform.anchoredPosition = new Vector2(-4, -1); cost.rectTransform.sizeDelta = new Vector2(88, 24); cost.alignment = TextAlignmentOptions.MidlineRight;
                var summary = OpusUi.Text("SkillSummary", button.transform, font, EffectLabel(action) + (id == state.form.actionId ? " · 使用中" : ""), 12, T.Secondary, true);
                OpusUi.Band(summary.rectTransform, 32, 27, 4, 22);
                button.GetComponent<Image>().color = id == previewSkillId ? T.Raised : T.Panel;
                if (id == previewSkillId || id == state.form.actionId)
                {
                    var marker = OpusUi.Panel("SelectedInscription", button.transform, T.Green);
                    OpusUi.FixedRect(marker.rectTransform, 0, 0, 3, 52);
                }
                skillButtons.Add(id, button);
                Tip(button.gameObject, DescribeAction(action));
            }
        }
        private bool MatchesLocalFilters(CombatActionConfig action)
        {
            string mode = string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode;
            if (modeFilter.value > 0 && mode != new[] { "", "single", "row", "column", "all" }[modeFilter.value]) return false;
            switch (effectFilter.value)
            {
                case 1: return action.effectKind == "damage";
                case 2: return action.effectKind == "heal";
                case 3: return action.effectKind == "restore_mp";
                case 4: return action.effectKind == "drain_mp";
                case 5: return action.effectKind == "apply_status" && Beneficial(action.statusKind);
                case 6: return action.effectKind == "apply_status" && !Beneficial(action.statusKind);
                case 7: return action.effectKind == "cleanse" || action.effectKind == "dispel";
                default: return true;
            }
        }
        private static bool Beneficial(string kind) { return kind == "guard" || kind == "magic_guard" || kind == "empowered" || kind == "haste"; }
        private string EffectLabel(CombatActionConfig action)
        {
            var label = state.catalog.effectKinds.FirstOrDefault(item => item.id == action.effectKind)?.label ?? action.effectKind;
            if (action.effectKind == "apply_status") return label + " · " + OpusBattleText.StatusName(action.statusKind);
            return label + (action.magnitude > 0 ? " " + action.magnitude : "");
        }
        private string DescribeAction(CombatActionConfig action)
        {
            if (action == null) return "点击技能行查看真实详情，再点「设为行动」写入第④段。";
            string text = action.displayName + "\nMP 费用 " + action.mpCost + " · 基础 WAIT " + OpusUi.N(action.baseWait)
                + " · " + OptionLabel(state.catalog.targetModes, string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode)
                + "\n" + EffectLabel(action) + " · " + (action.isMagic ? "魔法动作" : "非魔法动作");
            if (action.effectKind == "damage") text += "\n伤害类型：" + OptionLabel(state.catalog.damageTypes, string.IsNullOrEmpty(action.damageType) ? "legacy" : action.damageType);
            if (action.effectKind == "apply_status") text += "\n强度 " + action.statusMagnitude + " · 持续 " + OpusUi.N(action.statusDuration) + " WAIT";
            return text + "\n先选锚点，再扩合法覆盖；一次扣费、一次 WAIT。";
        }
    }
}
