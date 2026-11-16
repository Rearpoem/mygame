using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Opus.OpusTheme;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    public sealed class OpusConditionControls
    {
        public Button KindButton { get; internal set; }
        public TMP_Dropdown SubjectDropdown { get; internal set; }
        public TMP_InputField ThresholdInput { get; internal set; }
        public TMP_InputField ValueInput { get; internal set; }
        public Button ValuePicker { get; internal set; }
        public TMP_Text UnitText { get; internal set; }
        public Button RemoveButton { get; internal set; }
    }

    public sealed partial class OpusCandidateView
    {
        private int activeSegment;
        private RectTransform[] segmentPanels;
        private RectTransform conditionList;
        private RectTransform mixedControls;
        private RectTransform scopeOptions;
        private readonly List<OpusConditionControls> terms = new List<OpusConditionControls>();
        private readonly Dictionary<string, Button> scopes = new Dictionary<string, Button>();
        private TMP_Text expression, scopeExplanation, selectorExplanation, selectedActionExplanation;
        private TMP_Dropdown operation, grouping, leftOperator, rightOperator;
        private Button selectorPicker;
        private string scopeKey;
        public Button[] SegmentButtons { get; private set; }
        public TMP_InputField LabelInput { get; private set; }
        public Button AddConditionButton { get; private set; }
        public Button ReplaceButton { get; private set; }
        public Button InsertButton { get; private set; }
        public Button DiscardButton { get; private set; }
        public IReadOnlyList<OpusConditionControls> Terms { get { return terms; } }
        public OpusCatalogPopup CatalogPopup { get { return popup; } }
        public int ActiveSegment { get { return activeSegment; } }

        private void BuildForm(RectTransform parent)
        {
            var header = OpusUi.Row("Opus_FormHead", parent); Ui.FixedHeight(header.gameObject, 28);
            formState = OpusUi.Text("Opus_FormStatus", header, font, "", 13, T.Amber, true); Ui.Width(formState.gameObject, 180);
            LabelInput = OpusUi.Input("Opus_RuleLabel", header, font, 0, "规则标题");
            LabelInput.onValueChanged.AddListener(raw => Mutate(form => form.label = raw));
            var strip = OpusUi.Row("Opus_FourSegmentsAlwaysVisible", parent, 4); Ui.FixedHeight(strip.gameObject, 60);
            SegmentButtons = new Button[4];
            var labels = new[] { "① 条件表达式", "② 对象范围", "③ 选择方式", "④ 行动" };
            for (int i = 0; i < 4; i++)
            {
                int at = i;
                SegmentButtons[i] = OpusUi.Button("Opus_Segment_" + i, strip, font, labels[i] + "\n未选择", () => { activeSegment = at; pendingRefresh = true; }, 0, 56);
                SegmentButtons[i].GetComponentInChildren<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
                if (i < 3)
                {
                    var arrow = OpusUi.Text("Then", strip, font, "→", 13, T.Secondary, true); Ui.Width(arrow.gameObject, 13);
                    arrow.alignment = TextAlignmentOptions.Center;
                }
            }
            var frame = OpusUi.Rect("Opus_ActiveSegmentDetail", parent); Ui.Flexible(frame.gameObject); frame.GetComponent<LayoutElement>().minHeight = 150;
            segmentPanels = new RectTransform[4];
            for (int i = 0; i < 4; i++) { segmentPanels[i] = OpusUi.Rect("SegmentDetail" + i, frame); Ui.Stretch(segmentPanels[i]); }
            var conditions = OpusUi.Scroll("Opus_ConditionsScroll", segmentPanels[0], out _); Ui.Stretch(conditions.parent.parent.GetComponent<RectTransform>());
            var groupingRow = OpusUi.Row("Opus_ConditionGrouping", conditions, 4); Ui.FixedHeight(groupingRow.gameObject, 32);
            operation = OpusUi.Dropdown("Opus_Operation", groupingRow, font);
            operation.onValueChanged.AddListener(index => Mutate(form => form.operation = Id(state.catalog.operations, index)));
            AddConditionButton = OpusUi.Button("Opus_AddCondition", groupingRow, font, "+ 条件项", AddTerm, 124, 32);
            mixedControls = OpusUi.Row("Opus_MixedBrackets", conditions, 4); Ui.FixedHeight(mixedControls.gameObject, 32);
            leftOperator = OpusUi.Dropdown("Opus_LeftOperator", mixedControls, font);
            rightOperator = OpusUi.Dropdown("Opus_RightOperator", mixedControls, font);
            grouping = OpusUi.Dropdown("Opus_Grouping", mixedControls, font);
            TacticsUiControls.Options(leftOperator, new[] { "A ? B", "A 且 B", "A 或 B" });
            TacticsUiControls.Options(rightOperator, new[] { "? C", "且 C", "或 C" });
            leftOperator.onValueChanged.AddListener(index => SetOperator(0, index)); rightOperator.onValueChanged.AddListener(index => SetOperator(1, index));
            grouping.onValueChanged.AddListener(index => Mutate(form => form.grouping = Id(state.catalog.groupings, index)));
            conditionList = OpusUi.Column("Opus_ConditionRows", conditions, 4, 0);
            expression = OpusUi.Text("Opus_ExpressionPreview", conditions, font, "", 13, T.Secondary); OpusUi.ContentText(expression);
            scopeOptions = OpusUi.Column("Opus_ScopeChoices", segmentPanels[1], 8, 4); Ui.Stretch(scopeOptions);
            var selector = OpusUi.Column("Opus_SelectorDetail", segmentPanels[2], 8, 4); Ui.Stretch(selector);
            selectorPicker = OpusUi.Button("Opus_SelectorPicker", selector, font, "选择方式 · 搜索目录", () => popup.Open(state.catalog.selectors,
                id => Mutate(form => form.selector = id)));
            selectorExplanation = OpusUi.Text("Opus_AnchorExplanation", selector, font, "", 15, T.Secondary); Ui.Flexible(selectorExplanation.gameObject);
            var action = OpusUi.Column("Opus_ActionSegment", segmentPanels[3], 8, 4); Ui.Stretch(action);
            selectedActionExplanation = OpusUi.ScrollText("Opus_ChosenAction", action, font, out _, 15);
            OpusUi.Button("Opus_FocusCodex", action, font, "去右侧技能目录选择", () => ActionSearchInput.ActivateInputField(), 240, 34);
            var commands = OpusUi.Row("Opus_FormWrites", parent, 6); Ui.FixedHeight(commands.gameObject, 36);
            DiscardButton = OpusUi.Button("Opus_DiscardForm", commands, font, "放弃表单修改", () => shared.LoadSelected(), 164, 34);
            InsertButton = OpusUi.Button("Opus_Insert", commands, font, "新增到末尾", () => shared.Insert(), 150, 34);
            ReplaceButton = OpusUi.Button("Opus_Replace", commands, font, "应用替换", () => shared.Replace(), 130, 34);
        }

        private void AddTerm() { Mutate(form => form.terms = (form.terms ?? Array.Empty<TacticsFormTerm>()).Concat(new[] { new TacticsFormTerm { kind = "", subject = "", thresholdText = "", value = "" } }).ToArray()); }
        private void SetOperator(int at, int index) { Mutate(form => { if (form.operators == null || form.operators.Length != 2) form.operators = new[] { "", "" }; form.operators[at] = index == 1 ? "and" : index == 2 ? "or" : ""; }); }
        private void EditTerm(int at, Action<TacticsFormTerm> action) { Mutate(form => { if (at < form.terms.Length) action(form.terms[at]); }); }
        private void BuildTerm(int index)
        {
            var row = OpusUi.Row("Opus_Condition_" + index, conditionList, 4); Ui.FixedHeight(row.gameObject, 36);
            var controls = new OpusConditionControls();
            controls.SubjectDropdown = OpusUi.Dropdown("Opus_Subject_" + index, row, font, 84);
            controls.SubjectDropdown.onValueChanged.AddListener(at => EditTerm(index, term => term.subject = Id(state.catalog.subjects, at)));
            controls.KindButton = OpusUi.Button("Opus_Kind_" + index, row, font, "选择条件类别", () => popup.Open(state.catalog.conditions, id => EditTerm(index, term =>
            {
                term.kind = id; var option = state.catalog.conditions.First(item => item.id == id);
                term.thresholdText = option.usesThreshold ? option.defaultThreshold.ToString("R", CultureInfo.InvariantCulture) : "";
                term.value = option.defaultValue ?? "";
            })), 0, 32);
            controls.ThresholdInput = OpusUi.Input("Opus_Threshold_" + index, row, font, 66);
            controls.ThresholdInput.onValueChanged.AddListener(raw => EditTerm(index, term =>
            {
                var option = state.catalog.conditions.FirstOrDefault(item => item.id == term.kind); double number;
                term.thresholdText = option?.thresholdUnit == "ratio" && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number)
                    ? (number / 100d).ToString("R", CultureInfo.InvariantCulture) : raw;
            }));
            controls.UnitText = OpusUi.Text("Opus_Unit_" + index, row, font, "", 12, T.Secondary, true); Ui.Width(controls.UnitText.gameObject, 36);
            controls.UnitText.alignment = TextAlignmentOptions.MidlineLeft;
            controls.ValueInput = OpusUi.Input("Opus_Value_" + index, row, font, 122, "状态ID / 标签");
            controls.ValueInput.onValueChanged.AddListener(value => EditTerm(index, term => term.value = value));
            controls.ValuePicker = OpusUi.Button("Opus_StatusKind_" + index, row, font, "状态类别", () => popup.Open(state.catalog.statusKinds, id => EditTerm(index, term => term.value = id)), 124, 32);
            controls.RemoveButton = OpusUi.Button("Opus_RemoveTerm_" + index, row, font, "×", () => Mutate(form => form.terms = form.terms.Where((_, at) => at != index).ToArray()), 28, 32);
            terms.Add(controls);
        }

        private void RenderForm()
        {
            var form = state.form;
            formState.text = (form.selectedInstructionIndex < 0 ? "新规则（空白）" : "编辑 #" + (form.selectedInstructionIndex + 1)) + (form.hasUnsubmittedChanges ? " · ● 未写入草稿" : " · ○ 未修改");
            SetText(LabelInput, form.label);
            var action = state.catalog.actions.FirstOrDefault(item => item.id == form.actionId);
            var values = new[] { ConditionSummary(), OptionLabel(state.catalog.scopes, form.scope), OptionLabel(state.catalog.selectors, form.selector), action?.displayName ?? "未选择" };
            var names = new[] { "① 条件表达式", "② 对象范围", "③ 选择方式", "④ 行动" };
            for (int i = 0; i < 4; i++)
            {
                OpusUi.Title(SegmentButtons[i], names[i] + "\n" + values[i]);
                SegmentButtons[i].GetComponent<Image>().color = activeSegment == i ? T.Raised : T.Panel;
                SegmentButtons[i].GetComponentInChildren<TMP_Text>().color = activeSegment == i ? T.Green : T.Ink;
                segmentPanels[i].gameObject.SetActive(activeSegment == i);
            }
            SetOptions(operation, state.catalog.operations, "分组方式", form.operation);
            SetOptions(grouping, state.catalog.groupings, "明确括号", form.grouping);
            mixedControls.gameObject.SetActive(form.operation == "mixed");
            leftOperator.SetValueWithoutNotify(Binary(form.operators, 0)); rightOperator.SetValueWithoutNotify(Binary(form.operators, 1));
            var input = form.terms ?? Array.Empty<TacticsFormTerm>();
            if (terms.Count != input.Length) { OpusUi.Clear(conditionList); terms.Clear(); for (int i = 0; i < input.Length; i++) BuildTerm(i); }
            for (int i = 0; i < input.Length; i++)
            {
                var term = input[i]; var controls = terms[i]; var option = state.catalog.conditions.FirstOrDefault(item => item.id == term.kind);
                SetOptions(controls.SubjectDropdown, state.catalog.subjects, "来源", term.subject);
                OpusUi.Title(controls.KindButton, option?.label ?? "搜索条件类别");
                if (i >= state.catalog.conditionCapacity) OpusUi.Title(controls.KindButton, "超出容量 · " + (option?.label ?? "未选择"));
                controls.KindButton.GetComponent<Image>().color = i >= state.catalog.conditionCapacity ? new Color32(58, 26, 26, 255) : T.Raised;
                string threshold = term.thresholdText; double parsed;
                if (option?.thresholdUnit == "ratio" && double.TryParse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && !double.IsNaN(parsed) && !double.IsInfinity(parsed)) threshold = (parsed * 100).ToString("0.#########", CultureInfo.InvariantCulture);
                SetText(controls.ThresholdInput, threshold); SetText(controls.ValueInput, term.value);
                controls.ThresholdInput.gameObject.SetActive(option != null && option.usesThreshold);
                controls.UnitText.gameObject.SetActive(option != null && option.usesThreshold);
                controls.UnitText.text = Unit(option?.thresholdUnit);
                bool statusKind = term.kind == "has_status_kind" || term.kind == "lacks_status_kind";
                controls.ValueInput.gameObject.SetActive(option != null && option.usesValue && !statusKind);
                controls.ValuePicker.gameObject.SetActive(option != null && option.usesValue && statusKind);
                OpusUi.Title(controls.ValuePicker, OptionLabel(state.catalog.statusKinds, term.value));
            }
            AddConditionButton.interactable = input.Length < state.catalog.conditionCapacity;
            OpusUi.Title(AddConditionButton, "+ 条件 " + input.Length + "/" + state.catalog.conditionCapacity);
            expression.text = "表达式：" + ConditionSummary(true);
            RenderScopes();
            OpusUi.Title(selectorPicker, OptionLabel(state.catalog.selectors, form.selector) + " [搜索]");
            selectorExplanation.text = "在「" + OptionLabel(state.catalog.scopes, form.scope) + "」的合格候选中，以「" + OptionLabel(state.catalog.selectors, form.selector)
                + "」选出锚点；行动范围决定实际覆盖。\n\n" + (state.catalog.selectors.FirstOrDefault(item => item.id == form.selector)?.description ?? "请选择目录中的一种选法。");
            selectedActionExplanation.text = DescribeAction(action);
            var selectedUnit = state.workbench.draftPreview.units.First(unit => unit.id == form.selectedUnitId);
            InsertButton.interactable = selectedUnit.instructions.Length < selectedUnit.instructionCapacity;
            ReplaceButton.interactable = form.selectedInstructionIndex >= 0;
        }
        private void RenderScopes()
        {
            string key = state.workbench.draftScenario.id + ":" + state.form.selectedUnitId;
            if (scopeKey != key)
            {
                scopeKey = key; OpusUi.Clear(scopeOptions); scopes.Clear();
                foreach (var option in state.catalog.scopes) { string id = option.id; scopes[id] = OpusUi.Button("Opus_Scope_" + id, scopeOptions, font, option.label, () => Mutate(form => form.scope = id), 0, 32); }
                scopeExplanation = OpusUi.Text("Opus_AlliesIncludeSelf", scopeOptions, font, "", 13, T.Secondary); Ui.Flexible(scopeExplanation.gameObject);
            }
            foreach (var pair in scopes)
            {
                pair.Value.GetComponent<Image>().color = state.form.scope == pair.Key ? T.Raised : T.Panel;
                pair.Value.GetComponentInChildren<TMP_Text>().color = state.form.scope == pair.Key ? T.Green : T.Ink;
            }
            scopeExplanation.text = "己方范围" + (state.workbench.draftScenario.alliesIncludeSelf ? "包含自身。" : "不包含自身。") + "\n" + (state.catalog.scopes.FirstOrDefault(item => item.id == state.form.scope)?.description ?? "请选择对象范围。");
        }
        private string ConditionSummary(bool full = false)
        {
            var terms = state.form.terms ?? Array.Empty<TacticsFormTerm>(); if (terms.Length == 0) return "未选择";
            var parts = terms.Select(term => OptionLabel(state.catalog.subjects, term.subject) + "·" + OptionLabel(state.catalog.conditions, term.kind)
                + (string.IsNullOrEmpty(term.thresholdText) ? "" : " " + ThresholdLabel(term)) + (string.IsNullOrEmpty(term.value) ? "" : " [" + term.value + "]")).ToArray();
            if (state.form.operation == "mixed" && parts.Length == 3)
            {
                string a = BinaryText(state.form.operators, 0), b = BinaryText(state.form.operators, 1);
                return state.form.grouping == "left" ? "(" + parts[0] + a + parts[1] + ")" + b + parts[2]
                    : state.form.grouping == "right" ? parts[0] + a + "(" + parts[1] + b + parts[2] + ")" : string.Join(" ? ", parts) + "（括号未指定）";
            }
            return full ? string.Join(state.form.operation == "any" ? " 或 " : state.form.operation == "all" ? " 且 " : " ? ", parts) : parts[0] + (parts.Length > 1 ? " +" + (parts.Length - 1) : "");
        }
        private string ThresholdLabel(TacticsFormTerm term)
        {
            var option = state.catalog.conditions.FirstOrDefault(item => item.id == term.kind); double number;
            return option?.thresholdUnit == "ratio" && double.TryParse(term.thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                ? OpusUi.N(number * 100) + "%" : term.thresholdText + " " + Unit(option?.thresholdUnit);
        }
        private void InvalidateForm() { scopeKey = null; codexKey = null; }
        private static void SetText(TMP_InputField input, string text) { if (!input.isFocused && input.text != (text ?? "")) input.SetTextWithoutNotify(text ?? ""); }
        private static string Unit(string unit) { return unit == "ratio" ? "%" : unit == "points" ? "点" : unit == "count" ? "人" : unit == "row" ? "排" : unit ?? ""; }
        private static string Id(CombatTacticsOption[] options, int index) { return index > 0 && index <= options.Length ? options[index - 1].id : ""; }
        private static string OptionLabel(CombatTacticsOption[] options, string id) { return options.FirstOrDefault(item => item.id == id)?.label ?? "未选择"; }
        private static void SetOptions(TMP_Dropdown dropdown, CombatTacticsOption[] options, string hint, string id)
        {
            var labels = new[] { hint }.Concat(options.Select(item => item.label)).ToArray(); int at = Array.FindIndex(options, item => item.id == id) + 1;
            if (!dropdown.options.Select(item => item.text).SequenceEqual(labels)) TacticsUiControls.Options(dropdown, labels, at); else dropdown.SetValueWithoutNotify(at);
        }
        private static int Binary(string[] ops, int at) { string id = ops != null && at < ops.Length ? ops[at] : ""; return id == "and" ? 1 : id == "or" ? 2 : 0; }
        private static string BinaryText(string[] ops, int at) { int index = Binary(ops, at); return index == 1 ? " 且 " : index == 2 ? " 或 " : " ? "; }
    }
}
