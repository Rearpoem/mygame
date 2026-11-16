using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Prototype.Combat;
using Prototype.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    public sealed class TacticsConditionControls
    {
        public RectTransform Root { get; internal set; }
        public TMP_Dropdown KindDropdown { get; internal set; }
        public TMP_Dropdown SubjectDropdown { get; internal set; }
        public TMP_InputField ThresholdInput { get; internal set; }
        public TMP_InputField ValueInput { get; internal set; }
        public TMP_Text DescriptionText { get; internal set; }
        public TMP_Text ValueLabel { get; internal set; }
        public TMP_Text ThresholdLabel { get; internal set; }
        public TMP_Text TitleText { get; internal set; }
        public Button RemoveButton { get; internal set; }
    }

    /// <summary>Edits DTO fields only. Validation and capacity decisions stay in the workbench/kernel.</summary>
    public sealed class TacticsInstructionEditor : MonoBehaviour
    {
        private TMP_FontAsset font;
        private TacticsWorkbenchSession workbench;
        private TacticsCatalogSnapshot catalog;
        private readonly List<TacticsConditionControls> terms = new List<TacticsConditionControls>();
        private readonly List<CombatActionConfig> visibleActions = new List<CombatActionConfig>();
        private string selectedActionId = "";
        private CombatTacticsOption[] targetModes = Array.Empty<CombatTacticsOption>();
        private CombatTacticsOption[] effectKinds = Array.Empty<CombatTacticsOption>();
        private CombatTacticsOption[] damageTypes = Array.Empty<CombatTacticsOption>();
        private CombatTacticsOption[] statusKinds = Array.Empty<CombatTacticsOption>();
        private string[] unitIds = Array.Empty<string>();
        private string selectedUnitId;
        private int selectedInstructionIndex = -1;
        private bool refreshing;
        private string draftId;
        private RectTransform termContainer;
        private RectTransform mixedControls;
        private TMP_Text selectedRuleText;
        private TMP_Text capacityText;
        private TMP_Text expressionText;
        private TMP_Text draftPreviewText;

        public string SelectedUnitId { get { return selectedUnitId; } }
        public int SelectedInstructionIndex { get { return selectedInstructionIndex; } }
        public IReadOnlyList<TacticsConditionControls> Terms { get { return terms; } }
        public TacticsCatalogSnapshot Catalog { get { return catalog; } }
        public TMP_Dropdown UnitDropdown { get; private set; }
        public TMP_Dropdown InstructionDropdown { get; private set; }
        public TMP_Dropdown OperationDropdown { get; private set; }
        public TMP_Dropdown GroupingDropdown { get; private set; }
        public TMP_Dropdown LeftOperatorDropdown { get; private set; }
        public TMP_Dropdown RightOperatorDropdown { get; private set; }
        public TMP_Dropdown ScopeDropdown { get; private set; }
        public TMP_Dropdown SelectorDropdown { get; private set; }
        public TMP_Dropdown ActionDropdown { get; private set; }
        public TMP_InputField ActionSearchInput { get; private set; }
        public TMP_Text ActionDetailsText { get; private set; }
        public TMP_Text ActionMatchText { get; private set; }
        public IReadOnlyList<CombatActionConfig> VisibleActions { get { return visibleActions.AsReadOnly(); } }
        public string SelectedActionId { get { return selectedActionId; } }
        public int MatchingActionCount { get; private set; }
        public bool SelectedActionRetained { get; private set; }
        public CombatActionConfig SelectedAction { get { return catalog == null ? null : catalog.actions.FirstOrDefault(action => action.id == selectedActionId); } }
        public TMP_InputField LabelInput { get; private set; }
        public Button NewBlankButton { get; private set; }
        public Button InsertButton { get; private set; }
        public Button ReplaceButton { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button MoveUpButton { get; private set; }
        public Button MoveDownButton { get; private set; }
        public Button LoadSelectedButton { get; private set; }
        public Button AddConditionButton { get; private set; }
        public ScrollRect FormScroll { get; private set; }
        public ScrollRect SelectedRuleScroll { get; private set; }
        public TMP_Text CapacityText { get { return capacityText; } }
        public TMP_Text SelectedRuleText { get { return selectedRuleText; } }
        public TMP_Text ExpressionText { get { return expressionText; } }
        public TMP_Text DraftPreviewText { get { return draftPreviewText; } }
        public bool HasFormChanges { get; private set; }
        public event Action NewBlankRequested;
        public event Action InsertRequested;
        public event Action ReplaceRequested;
        public event Action DeleteRequested;
        public event Action<int> MoveRequested;
        public event Action SelectionChanged;
        public event Action FormEdited;

        public void Initialize(TMP_FontAsset uiFont, TacticsWorkbenchSession session)
        {
            font = uiFont;
            workbench = session;
            BuildInterface();
            RefreshSelection(true);
        }

        public void BindWorkbench(TacticsWorkbenchSession session)
        {
            workbench = session;
            selectedUnitId = null;
            selectedInstructionIndex = -1;
            RefreshSelection(true);
        }

        private void BuildInterface()
        {
            var unitRow = Ui.Row("TacticsUnitRow", transform, 6);
            Ui.FixedHeight(unitRow.gameObject, 36);
            Ui.Label("TacticsUnitLabel", unitRow, font, "编排队员", 82);
            UnitDropdown = TacticsUiControls.Dropdown("Tactics_Unit", unitRow, font);
            UnitDropdown.onValueChanged.AddListener(_ =>
            {
                if (refreshing) return;
                selectedUnitId = unitIds[UnitDropdown.value];
                selectedInstructionIndex = -1;
                RefreshSelection(true);
                SelectionChanged?.Invoke();
            });
            capacityText = Ui.Text("TacticsCapacity", transform, font, "", 16, Ui.Accent);
            Ui.FixedHeight(capacityText.gameObject, 51);
            InstructionDropdown = TacticsUiControls.Dropdown("Tactics_Instruction", transform, font);
            InstructionDropdown.onValueChanged.AddListener(index =>
            {
                if (refreshing) return;
                selectedInstructionIndex = index - 1;
                RefreshSelectedRule();
                if (selectedInstructionIndex >= 0) LoadSelectedInstruction();
                SelectionChanged?.Invoke();
            });
            selectedRuleText = Ui.ScrollText("TacticsSelectedRule", transform, font, out var selectedScroll, 16);
            selectedRuleText.margin = new Vector4(4, 4, 4, 4);
            SelectedRuleScroll = selectedScroll;
            Ui.FixedHeight(selectedScroll.gameObject, 72);

            var commands = Ui.Row("TacticsEditCommands", transform, 5);
            Ui.FixedHeight(commands.gameObject, 35);
            NewBlankButton = Ui.Button("Tactics_NewBlank", commands, font, "新建空白", () => NewBlankRequested?.Invoke(), 98, true);
            InsertButton = Ui.Button("Tactics_Insert", commands, font, "新增到末尾", () => InsertRequested?.Invoke(), 108);
            ReplaceButton = Ui.Button("Tactics_Replace", commands, font, "应用替换", () => ReplaceRequested?.Invoke(), 105);
            DeleteButton = Ui.Button("Tactics_Delete", commands, font, "删除", () => DeleteRequested?.Invoke(), 73);
            var ordering = Ui.Row("TacticsOrderCommands", transform, 7);
            Ui.FixedHeight(ordering.gameObject, 34);
            MoveUpButton = Ui.Button("Tactics_MoveUp", ordering, font, "上移 ↑", () => MoveRequested?.Invoke(-1), 96);
            MoveDownButton = Ui.Button("Tactics_MoveDown", ordering, font, "下移 ↓", () => MoveRequested?.Invoke(1), 96);
            LoadSelectedButton = Ui.Button("Tactics_LoadSelected", ordering, font, "重载所选到草稿", LoadSelectedInstruction, 162);

            var content = TacticsUiControls.ScrollColumn("TacticsForm", transform, out var formScroll);
            FormScroll = formScroll;
            TacticsUiControls.Heading("TacticsDraftTitle", content, font, "完整四段草稿 · 新增前请逐项选择");
            LabelInput = Ui.Input("Tactics_Label", content, font, 0);
            Ui.Flexible(LabelInput.gameObject, 1, 0);
            Ui.FixedHeight(LabelInput.gameObject, 36);
            LabelInput.contentType = TMP_InputField.ContentType.Standard;
            LabelInput.onValueChanged.AddListener(_ => FormChanged());
            TacticsUiControls.Heading("TacticsConditionHeading", content, font, "1  条件表达式");
            OperationDropdown = TacticsUiControls.Dropdown("Tactics_Operation", content, font);
            OperationDropdown.onValueChanged.AddListener(_ => FormChanged());
            termContainer = Ui.Column("TacticsConditionTerms", content, 8);
            AddConditionButton = Ui.Button("Tactics_AddCondition", content, font, "+ 新增条件项", AddCondition, 176);
            Ui.FixedHeight(AddConditionButton.gameObject, 35);
            mixedControls = Ui.Column("TacticsMixedGrouping", content, 6, 7, Ui.Card);
            TacticsUiControls.Heading("TacticsMixedHeading", mixedControls, font, "混合表达式 · 必须明确括号");
            LeftOperatorDropdown = TacticsUiControls.Dropdown("Tactics_OperatorAB", mixedControls, font);
            RightOperatorDropdown = TacticsUiControls.Dropdown("Tactics_OperatorBC", mixedControls, font);
            GroupingDropdown = TacticsUiControls.Dropdown("Tactics_Grouping", mixedControls, font);
            LeftOperatorDropdown.onValueChanged.AddListener(_ => FormChanged());
            RightOperatorDropdown.onValueChanged.AddListener(_ => FormChanged());
            GroupingDropdown.onValueChanged.AddListener(_ => FormChanged());
            expressionText = Ui.Text("TacticsExpression", content, font, "", 16, Ui.Accent);
            TacticsUiControls.Heading("TacticsScopeHeading", content, font, "2  对象范围");
            ScopeDropdown = TacticsUiControls.Dropdown("Tactics_Scope", content, font);
            TacticsUiControls.Heading("TacticsSelectorHeading", content, font, "3  选择方式");
            SelectorDropdown = TacticsUiControls.Dropdown("Tactics_Selector", content, font);
            TacticsUiControls.Heading("TacticsActionHeading", content, font, "4  行动（仅当前角色已开放动作）");
            var searchRow = Ui.Row("TacticsActionSearchRow", content, 6);
            Ui.FixedHeight(searchRow.gameObject, 36);
            Ui.Label("TacticsActionSearchLabel", searchRow, font, "查找技能", 82);
            ActionSearchInput = Ui.Input("Tactics_ActionSearch", searchRow, font, 0);
            Ui.Flexible(ActionSearchInput.gameObject, 1, 0);
            ActionSearchInput.contentType = TMP_InputField.ContentType.Standard;
            ActionSearchInput.onValueChanged.AddListener(_ => { if (!refreshing) RefreshActionOptions(); });
            ActionDropdown = TacticsUiControls.Dropdown("Tactics_Action", content, font);
            ActionMatchText = Ui.Text("TacticsActionMatches", content, font, "", 15, Ui.Muted);
            ActionDetailsText = Ui.Text("TacticsActionDetails", content, font, "", 16, Ui.Ink);
            ActionDetailsText.margin = new Vector4(3, 3, 3, 3);
            ScopeDropdown.onValueChanged.AddListener(_ => FormChanged());
            SelectorDropdown.onValueChanged.AddListener(_ => FormChanged());
            ActionDropdown.onValueChanged.AddListener(_ =>
            {
                if (refreshing) return;
                selectedActionId = ActionDropdown.value > 0 && ActionDropdown.value <= visibleActions.Count
                    ? visibleActions[ActionDropdown.value - 1].id : "";
                RefreshActionDetails();
                FormChanged();
            });
            draftPreviewText = Ui.Text("TacticsDraftPreview", content, font, "", 16, Ui.Good);
            var hint = Ui.Text("TacticsFormHint", content, font,
                "条件默认逐个候选对象读取；选「施放者」才读施放者自身。数值范围、技能费用与可选项均来自本轮目录。滚轮查看完整表单。", 15, Ui.Muted);
            hint.margin = new Vector4(0, 4, 0, 4);
        }

        public void RefreshSelection(bool loadSelected)
        {
            refreshing = true;
            var scenario = workbench.GetDraftScenario();
            var team = TacticsUiControls.PlayerTeam(scenario);
            var units = scenario.units.Where(unit => unit.team == team).ToArray();
            unitIds = units.Select(unit => unit.id).ToArray();
            if (!unitIds.Contains(selectedUnitId)) selectedUnitId = unitIds.FirstOrDefault();
            TacticsUiControls.Options(UnitDropdown, units.Select(unit => unit.name + " [" + unit.id + "]"), Array.IndexOf(unitIds, selectedUnitId));
            catalog = workbench.GetCatalog(selectedUnitId);
            var unit = units.First(value => value.id == selectedUnitId);
            var preview = workbench.GetDraftPreview().units.First(value => value.id == selectedUnitId);
            capacityText.text = unit.name + " · 智力 " + preview.intelligence + "  |  指令 " + unit.instructions.Length + "/" + catalog.instructionCapacity
                + "\n每条条件项容量 " + catalog.conditionCapacity + "；超载项保留并显示实际失效原因。";
            var options = new List<string> { "未选已有指令 · 新增将放到末尾" };
            for (var i = 0; i < unit.instructions.Length; i++)
            {
                var validation = workbench.GetInstructionValidation(selectedUnitId, i);
                options.Add("#" + (i + 1) + " " + preview.instructions[i].display + (validation.success ? "" : " [失效]"));
            }
            if (selectedInstructionIndex >= unit.instructions.Length) selectedInstructionIndex = unit.instructions.Length - 1;
            TacticsUiControls.Options(InstructionDropdown, options, selectedInstructionIndex + 1);
            // List/status refreshes must not reset a partially completed form.
            if (loadSelected) PopulateCatalogOptions();
            refreshing = false;
            RefreshSelectedRule();
            if (loadSelected && selectedInstructionIndex >= 0) LoadSelectedInstruction();
            else if (loadSelected) SetDraft(workbench.CreateBlankInstruction());
            else RefreshDraftPreview();
        }

        public void SelectUnit(string unitId)
        {
            if (!unitIds.Contains(unitId)) throw new ArgumentException("没有可编排队员：" + unitId);
            selectedUnitId = unitId;
            selectedInstructionIndex = -1;
            RefreshSelection(true);
        }

        public void SelectInstruction(int index, bool loadDraft = true)
        {
            selectedInstructionIndex = index;
            RefreshSelection(loadDraft);
        }

        private void PopulateCatalogOptions()
        {
            targetModes = catalog.targetModes ?? Array.Empty<CombatTacticsOption>();
            effectKinds = catalog.effectKinds ?? Array.Empty<CombatTacticsOption>();
            damageTypes = catalog.damageTypes ?? Array.Empty<CombatTacticsOption>();
            statusKinds = catalog.statusKinds ?? Array.Empty<CombatTacticsOption>();
            SetOptions(OperationDropdown, catalog.operations, "选择 AND／OR／明确混合分组");
            SetOptions(ScopeDropdown, catalog.scopes, "选择对象范围");
            SetOptions(SelectorDropdown, catalog.selectors, "选择目标的方式");
            selectedActionId = "";
            ActionSearchInput.SetTextWithoutNotify("");
            RefreshActionOptions();
            SetOptions(GroupingDropdown, catalog.groupings, "选择括号分组");
            var operators = catalog.operations.Where(option => option.id != "mixed").ToArray();
            SetOptions(LeftOperatorDropdown, operators, "A 与 B 的连接符");
            SetOptions(RightOperatorDropdown, operators, "B 与 C 的连接符");
        }

        private void RefreshSelectedRule()
        {
            var unit = workbench.GetDraftPreview().units.First(value => value.id == selectedUnitId);
            var selected = selectedInstructionIndex >= 0 && selectedInstructionIndex < unit.instructions.Length;
            DeleteButton.interactable = selected;
            ReplaceButton.interactable = selected;
            LoadSelectedButton.interactable = selected;
            MoveUpButton.interactable = selected && selectedInstructionIndex > 0;
            MoveDownButton.interactable = selected && selectedInstructionIndex < unit.instructions.Length - 1;
            if (!selected)
            {
                selectedRuleText.text = "未选已有指令。\n点「新建空白」，填完四段后新增；已有规则从上方列表选取。";
                selectedRuleText.color = Ui.Muted;
                return;
            }
            var validation = workbench.GetInstructionValidation(selectedUnitId, selectedInstructionIndex);
            selectedRuleText.text = "当前 #" + (selectedInstructionIndex + 1) + "  " + unit.instructions[selectedInstructionIndex].display
                + "\n" + (validation.success ? "可用 · 实际指令与容量预检通过。" : validation.code + " · " + validation.message);
            selectedRuleText.color = validation.success ? Ui.Ink : Ui.Bad;
            SelectedRuleScroll.verticalNormalizedPosition = 1;
        }

        public void LoadSelectedInstruction()
        {
            var unit = workbench.GetDraftScenario().units.First(value => value.id == selectedUnitId);
            if (selectedInstructionIndex < 0 || selectedInstructionIndex >= unit.instructions.Length) return;
            SetDraft(unit.instructions[selectedInstructionIndex]);
        }

        public void SetDraft(CombatInstruction instruction)
        {
            refreshing = true;
            draftId = instruction.id;
            LabelInput.SetTextWithoutNotify(instruction.label ?? "");
            SelectOption(OperationDropdown, catalog.operations, instruction.condition == null ? "" : instruction.condition.operation);
            SelectOption(ScopeDropdown, catalog.scopes, instruction.scope);
            SelectOption(SelectorDropdown, catalog.selectors, instruction.selector);
            selectedActionId = catalog.actions.Any(action => action.id == instruction.actionId) ? instruction.actionId : "";
            ActionSearchInput.SetTextWithoutNotify("");
            RefreshActionOptions();
            foreach (var term in terms) { term.Root.gameObject.SetActive(false); Destroy(term.Root.gameObject); }
            terms.Clear();
            if (instruction.condition != null && instruction.condition.terms != null)
                foreach (var term in instruction.condition.terms) AddCondition(term);
            SelectOption(GroupingDropdown, catalog.groupings, instruction.condition == null ? "" : instruction.condition.grouping);
            var operators = catalog.operations.Where(option => option.id != "mixed").ToArray();
            var binary = instruction.condition == null ? null : instruction.condition.operators;
            SelectOption(LeftOperatorDropdown, operators, binary != null && binary.Length > 0 ? OperationId(binary[0]) : "");
            SelectOption(RightOperatorDropdown, operators, binary != null && binary.Length > 1 ? OperationId(binary[1]) : "");
            refreshing = false;
            HasFormChanges = false;
            RefreshDraftPreview();
            FormScroll.verticalNormalizedPosition = 1;
            FormEdited?.Invoke();
        }

        public void AddCondition() { AddCondition(null); FormChanged(); }

        private void AddCondition(CombatConditionTerm source)
        {
            var controls = new TacticsConditionControls();
            controls.Root = Ui.Column("TacticsTerm_" + terms.Count, termContainer, 5, 7, Ui.Card);
            var top = Ui.Row("TermHeading", controls.Root, 5);
            Ui.FixedHeight(top.gameObject, 31);
            var title = Ui.Text("TermLabel", top, font, "条件 " + TermName(terms.Count), 16, Ui.Accent);
            controls.TitleText = title;
            Ui.Flexible(title.gameObject);
            controls.RemoveButton = Ui.Button("Tactics_RemoveCondition_" + terms.Count, top, font, "移除", () =>
            {
                terms.Remove(controls);
                controls.Root.gameObject.SetActive(false);
                Destroy(controls.Root.gameObject);
                for (var i = 0; i < terms.Count; i++) terms[i].TitleText.text = "条件 " + TermName(i);
                FormChanged();
            }, 76);
            controls.KindDropdown = TacticsUiControls.Dropdown("Tactics_ConditionKind_" + terms.Count, controls.Root, font);
            SetOptions(controls.KindDropdown, catalog.conditions, "选择条件");
            var subject = Ui.Row("SubjectRow", controls.Root, 5);
            Ui.FixedHeight(subject.gameObject, 36);
            Ui.Label("SubjectLabel", subject, font, "读取来源", 81);
            controls.SubjectDropdown = TacticsUiControls.Dropdown("Tactics_ConditionSubject_" + terms.Count, subject, font);
            SetOptions(controls.SubjectDropdown, catalog.subjects, "选择读取来源");
            SelectOption(controls.SubjectDropdown, catalog.subjects, source == null || string.IsNullOrEmpty(source.subject) ? "target" : source.subject);
            controls.ThresholdLabel = Ui.Text("TermThresholdLabel", controls.Root, font, "", 15, Ui.Muted);
            controls.ThresholdInput = Ui.Input("Tactics_ConditionThreshold_" + terms.Count, controls.Root, font, 0);
            Ui.Flexible(controls.ThresholdInput.gameObject, 1, 0);
            Ui.FixedHeight(controls.ThresholdInput.gameObject, 35);
            controls.ValueLabel = Ui.Text("TermValueLabel", controls.Root, font, "", 15, Ui.Muted);
            controls.ValueInput = Ui.Input("Tactics_ConditionValue_" + terms.Count, controls.Root, font, 0);
            Ui.Flexible(controls.ValueInput.gameObject, 1, 0);
            Ui.FixedHeight(controls.ValueInput.gameObject, 35);
            controls.ValueInput.contentType = TMP_InputField.ContentType.Standard;
            controls.DescriptionText = Ui.Text("TermDescription", controls.Root, font, "", 15, Ui.Muted);
            terms.Add(controls);
            SelectOption(controls.KindDropdown, catalog.conditions, source == null ? "" : source.kind);
            UpdateTermMetadata(controls, true);
            if (source != null)
            {
                controls.ThresholdInput.SetTextWithoutNotify(Number(source.threshold));
                controls.ValueInput.SetTextWithoutNotify(source.value ?? "");
            }
            controls.KindDropdown.onValueChanged.AddListener(_ => { UpdateTermMetadata(controls, true); FormChanged(); });
            controls.SubjectDropdown.onValueChanged.AddListener(_ => FormChanged());
            controls.ThresholdInput.onValueChanged.AddListener(_ => FormChanged());
            controls.ValueInput.onValueChanged.AddListener(_ => FormChanged());
            RefreshDraftPreview();
        }

        private void UpdateTermMetadata(TacticsConditionControls controls, bool setDefault)
        {
            var option = Selected(catalog.conditions, controls.KindDropdown);
            controls.ThresholdInput.gameObject.SetActive(option != null && option.usesThreshold);
            controls.ValueInput.gameObject.SetActive(option != null && option.usesValue);
            controls.ThresholdLabel.gameObject.SetActive(option != null && option.usesThreshold);
            controls.ValueLabel.gameObject.SetActive(option != null && option.usesValue);
            controls.DescriptionText.text = option == null ? "先选择条件。默认来源是当前候选对象；施放者条件须显式切换来源。"
                : (string.IsNullOrEmpty(option.category) ? "" : "目录类别：" + CategoryLabel(option.category) + " [" + option.category + "]\n") + option.description;
            if (option == null) return;
            controls.ThresholdInput.contentType = option.integerThreshold ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
            controls.ThresholdLabel.text = "阈值 " + Number(option.minThreshold) + " 至 " + Number(option.maxThreshold)
                + " · " + ThresholdUnitLabel(option.thresholdUnit) + (option.integerThreshold ? "（整数）" : "");
            controls.ValueLabel.text = option.valueHint;
            if (setDefault && option.usesThreshold) controls.ThresholdInput.SetTextWithoutNotify(Number(option.defaultThreshold));
            if (setDefault && option.usesValue) controls.ValueInput.SetTextWithoutNotify(option.defaultValue ?? "");
        }

        public bool TryReadInstruction(out CombatInstruction instruction, out string error)
        {
            error = null;
            var conditionTerms = new List<CombatConditionTerm>();
            for (var i = 0; i < terms.Count; i++)
            {
                var option = Selected(catalog.conditions, terms[i].KindDropdown);
                var threshold = 0.0;
                if (option != null && option.usesThreshold && !double.TryParse(terms[i].ThresholdInput.text,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out threshold))
                {
                    instruction = null;
                    error = "条件 " + TermName(i) + " 的阈值请输入数字。";
                    return false;
                }
                conditionTerms.Add(new CombatConditionTerm
                {
                    kind = option == null ? "" : option.id,
                    threshold = threshold,
                    subject = SelectedId(catalog.subjects, terms[i].SubjectDropdown),
                    value = option != null && option.usesValue ? terms[i].ValueInput.text : ""
                });
            }
            var operation = SelectedId(catalog.operations, OperationDropdown);
            var binary = catalog.operations.Where(option => option.id != "mixed").ToArray();
            instruction = new CombatInstruction
            {
                id = draftId,
                label = LabelInput.text,
                condition = new CombatCondition
                {
                    operation = operation,
                    terms = conditionTerms.ToArray(),
                    operators = operation == "mixed" ? new[] { BinaryId(SelectedId(binary, LeftOperatorDropdown)), BinaryId(SelectedId(binary, RightOperatorDropdown)) } : null,
                    grouping = operation == "mixed" ? SelectedId(catalog.groupings, GroupingDropdown) : null
                },
                scope = SelectedId(catalog.scopes, ScopeDropdown),
                selector = SelectedId(catalog.selectors, SelectorDropdown),
                actionId = selectedActionId
            };
            return true;
        }

        private void RefreshDraftPreview()
        {
            if (refreshing || catalog == null || OperationDropdown == null) return;
            RefreshActionDetails();
            var operation = SelectedId(catalog.operations, OperationDropdown);
            mixedControls.gameObject.SetActive(operation == "mixed");
            var labels = terms.Select((term, index) => TermDescription(term, index)).ToArray();
            string expression;
            if (operation == "mixed")
            {
                var a = labels.Length > 0 ? labels[0] : "A:未填";
                var b = labels.Length > 1 ? labels[1] : "B:未填";
                var c = labels.Length > 2 ? labels[2] : "C:未填";
                var binary = catalog.operations.Where(option => option.id != "mixed").ToArray();
                var first = BinaryLabel(SelectedId(binary, LeftOperatorDropdown));
                var second = BinaryLabel(SelectedId(binary, RightOperatorDropdown));
                var grouping = SelectedId(catalog.groupings, GroupingDropdown);
                expression = grouping == "left" ? "(" + a + " " + first + " " + b + ") " + second + " " + c
                    : grouping == "right" ? a + " " + first + " (" + b + " " + second + " " + c + ")"
                    : "未选择括号分组： (A op B) op C 或 A op (B op C)";
                if (labels.Length != 3) expression += "\n混合表达式需 3 项；当前 " + labels.Length + " 项，提交时由战斗核心校验。";
            }
            else expression = labels.Length == 0 ? "条件尚为空白" : "(" + string.Join(operation == "all" ? " AND " : operation == "any" ? " OR " : " ? ", labels) + ")";
            expressionText.text = "明确表达式：\n" + expression;
            draftPreviewText.text = "四段预览\n" + expression + "\n｜" + SelectedLabel(catalog.scopes, ScopeDropdown, "未选范围")
                + "｜" + SelectedLabel(catalog.selectors, SelectorDropdown, "未选方式") + "｜"
                + (SelectedAction == null ? "未选行动" : SelectedAction.displayName);
        }

        private void RefreshActionOptions()
        {
            if (catalog == null || ActionDropdown == null) return;
            var query = (ActionSearchInput.text ?? "").Trim();
            var matches = catalog.actions.Where(action => MatchesAction(action, query)).ToArray();
            var current = SelectedAction;
            var retainCurrent = current != null && !matches.Any(action => action.id == current.id);
            MatchingActionCount = matches.Length;
            SelectedActionRetained = retainCurrent;
            visibleActions.Clear();
            if (retainCurrent) visibleActions.Add(current);
            visibleActions.AddRange(matches);
            var wasRefreshing = refreshing;
            refreshing = true;
            TacticsUiControls.Options(ActionDropdown, new[] { "选择角色行动" }.Concat(visibleActions.Select(action =>
                (retainCurrent && action.id == selectedActionId ? "当前已选 · " : "") + ActionLabel(action))),
                visibleActions.FindIndex(action => action.id == selectedActionId) + 1);
            refreshing = wasRefreshing;
            ActionMatchText.text = "匹配 " + matches.Length + " / 可配 " + catalog.actions.Length
                + (retainCurrent ? "；当前已选动作保留在首项。" : "；搜索只浏览，选动作才修改草稿。");
            RefreshActionDetails();
        }

        private bool MatchesAction(CombatActionConfig action, string query)
        {
            return string.IsNullOrEmpty(query) || Contains(action.displayName, query) || Contains(action.id, query)
                || Contains(action.effectKind, query) || MatchesOption(effectKinds, action.effectKind, query)
                || MatchesOption(targetModes, string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode, query)
                || MatchesOption(statusKinds, action.statusKind, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ActionLabel(CombatActionConfig action)
        {
            return action.displayName + " · MP " + action.mpCost + " · WAIT " + Number(action.baseWait);
        }

        private void RefreshActionDetails()
        {
            if (ActionDetailsText == null || catalog == null) return;
            var action = SelectedAction;
            if (action == null)
            {
                ActionDetailsText.text = "请选择动作查看实际费用、WAIT与效果配置。可输入中文名称查找；目录只包含当前角色已开放动作。";
                return;
            }
            var text = new StringBuilder();
            var modeId = string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode;
            var mode = FindOption(targetModes, modeId);
            var effect = FindOption(effectKinds, action.effectKind);
            text.Append(action.displayName).Append(" [").Append(action.id).Append("]\nMP 费用 ").Append(action.mpCost)
                .Append("；基础 WAIT ").Append(Number(action.baseWait)).Append("；覆盖 ").Append(mode == null ? modeId : mode.label)
                .Append("\n效果：").Append(effect == null ? action.effectKind : effect.label).Append(" [").Append(action.effectKind)
                .Append("]");
            if (action.effectKind == "damage" || action.effectKind == "heal" || action.effectKind == "restore_mp" || action.effectKind == "drain_mp")
                text.Append("；基础量 ").Append(action.magnitude);
            if (action.effectKind == "apply_status")
                text.Append("；幅度 ").Append(action.statusMagnitude).Append("；持续 WAIT ").Append(Number(action.statusDuration));
            if (effect != null && !string.IsNullOrEmpty(effect.description)) text.Append("\n").Append(effect.description);
            text.Append("\n主目标范围：").Append(SelectedLabel(catalog.scopes, ScopeDropdown, "尚未选择"))
                .Append("；选择方式：").Append(SelectedLabel(catalog.selectors, SelectorDropdown, "尚未选择"));
            text.Append("\n魔法动作标记 isMagic：").Append(action.isMagic ? "是" : "否").Append("；沉默按此标记检查。");
            if (action.effectKind == "damage")
            {
                var damageId = string.IsNullOrEmpty(action.damageType) ? "legacy" : action.damageType;
                var damage = FindOption(damageTypes, damageId);
                text.Append("\n伤害类型 damageType：").Append(damage == null ? damageId : damage.label).Append(" [").Append(damageId).Append("]");
                if (damage != null && !string.IsNullOrEmpty(damage.description)) text.Append("\n").Append(damage.description);
            }
            if (!string.IsNullOrEmpty(action.statusId) || !string.IsNullOrEmpty(action.statusKind))
            {
                var status = FindOption(statusKinds, action.statusKind);
                text.Append("\n状态 ").Append(action.statusId).Append(" / ").Append(action.statusKind)
                    .Append("；幅度 ").Append(action.statusMagnitude).Append("；持续 WAIT ").Append(Number(action.statusDuration));
                if (status != null) text.Append("\n").Append(status.label).Append("：").Append(status.description);
            }
            text.Append("\n覆盖方式：").Append(mode == null ? modeId : mode.label).Append(" [").Append(modeId).Append("]");
            if (mode != null && !string.IsNullOrEmpty(mode.description)) text.Append("\n").Append(mode.description);
            if (modeId == "single") text.Append("\n只对选出的主目标生效。");
            else text.Append("\n先用完整条件、对象范围和选择方式选主目标，再扩展覆盖。覆盖仍受原对象范围限制，自身范围只含自身。")
                .Append("\n群体覆盖者不重复检查主目标条件；MP 费用与 WAIT 各结算一次。");
            ActionDetailsText.text = text.ToString();
        }

        private static CombatTacticsOption FindOption(CombatTacticsOption[] options, string id)
        {
            return options.FirstOrDefault(option => option.id == id);
        }

        private static bool MatchesOption(CombatTacticsOption[] options, string id, string query)
        {
            var option = FindOption(options, id);
            return option != null && (Contains(option.label, query) || Contains(option.description, query));
        }

        private static string ThresholdUnitLabel(string unit)
        {
            switch (unit)
            {
                case "ratio": return "比例（0.35 = 35%）";
                case "points": return "点数";
                case "count": return "人数";
                case "wait": return "WAIT";
                case "row": return "排号";
                default: return string.IsNullOrEmpty(unit) ? "数值" : unit;
            }
        }

        private static string CategoryLabel(string category)
        {
            switch (category)
            {
                case "basic": return "基础";
                case "resource": return "资源";
                case "position": return "站位";
                case "status": return "状态";
                case "tag": return "标签";
                case "team": return "队伍数量";
                case "time": return "时间";
                case "relation": return "关系";
                default: return category;
            }
        }

        private void FormChanged()
        {
            if (refreshing) return;
            HasFormChanges = true;
            RefreshDraftPreview();
            FormEdited?.Invoke();
        }

        public TacticsFormInput GetFormInputSnapshot()
        {
            var binary = catalog.operations.Where(option => option.id != "mixed").ToArray();
            return new TacticsFormInput
            {
                selectedUnitId = selectedUnitId,
                selectedInstructionIndex = selectedInstructionIndex,
                instructionId = draftId,
                label = LabelInput.text,
                operation = SelectedId(catalog.operations, OperationDropdown),
                grouping = SelectedId(catalog.groupings, GroupingDropdown),
                operators = new[] { BinaryId(SelectedId(binary, LeftOperatorDropdown)), BinaryId(SelectedId(binary, RightOperatorDropdown)) },
                scope = SelectedId(catalog.scopes, ScopeDropdown),
                selector = SelectedId(catalog.selectors, SelectorDropdown),
                actionId = selectedActionId,
                actionSearch = ActionSearchInput.text,
                hasUnsubmittedChanges = HasFormChanges,
                terms = terms.Select(term => new TacticsFormTerm
                {
                    kind = SelectedId(catalog.conditions, term.KindDropdown),
                    subject = SelectedId(catalog.subjects, term.SubjectDropdown),
                    thresholdText = term.ThresholdInput.text,
                    value = term.ValueInput.text
                }).ToArray()
            };
        }

        /// <summary>Restores raw shared form text without parsing numbers or touching the battle.
        /// Candidate views are projections of this one editor, including incomplete/invalid input.</summary>
        public void ApplyFormInput(TacticsFormInput input, bool markDirty = true)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.selectedUnitId != selectedUnitId || input.selectedInstructionIndex != selectedInstructionIndex)
                throw new ArgumentException("切换队员或规则请使用选择命令；表单提交不能暗中切换当前选择。");
            refreshing = true;
            try
            {
                draftId = input.instructionId;
                LabelInput.SetTextWithoutNotify(input.label ?? "");
                SelectOption(OperationDropdown, catalog.operations, input.operation);
                SelectOption(ScopeDropdown, catalog.scopes, input.scope);
                SelectOption(SelectorDropdown, catalog.selectors, input.selector);
                SelectOption(GroupingDropdown, catalog.groupings, input.grouping);
                var binaryOptions = catalog.operations.Where(option => option.id != "mixed").ToArray();
                SelectOption(LeftOperatorDropdown, binaryOptions, input.operators != null && input.operators.Length > 0 ? OperationId(input.operators[0]) : "");
                SelectOption(RightOperatorDropdown, binaryOptions, input.operators != null && input.operators.Length > 1 ? OperationId(input.operators[1]) : "");
                selectedActionId = input.actionId ?? "";
                ActionSearchInput.SetTextWithoutNotify(input.actionSearch ?? "");
                RefreshActionOptions();
                foreach (var old in terms) { old.Root.gameObject.SetActive(false); Destroy(old.Root.gameObject); }
                terms.Clear();
                foreach (var raw in input.terms ?? Array.Empty<TacticsFormTerm>())
                {
                    AddCondition(null);
                    var controls = terms[terms.Count - 1];
                    SelectOption(controls.KindDropdown, catalog.conditions, raw.kind);
                    SelectOption(controls.SubjectDropdown, catalog.subjects, raw.subject);
                    UpdateTermMetadata(controls, false);
                    controls.ThresholdInput.SetTextWithoutNotify(raw.thresholdText ?? "");
                    controls.ValueInput.SetTextWithoutNotify(raw.value ?? "");
                }
                HasFormChanges = HasFormChanges || markDirty || input.hasUnsubmittedChanges;
            }
            finally { refreshing = false; }
            RefreshDraftPreview();
            FormEdited?.Invoke();
        }

        private string TermDescription(TacticsConditionControls controls, int index)
        {
            var option = Selected(catalog.conditions, controls.KindDropdown);
            var prefix = SelectedId(catalog.subjects, controls.SubjectDropdown) == "actor" ? "施放者" : "";
            return TermName(index) + ":" + prefix + (option == null ? "未填" : option.label)
                + (option != null && option.usesThreshold ? controls.ThresholdInput.text : "")
                + (option != null && option.usesValue ? "[" + controls.ValueInput.text + "]" : "");
        }

        private static void SetOptions(TMP_Dropdown dropdown, CombatTacticsOption[] options, string placeholder)
        {
            TacticsUiControls.Options(dropdown, new[] { placeholder }.Concat(options.Select(option =>
                string.IsNullOrEmpty(option.category) ? option.label : CategoryLabel(option.category) + " · " + option.label)));
        }

        private static void SelectOption(TMP_Dropdown dropdown, CombatTacticsOption[] options, string id)
        {
            dropdown.SetValueWithoutNotify(Array.FindIndex(options, option => option.id == id) + 1);
            dropdown.RefreshShownValue();
        }

        private static CombatTacticsOption Selected(CombatTacticsOption[] options, TMP_Dropdown dropdown)
        {
            return dropdown.value > 0 && dropdown.value <= options.Length ? options[dropdown.value - 1] : null;
        }
        private static string SelectedId(CombatTacticsOption[] options, TMP_Dropdown dropdown) { return Selected(options, dropdown)?.id ?? ""; }
        private static string SelectedLabel(CombatTacticsOption[] options, TMP_Dropdown dropdown, string fallback) { return Selected(options, dropdown)?.label ?? fallback; }
        private static string BinaryId(string operation) { return operation == "all" ? "and" : operation == "any" ? "or" : ""; }
        private static string OperationId(string binary) { return binary == "and" ? "all" : binary == "or" ? "any" : ""; }
        private static string BinaryLabel(string operation) { return operation == "all" ? "AND" : operation == "any" ? "OR" : "?"; }
        private static string TermName(int index) { return index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString(); }
        private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    }

    [Serializable]
    public sealed class TacticsFormInput
    {
        public string selectedUnitId;
        public int selectedInstructionIndex;
        public string instructionId;
        public string label;
        public string operation;
        public string grouping;
        public string[] operators;
        public string scope;
        public string selector;
        public string actionId;
        public string actionSearch;
        public bool hasUnsubmittedChanges;
        public TacticsFormTerm[] terms;
    }

    [Serializable]
    public sealed class TacticsFormTerm
    {
        public string kind;
        public string subject;
        public string thresholdText;
        public string value;
    }
}
