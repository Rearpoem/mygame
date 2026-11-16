using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Hydrogen.HydrogenThemeTokens;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    public sealed class HydrogenConditionControls
    {
        public TMP_Dropdown KindDropdown { get; internal set; }
        public TMP_Dropdown SubjectDropdown { get; internal set; }
        public TMP_InputField ThresholdInput { get; internal set; }
        public TMP_InputField ValueInput { get; internal set; }
        public TMP_Text UnitText { get; internal set; }
        public Button RemoveButton { get; internal set; }
    }

    public sealed partial class HydrogenCandidateView
    {
        private RectTransform termList;
        private RectTransform mixedRow;
        private RectTransform skillList;
        private readonly List<HydrogenConditionControls> terms = new List<HydrogenConditionControls>();
        private readonly Dictionary<string, Button> skillButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private string skillSignature;
        private TMP_Dropdown skillCategory;
        private TMP_Text actionMatch;
        private TMP_Text actionDetails;
        public TMP_InputField LabelInput { get; private set; }
        public TMP_Dropdown OperationDropdown { get; private set; }
        public TMP_Dropdown GroupingDropdown { get; private set; }
        public TMP_Dropdown LeftOperatorDropdown { get; private set; }
        public TMP_Dropdown RightOperatorDropdown { get; private set; }
        public TMP_Dropdown ScopeDropdown { get; private set; }
        public TMP_Dropdown SelectorDropdown { get; private set; }
        public TMP_InputField ActionSearchInput { get; private set; }
        public Button ClearSearchButton { get; private set; }
        public Button AddConditionButton { get; private set; }
        public IReadOnlyList<HydrogenConditionControls> Terms { get { return terms; } }
        public IReadOnlyDictionary<string, Button> SkillButtons { get { return skillButtons; } }
        public TMP_Text ActionMatchText { get { return actionMatch; } }
        public TMP_Text ActionDetailsText { get { return actionDetails; } }

        private void BuildForm(RectTransform parent)
        {
            parent.GetComponent<VerticalLayoutGroup>().spacing = 5;
            formTitle = HydrogenUi.Text("Hydrogen_FormMode", parent, font, "四段规则编辑器", 15, T.Gold);
            Ui.FixedHeight(formTitle.gameObject, 22);
            LabelInput = HydrogenUi.Input("Hydrogen_RuleLabel", parent, font, 0, "规则标题（可选）");
            Ui.FixedHeight(LabelInput.gameObject, 28);
            LabelInput.onValueChanged.AddListener(value => EditForm(form => form.label = value));
            var commits = HydrogenUi.Row("Hydrogen_FormCommitBar", parent, 5);
            Ui.FixedHeight(commits.gameObject, 30);
            replace = HydrogenUi.Button("Hydrogen_Replace", commits, font, "应用替换", () => shared.Replace(), 0, true, 30);
            insert = HydrogenUi.Button("Hydrogen_Insert", commits, font, "新增到末尾", () => shared.Insert(), 0, false, 30);
            discard = HydrogenUi.Button("Hydrogen_DiscardForm", commits, font, "放弃表单修改", () => shared.LoadSelected(), 0, false, 30);

            FormSection(parent, "1. 条件表达式 · 逐候选求值");
            var operation = HydrogenUi.Row("Hydrogen_ConditionOperation", parent, 5);
            Ui.FixedHeight(operation.gameObject, 30);
            OperationDropdown = HydrogenUi.Dropdown("Hydrogen_Operation", operation, font);
            OperationDropdown.onValueChanged.AddListener(index => EditForm(form => form.operation = Id(state.catalog.operations, index)));
            AddConditionButton = HydrogenUi.Button("Hydrogen_AddCondition", operation, font, "+ 条件项", AddTerm, 96, false, 30);
            mixedRow = HydrogenUi.Row("Hydrogen_ExplicitGrouping", parent, 5);
            Ui.FixedHeight(mixedRow.gameObject, 30);
            LeftOperatorDropdown = HydrogenUi.Dropdown("Hydrogen_LeftOperator", mixedRow, font);
            RightOperatorDropdown = HydrogenUi.Dropdown("Hydrogen_RightOperator", mixedRow, font);
            GroupingDropdown = HydrogenUi.Dropdown("Hydrogen_Grouping", mixedRow, font);
            TacticsUiControls.Options(LeftOperatorDropdown, new[] { "A ? B", "A AND B", "A OR B" });
            TacticsUiControls.Options(RightOperatorDropdown, new[] { "? C", "AND C", "OR C" });
            LeftOperatorDropdown.onValueChanged.AddListener(index => SetOperator(0, index));
            RightOperatorDropdown.onValueChanged.AddListener(index => SetOperator(1, index));
            GroupingDropdown.onValueChanged.AddListener(index => EditForm(form => form.grouping = Id(state.catalog.groupings, index)));
            termList = HydrogenUi.Column("Hydrogen_ConditionTerms", parent, 3);

            var scope = HydrogenUi.Row("Hydrogen_ScopePart", parent, 5);
            Ui.FixedHeight(scope.gameObject, 32);
            PartLabel(scope, "2. 对象范围", 91);
            ScopeDropdown = HydrogenUi.Dropdown("Hydrogen_Scope", scope, font);
            ScopeDropdown.onValueChanged.AddListener(index => EditForm(form => form.scope = Id(state.catalog.scopes, index)));
            var selector = HydrogenUi.Row("Hydrogen_SelectorPart", parent, 5);
            Ui.FixedHeight(selector.gameObject, 32);
            PartLabel(selector, "3. 选择方式", 91);
            SelectorDropdown = HydrogenUi.Dropdown("Hydrogen_Selector", selector, font);
            SelectorDropdown.onValueChanged.AddListener(index => EditForm(form => form.selector = Id(state.catalog.selectors, index)));

            FormSection(parent, "4. 行动选择 · 当前角色白名单");
            var search = HydrogenUi.Row("Hydrogen_WhiteListSearch", parent, 5);
            Ui.FixedHeight(search.gameObject, 32);
            ActionSearchInput = HydrogenUi.Input("Hydrogen_ActionSearch", search, font, 0, "搜索技能名 / 效果");
            ActionSearchInput.onValueChanged.AddListener(query => { if (!refreshing) shared.SetActionSearch(query); });
            skillCategory = HydrogenUi.Dropdown("Hydrogen_ActionCategory", search, font, 88);
            TacticsUiControls.Options(skillCategory, new[] { "全部", "伤害", "支援" });
            skillCategory.onValueChanged.AddListener(_ => { if (!refreshing) { skillSignature = null; RefreshView(); } });
            ClearSearchButton = HydrogenUi.Button("Hydrogen_ClearSearch", search, font, "清空", () => { shared.SetActionSearch(""); skillCategory.SetValueWithoutNotify(0); skillSignature = null; RefreshView(); }, 48, false, 32);
            actionMatch = HydrogenUi.Text("Hydrogen_ActionSearchFeedback", parent, font, "", 12, T.Secondary);
            Ui.FixedHeight(actionMatch.gameObject, 31);
            skillList = HydrogenUi.ScrollColumn("Hydrogen_WhiteListActionCards", parent, out _, 5, 4);
        }

        private void FormSection(Transform parent, string title)
        {
            var label = HydrogenUi.Text("Hydrogen_FourPartHeading", parent, font, title, 14, T.Cyan);
            Ui.FixedHeight(label.gameObject, 22);
        }

        private void PartLabel(Transform parent, string title, float width)
        {
            var label = HydrogenUi.Text("PartLabel", parent, font, title, 14, T.Cyan, true);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Width(label.gameObject, width);
        }

        private void EditForm(Action<TacticsFormInput> mutation)
        {
            if (refreshing || shared == null) return;
            var form = shared.GetState().form;
            mutation(form);
            shared.SetForm(form);
        }

        private void SetOperator(int at, int index)
        {
            EditForm(form =>
            {
                if (form.operators == null || form.operators.Length != 2) form.operators = new[] { "", "" };
                form.operators[at] = index == 1 ? "and" : index == 2 ? "or" : "";
            });
        }

        private void AddTerm()
        {
            EditForm(form => form.terms = (form.terms ?? Array.Empty<TacticsFormTerm>())
                .Concat(new[] { new TacticsFormTerm { kind = "", subject = "", thresholdText = "", value = "" } }).ToArray());
        }

        private void BuildTerm(int index)
        {
            var root = HydrogenUi.Column("Hydrogen_Term_" + index, termList, 2, 3, T.Elevated);
            Ui.FixedHeight(root.gameObject, 60);
            var first = HydrogenUi.Row("ConditionAndSource", root, 4);
            Ui.FixedHeight(first.gameObject, 26);
            var controls = new HydrogenConditionControls();
            controls.SubjectDropdown = HydrogenUi.Dropdown("Hydrogen_TermSubject_" + index, first, font, 92);
            controls.KindDropdown = HydrogenUi.Dropdown("Hydrogen_TermKind_" + index, first, font);
            controls.RemoveButton = HydrogenUi.Button("Hydrogen_RemoveTerm_" + index, first, font, "×", () =>
                EditForm(form => form.terms = form.terms.Where((_, at) => at != index).ToArray()), 24, false, 26);
            controls.SubjectDropdown.onValueChanged.AddListener(selected => EditTerm(index, term => term.subject = Id(state.catalog.subjects, selected)));
            controls.KindDropdown.onValueChanged.AddListener(selected => EditTerm(index, term =>
            {
                term.kind = Id(state.catalog.conditions, selected);
                var option = state.catalog.conditions.FirstOrDefault(value => value.id == term.kind);
                term.thresholdText = option == null || !option.usesThreshold ? "" : option.defaultThreshold.ToString("R", CultureInfo.InvariantCulture);
                term.value = option?.defaultValue ?? "";
            }));
            var values = HydrogenUi.Row("ActualConditionValues", root, 4);
            Ui.FixedHeight(values.gameObject, 24);
            var threshold = HydrogenUi.Row("ThresholdAndUnit", values, 0);
            Ui.Width(threshold.gameObject, 124);
            threshold.gameObject.AddComponent<Image>().color = T.Input;
            controls.ThresholdInput = HydrogenUi.Input("Hydrogen_Threshold_" + index, threshold, font, 88);
            Ui.FixedHeight(controls.ThresholdInput.gameObject, 24);
            controls.UnitText = HydrogenUi.Text("Hydrogen_ThresholdUnit_" + index, threshold, font, "", 12, T.Cyan, true);
            Ui.Width(controls.UnitText.gameObject, 36);
            controls.UnitText.alignment = TextAlignmentOptions.MidlineLeft;
            controls.ValueInput = HydrogenUi.Input("Hydrogen_ConditionValue_" + index, values, font, 0, "状态 / 标签值");
            Ui.FixedHeight(controls.ValueInput.gameObject, 24);
            controls.ThresholdInput.onValueChanged.AddListener(raw => EditTerm(index, term =>
            {
                var option = state.catalog.conditions.FirstOrDefault(value => value.id == term.kind);
                double number;
                term.thresholdText = option != null && option.thresholdUnit == "ratio"
                    && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    ? (number / 100d).ToString("R", CultureInfo.InvariantCulture) : raw;
            }));
            controls.ValueInput.onValueChanged.AddListener(value => EditTerm(index, term => term.value = value));
            terms.Add(controls);
        }

        private void EditTerm(int index, Action<TacticsFormTerm> mutation)
        {
            EditForm(form => { if (index < form.terms.Length) mutation(form.terms[index]); });
        }

        private void RefreshForm()
        {
            var form = state.form;
            formTitle.text = (form.selectedInstructionIndex < 0 ? "新建空白规则" : "编辑指令 #" + (form.selectedInstructionIndex + 1))
                + (form.hasUnsubmittedChanges ? " · 未提交修改 ●" : " · 表单已同步");
            formTitle.color = form.hasUnsubmittedChanges ? T.Amber : T.Gold;
            SetText(LabelInput, form.label);
            SetOptions(OperationDropdown, state.catalog.operations, "选择组合方式", form.operation);
            SetOptions(GroupingDropdown, state.catalog.groupings, "明确括号", form.grouping);
            LeftOperatorDropdown.SetValueWithoutNotify(BinaryIndex(form.operators, 0));
            RightOperatorDropdown.SetValueWithoutNotify(BinaryIndex(form.operators, 1));
            mixedRow.gameObject.SetActive(form.operation == "mixed");
            var inputs = form.terms ?? Array.Empty<TacticsFormTerm>();
            if (terms.Count != inputs.Length)
            {
                HydrogenUi.Clear(termList);
                terms.Clear();
                for (int i = 0; i < inputs.Length; i++) BuildTerm(i);
            }
            for (int i = 0; i < terms.Count; i++)
            {
                var term = inputs[i];
                var controls = terms[i];
                SetOptions(controls.KindDropdown, state.catalog.conditions, "条件 " + (char)('A' + i), term.kind);
                SetOptions(controls.SubjectDropdown, state.catalog.subjects, "来源", term.subject);
                var option = state.catalog.conditions.FirstOrDefault(value => value.id == term.kind);
                string threshold = term.thresholdText;
                double value;
                if (option?.thresholdUnit == "ratio" && double.TryParse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    threshold = (value * 100).ToString("0.#########", CultureInfo.InvariantCulture);
                SetText(controls.ThresholdInput, threshold);
                SetText(controls.ValueInput, term.value);
                controls.ThresholdInput.interactable = option != null && option.usesThreshold;
                controls.ValueInput.interactable = option != null && option.usesValue;
                // Conditions without a value have no inactive blue tail after the unit.
                // The shared raw value is still retained when the field is not displayed.
                controls.ValueInput.gameObject.SetActive(option != null && option.usesValue);
                controls.UnitText.text = option == null || !option.usesThreshold ? "—" : UnitLabel(option.thresholdUnit);
            }
            AddConditionButton.interactable = inputs.Length < state.catalog.conditionCapacity;
            HydrogenUi.ButtonTitle(AddConditionButton, "+ 条件 " + inputs.Length + "/" + state.catalog.conditionCapacity);
            SetOptions(ScopeDropdown, state.catalog.scopes, "选择对象范围", form.scope);
            SetOptions(SelectorDropdown, state.catalog.selectors, "选择目标方式", form.selector);
            SetText(ActionSearchInput, form.actionSearch);
            actionMatch.text = state.matchingActionCount == 0 && !string.IsNullOrEmpty(form.actionSearch)
                ? "白名单内无匹配技能；清空搜索可恢复。" + (state.selectedActionRetained ? "已选技能保留，不改变草稿。" : "")
                : state.actionMatchText;
            RefreshSkills();
        }

        private void RefreshSkills()
        {
            var key = state.workbench.draftScenario.id + "|" + state.workbench.draftRevision + "|" + state.form.selectedUnitId + "|" + state.form.actionSearch + "|" + state.form.actionId + "|" + skillCategory.value
                + "|" + state.form.scope + "|" + state.form.selector;
            if (key == skillSignature) return;
            skillSignature = key;
            HydrogenUi.Clear(skillList);
            skillButtons.Clear();
            var selected = state.catalog.actions.FirstOrDefault(action => action.id == state.form.actionId);
            actionDetails = HydrogenUi.Text("Hydrogen_SelectedActionDetails", skillList, font, DescribeAction(selected), 13, T.Cyan);
            actionDetails.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var visible = state.visibleActions ?? Array.Empty<CombatActionConfig>();
            if (skillCategory.value == 1) visible = visible.Where(action => action.effectKind == "damage").ToArray();
            else if (skillCategory.value == 2) visible = visible.Where(action => action.effectKind != "damage").ToArray();
            if (visible.Length == 0 || (state.matchingActionCount == 0 && !string.IsNullOrEmpty(state.form.actionSearch)))
            {
                var empty = HydrogenUi.Text("Hydrogen_EmptySearch", skillList, font,
                    "当前角色白名单内没有匹配技能。\n清空搜索或切回「全部」可恢复列表。", 14, T.Secondary);
                Ui.FixedHeight(empty.gameObject, 60);
            }
            foreach (var action in visible)
            {
                var id = action.id;
                var initial = state.workbench.draftScenario.units.First(unit => unit.id == state.form.selectedUnitId).mp;
                var button = HydrogenUi.Button("Hydrogen_Action_" + id, skillList, font,
                    action.displayName + (state.selectedActionRetained && id == state.form.actionId ? "（已选保留）" : "") + " · " + ModeLabel(action.targetMode) + "\n" + action.mpCost + " MP · " + HydrogenBattleText.N(action.baseWait)
                    + " WAIT" + (action.mpCost > initial ? " · >初始MP" : ""),
                    () => EditForm(form => form.actionId = id), 0, false, 58);
                var label = button.GetComponentInChildren<TMP_Text>();
                label.textWrappingMode = TextWrappingModes.Normal;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                if (id == state.form.actionId) button.GetComponent<Outline>().effectColor = T.Cyan;
                skillButtons.Add(id, button);
            }
        }

        private string DescribeAction(CombatActionConfig action)
        {
            if (action == null) return "请选择一个白名单行动。搜索只浏览目录，不改草稿。";
            var effect = state.catalog.effectKinds.FirstOrDefault(option => option.id == action.effectKind)?.label ?? action.effectKind;
            var text = action.displayName + " · " + effect + " · " + ModeLabel(action.targetMode) + "\nMP " + action.mpCost
                + " · 基础 WAIT " + HydrogenBattleText.N(action.baseWait) + " · " + (action.isMagic ? "魔法动作" : "非魔法动作");
            if (action.effectKind == "damage" || action.effectKind == "heal" || action.effectKind == "restore_mp" || action.effectKind == "drain_mp")
                text += " · 基础量 " + action.magnitude;
            if (action.effectKind == "damage") text += "\n伤害类型："
                + (state.catalog.damageTypes.FirstOrDefault(option => option.id == action.damageType)?.label ?? action.damageType);
            if (action.effectKind == "apply_status") text += "\n" + HydrogenBattleText.StatusName(action.statusKind) + " · 强度 "
                + action.statusMagnitude + " · 持续 " + HydrogenBattleText.N(action.statusDuration) + " WAIT";
            return text + "\n先选锚点，再按范围扩展；覆盖者不重复筛条件，费用与 WAIT 各结算一次。";
        }

        private string ModeLabel(string mode)
        {
            string id = string.IsNullOrEmpty(mode) ? "single" : mode;
            return state.catalog.targetModes.FirstOrDefault(option => option.id == id)?.label ?? id;
        }

        private static void SetText(TMP_InputField input, string value)
        {
            if (!input.isFocused && input.text != (value ?? "")) input.SetTextWithoutNotify(value ?? "");
        }

        private static string Id(CombatTacticsOption[] options, int index)
        {
            return index > 0 && index <= options.Length ? options[index - 1].id : "";
        }

        private static void SetOptions(TMP_Dropdown dropdown, CombatTacticsOption[] options, string hint, string selected)
        {
            var labels = new[] { hint }.Concat(options.Select(option => option.label)).ToArray();
            int index = Array.FindIndex(options, option => option.id == selected) + 1;
            if (!dropdown.options.Select(option => option.text).SequenceEqual(labels)) TacticsUiControls.Options(dropdown, labels, index);
            else dropdown.SetValueWithoutNotify(index);
        }

        private static int BinaryIndex(string[] operators, int index)
        {
            string id = operators != null && index < operators.Length ? operators[index] : "";
            return id == "and" ? 1 : id == "or" ? 2 : 0;
        }

        private static string UnitLabel(string unit)
        {
            switch (unit) { case "ratio": return "%"; case "points": return "点"; case "count": return "人"; case "wait": return "WAIT"; case "row": return "排"; default: return unit ?? "数值"; }
        }
    }
}
