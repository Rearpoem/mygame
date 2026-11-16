using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prototype.Combat;
using Prototype.Tactics;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Prototype.UnityClient.Candidates.Argon
{
    public sealed class ArgonConditionControls
    {
        public TMP_Dropdown SubjectDropdown { get; internal set; }
        public TMP_Dropdown KindDropdown { get; internal set; }
        public TMP_InputField ThresholdInput { get; internal set; }
        public TMP_InputField ValueInput { get; internal set; }
        public TMP_Text UnitText { get; internal set; }
        public TMP_Text HintText { get; internal set; }
        public Button RemoveButton { get; internal set; }
    }

    public sealed class ArgonEditorPage : MonoBehaviour
    {
        private TMP_FontAsset font;
        private TacticsSharedController shared;
        private bool refreshing;
        private RectTransform roleContent;
        private RectTransform ruleContent;
        private RectTransform enemyContent;
        private RectTransform termsContent;
        private RectTransform conditionSection;
        private RectTransform actionContent;
        private RectTransform filterContent;
        private RectTransform scopeContent;
        private TMP_Text formTitle;
        private TMP_Text capacityText;
        private TMP_Text capacityNote;
        private TMP_Text ruleFeedback;
        private TMP_Text formWarning;
        private TMP_Text expressionText;
        private TMP_Text scopeHint;
        private TMP_Text actionMatch;
        private TMP_Text emptyText;
        private RectTransform emptyPanel;
        private readonly List<ArgonConditionControls> terms = new List<ArgonConditionControls>();
        private readonly List<Button> roleButtons = new List<Button>();
        private readonly List<Button> ruleButtons = new List<Button>();
        private readonly List<Button> actionButtons = new List<Button>();
        private readonly List<Button> scopeButtons = new List<Button>();
        private string structureSignature;
        private string actionSignature;
        private string formStructureSignature;
        private string effectFilter = "";
        private string filterOwner;
        private int actionColumns = 2;
        private bool layoutRefreshPending;

        public TMP_InputField LabelInput { get; private set; }
        public TMP_Dropdown OperationDropdown { get; private set; }
        public TMP_Dropdown GroupingDropdown { get; private set; }
        public TMP_Dropdown LeftOperatorDropdown { get; private set; }
        public TMP_Dropdown RightOperatorDropdown { get; private set; }
        public TMP_Dropdown SelectorDropdown { get; private set; }
        public TMP_InputField ActionSearchInput { get; private set; }
        public TMP_Text ActionDetailsText { get; private set; }
        public TMP_Text CapacityText { get { return capacityText; } }
        public TMP_Text ExpressionText { get { return expressionText; } }
        public TMP_Text FormWarningText { get { return formWarning; } }
        public Button NewBlankButton { get; private set; }
        public Button MoveUpButton { get; private set; }
        public Button MoveDownButton { get; private set; }
        public Button LoadSelectedButton { get; private set; }
        public Button ReplaceButton { get; private set; }
        public Button InsertButton { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button AddConditionButton { get; private set; }
        public Button ClearSearchButton { get; private set; }
        public IReadOnlyList<ArgonConditionControls> Terms { get { return terms.AsReadOnly(); } }
        public IReadOnlyList<Button> RoleButtons { get { return roleButtons.AsReadOnly(); } }
        public IReadOnlyList<Button> RuleButtons { get { return ruleButtons.AsReadOnly(); } }
        public IReadOnlyList<Button> ActionButtons { get { return actionButtons.AsReadOnly(); } }
        public IReadOnlyList<Button> ScopeButtons { get { return scopeButtons.AsReadOnly(); } }

        public void Initialize(TMP_FontAsset value, TacticsSharedController controller)
        {
            font = value;
            shared = controller;
            var columns = ArgonUi.Row("Argon_PreparationThreeColumns", transform, 12);
            ArgonUi.Fill(columns);
            var roles = ArgonUi.Frame("Argon_CrewAndIntelligence", columns);
            ArgonUi.Size(roles.parent.gameObject, -1, -1, 256, 1);
            var priorities = ArgonUi.Frame("Argon_PriorityQueue", columns);
            ArgonUi.Size(priorities.parent.gameObject, -1, -1, 368, 1);
            var form = ArgonUi.Frame("Argon_FourPartWorkbench", columns, ArgonTheme.Brass);
            ArgonUi.Size(form.parent.gameObject, -1, -1, 608, 1);
            BuildCrew(roles);
            BuildPriority(priorities);
            BuildForm(form);
        }

        private void BuildCrew(Transform parent)
        {
            var column = ArgonUi.Column("CrewLayout", parent, 7);
            ArgonUi.Fill(column);
            Heading(column, "队员与智力容量");
            roleContent = ArgonUi.Column("Argon_RoleCards", column, 7);
            ArgonUi.Size(roleContent.gameObject, -1, 176, 1);
            capacityText = ArgonUi.Text("Argon_Capacity", column, font, "", 16, ArgonTheme.Focus, true);
            ArgonUi.Size(capacityText.gameObject, -1, 83, 1);
            capacityNote = ArgonUi.Text("Argon_CapacityContract", column, font, "容量读取当前角色，27项技能不等于27条指令。", 14, ArgonTheme.Secondary);
            ArgonUi.Size(capacityNote.gameObject, -1, 66, 1);
            Heading(column, "初始敌情 · 只读");
            ScrollRect scroll;
            enemyContent = ArgonUi.Scroll("Argon_EnemyIntelligence", column, out scroll, true, 2, 6);
            var note = ArgonUi.Text("SourceOfTruth", column, font, "这里显示草稿初始属性。\n正在播放的战斗使用已提交配置。", 12, ArgonTheme.Muted);
            ArgonUi.Size(note.gameObject, -1, 42, 1);
        }

        private void BuildPriority(Transform parent)
        {
            var column = ArgonUi.Column("PriorityLayout", parent, 7);
            ArgonUi.Fill(column);
            Heading(column, "优先级指令槽 · 自上而下尝试");
            var toolbar = ArgonUi.Row("PriorityTools", column, 5);
            ArgonUi.Size(toolbar.gameObject, -1, 32, 1);
            NewBlankButton = ArgonUi.Button("Argon_NewBlank", toolbar, font, "新建空白", () => shared.NewBlank(), 85, 32);
            MoveUpButton = ArgonUi.Button("Argon_MoveUp", toolbar, font, "↑", () => shared.Move(-1), 36, 32);
            MoveDownButton = ArgonUi.Button("Argon_MoveDown", toolbar, font, "↓", () => shared.Move(1), 36, 32);
            LoadSelectedButton = ArgonUi.Button("Argon_LoadSelected", toolbar, font, "重载所选", () => shared.LoadSelected(), -1, 32);
            ScrollRect scroll;
            ruleContent = ArgonUi.Scroll("Argon_PriorityCards", column, out scroll, true, 2, 8);
            ruleFeedback = ArgonUi.Text("Argon_RuleFeedback", column, font, "", 14, ArgonTheme.Warning);
            ArgonUi.Size(ruleFeedback.gameObject, -1, 62, 1);
        }

        private void BuildForm(Transform parent)
        {
            var column = ArgonUi.Column("FourPartLayout", parent, 5);
            ArgonUi.Fill(column);
            var top = ArgonUi.Row("FormHeading", column, 5);
            ArgonUi.Size(top.gameObject, -1, 29, 1);
            formTitle = ArgonUi.Text("Argon_FormTitle", top, font, "新建规则", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(formTitle.gameObject, 140, 29);
            LabelInput = ArgonUi.Input("Argon_RuleLabel", top, font, "规则名称", -1, 29);
            LabelInput.onValueChanged.AddListener(value => Edit(form => form.label = value));
            formWarning = ArgonUi.Text("Argon_FormWarning", column, font, "", 12, ArgonTheme.Warning);
            ArgonUi.Size(formWarning.gameObject, -1, 19, 1);
            BuildConditions(column);
            BuildScopeAndSelection(column);
            BuildActions(column);
            var note = ArgonUi.Text("Argon_SaveGuidance", column, font, "表单先写入指令槽，再点顶部「应用并开战」。", 12, ArgonTheme.Muted);
            ArgonUi.Size(note.gameObject, -1, 19, 1);
            var save = ArgonUi.Row("FormSaveControls", column, 7);
            ArgonUi.Size(save.gameObject, -1, 34, 1);
            ReplaceButton = ArgonUi.Button("Argon_Replace", save, font, "应用替换", () => shared.Replace(), -1, 34, true);
            InsertButton = ArgonUi.Button("Argon_Insert", save, font, "新增到末尾", () => shared.Insert(), -1, 34);
            DeleteButton = ArgonUi.Button("Argon_Delete", save, font, "删除本条", () => shared.Delete(), -1, 34);
        }

        private void BuildConditions(Transform parent)
        {
            var root = ArgonUi.Column("Argon_ConditionExpression", parent, 3);
            conditionSection = root;
            ArgonUi.Size(root.gameObject, -1, 138, 1);
            var title = ArgonUi.Row("ConditionHeading", root, 4);
            ArgonUi.Size(title.gameObject, -1, 24, 1);
            var heading = ArgonUi.Text("Heading", title, font, "① 条件表达式", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(heading.gameObject, -1, 24, 1);
            AddConditionButton = ArgonUi.Button("Argon_AddCondition", title, font, "+ 条件项", AddTerm, 94, 24);
            var logic = ArgonUi.Row("LogicGates", root, 4);
            ArgonUi.Size(logic.gameObject, -1, 28, 1);
            OperationDropdown = ArgonUi.Dropdown("Argon_Operation", logic, font, -1, 28);
            GroupingDropdown = ArgonUi.Dropdown("Argon_Grouping", logic, font, 154, 28);
            LeftOperatorDropdown = ArgonUi.Dropdown("Argon_LeftOperator", logic, font, 65, 28);
            RightOperatorDropdown = ArgonUi.Dropdown("Argon_RightOperator", logic, font, 65, 28);
            OperationDropdown.onValueChanged.AddListener(_ => Pick(OperationDropdown, state => state.catalog.operations, (form, id) => form.operation = id));
            GroupingDropdown.onValueChanged.AddListener(_ => Pick(GroupingDropdown, state => state.catalog.groupings, (form, id) => form.grouping = id));
            LeftOperatorDropdown.onValueChanged.AddListener(_ => PickBinary(0, LeftOperatorDropdown));
            RightOperatorDropdown.onValueChanged.AddListener(_ => PickBinary(1, RightOperatorDropdown));
            expressionText = ArgonUi.Text("Argon_ExplicitExpression", root, font, "", 14, ArgonTheme.Focus, true);
            ArgonUi.Size(expressionText.gameObject, -1, 22, 1);
            ScrollRect scroll;
            termsContent = ArgonUi.Scroll("Argon_ConditionRows", root, out scroll, true, 1, 4);
        }

        private void BuildScopeAndSelection(Transform parent)
        {
            var root = ArgonUi.Column("Argon_ScopeAndSelector", parent, 3);
            ArgonUi.Size(root.gameObject, -1, 76, 1);
            var headings = ArgonUi.Row("SegmentHeadings", root, 8);
            ArgonUi.Size(headings.gameObject, -1, 22, 1);
            var scope = ArgonUi.Text("ScopeHeading", headings, font, "② 对象范围", 16, ArgonTheme.Brass, true);
            var selector = ArgonUi.Text("SelectorHeading", headings, font, "③ 选择方式", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(scope.gameObject, -1, 22, 1);
            ArgonUi.Size(selector.gameObject, -1, 22, 1);
            var fields = ArgonUi.Row("ScopeSelectorInputs", root, 8);
            ArgonUi.Size(fields.gameObject, -1, 28, 1);
            scopeContent = ArgonUi.Row("Argon_ScopeChips", fields, 4);
            ArgonUi.Size(scopeContent.gameObject, -1, 28, 1);
            SelectorDropdown = ArgonUi.Dropdown("Argon_Selector", fields, font, -1, 28);
            SelectorDropdown.onValueChanged.AddListener(_ => Pick(SelectorDropdown, state => state.catalog.selectors, (form, id) => form.selector = id));
            scopeHint = ArgonUi.Text("Argon_ScopeContract", root, font, "", 12, ArgonTheme.Muted);
            ArgonUi.Size(scopeHint.gameObject, -1, 18, 1);
        }

        private void BuildActions(Transform parent)
        {
            var root = ArgonUi.Column("Argon_RoleSkillWhitelist", parent, 4);
            ArgonUi.Size(root.gameObject, -1, 210, 1, 1);
            Heading(root, "④ 行动 · 当前角色白名单");
            ScrollRect detailsScroll;
            ActionDetailsText = ArgonUi.Readout("Argon_SelectedActionDetails", root, font, out detailsScroll);
            // Three full Noto lines plus the viewport/content inset. The one-term
            // condition section releases enough space for two complete skill rows.
            ArgonUi.Size(detailsScroll.gameObject, -1, 84, 1);
            var search = ArgonUi.Row("ActionSearch", root, 5);
            ArgonUi.Size(search.gameObject, -1, 29, 1);
            ActionSearchInput = ArgonUi.Input("Argon_ActionSearch", search, font, "搜索技能 / 范围 / 状态", -1, 29);
            ActionSearchInput.onValueChanged.AddListener(value => { if (!refreshing) shared.SetActionSearch(value); });
            ClearSearchButton = ArgonUi.Button("Argon_ClearSearch", search, font, "清空", ClearSearch, 55, 29);
            filterContent = ArgonUi.Row("EffectFilterChips", root, 3);
            ArgonUi.Size(filterContent.gameObject, -1, 23, 1);
            actionMatch = ArgonUi.Text("Argon_ActionMatch", root, font, "", 12, ArgonTheme.Muted);
            ArgonUi.Size(actionMatch.gameObject, -1, 18, 1);
            ScrollRect scroll;
            actionContent = ArgonUi.Scroll("Argon_ActionCards", root, out scroll, true, 2, 5);
            emptyPanel = ArgonUi.Column("Argon_EmptySearch", root, 3, 4);
            ArgonUi.Size(emptyPanel.gameObject, -1, 78, 1);
            emptyText = ArgonUi.Text("EmptySearchMessage", emptyPanel, font, "", 14, ArgonTheme.Warning);
            ArgonUi.Size(emptyText.gameObject, -1, 41, 1);
            ArgonUi.Button("Argon_EmptySearchClear", emptyPanel, font, "清空搜索与筛选条件", ClearSearch, -1, 29);
            emptyPanel.gameObject.SetActive(false);
        }

        public void Refresh(TacticsCandidateState state)
        {
            if (state == null || state.workbench == null || state.form == null || state.catalog == null) return;
            refreshing = true;
            try
            {
                RefreshStructure(state);
                RefreshForm(state);
                RefreshActions(state);
                var unit = state.workbench.draftPreview.units.FirstOrDefault(candidate => candidate.id == state.form.selectedUnitId);
                var config = state.workbench.draftScenario.units.FirstOrDefault(candidate => candidate.id == state.form.selectedUnitId);
                var count = config == null || config.instructions == null ? 0 : config.instructions.Length;
                var capacity = unit == null ? 0 : unit.instructionCapacity;
                var full = count >= capacity;
                capacityText.text = unit == null ? "请选择队员" : unit.name + " · INT " + unit.intelligence + "\n指令槽  " + count + " / " + capacity + (full ? " 已满槽" : "") + "\n每条条件上限  " + unit.conditionTermCapacity;
                capacityText.color = full ? ArgonTheme.Warning : ArgonTheme.Focus;
                capacityNote.text = full ? "指令槽已满：选旧规则后「应用替换」，或先删除一条腾位。技能库数量不会扩充智力容量。" : "指令自上而下尝试。超容量规则保留并显示失效原因，不会被悄悄丢弃。";
                var sameRule = config != null && config.instructions != null && state.form.selectedInstructionIndex >= 0 && state.form.selectedInstructionIndex < config.instructions.Length &&
                    config.instructions[state.form.selectedInstructionIndex].id == state.form.instructionId;
                formTitle.text = state.form.selectedInstructionIndex < 0 ? "新建空白规则" : sameRule ? "编辑槽位 #" + (state.form.selectedInstructionIndex + 1) : "空白 → 槽 #" + (state.form.selectedInstructionIndex + 1);
                formWarning.text = state.form.hasUnsubmittedChanges ? "表单尚未写入指令槽 · 请先应用替换或新增" :
                    sameRule ? "表单已同步 · 修改草稿不会影响已提交战斗" : "空白表单待填写 · 开战使用当前已存指令槽";
                formWarning.color = state.form.hasUnsubmittedChanges ? ArgonTheme.Warning : ArgonTheme.Muted;
                ruleFeedback.text = full ? "满槽 " + count + "/" + capacity + "：可以替换或删除。\n新增入口在腾位后恢复。" : "已用 " + count + "/" + capacity + " 槽。\n点击规则卡加载完整四段。";
                if (unit != null && unit.instructions != null && state.form.selectedInstructionIndex >= 0 && state.form.selectedInstructionIndex < unit.instructions.Length)
                {
                    var reason = unit.instructions[state.form.selectedInstructionIndex].validationReason;
                    if (!string.IsNullOrEmpty(reason)) ruleFeedback.text = "该条失效：" + reason;
                }
                InsertButton.interactable = !full;
                var insertLabel = InsertButton.GetComponentInChildren<TMP_Text>();
                insertLabel.text = full ? "新增到末尾（满槽）" : "新增到末尾";
                insertLabel.color = full ? ArgonTheme.Disabled : ArgonTheme.Secondary;
                ReplaceButton.interactable = DeleteButton.interactable = LoadSelectedButton.interactable = state.form.selectedInstructionIndex >= 0;
                MoveUpButton.interactable = state.form.selectedInstructionIndex > 0;
                MoveDownButton.interactable = state.form.selectedInstructionIndex >= 0 && state.form.selectedInstructionIndex < count - 1;
                AddConditionButton.interactable = (state.form.terms ?? new TacticsFormTerm[0]).Length < state.catalog.conditionCapacity;
                AddConditionButton.GetComponentInChildren<TMP_Text>().text = "+ 条件 " + (state.form.terms ?? new TacticsFormTerm[0]).Length + "/" + state.catalog.conditionCapacity;
                ReplaceButton.GetComponentInChildren<TMP_Text>().text = state.form.selectedInstructionIndex >= 0 ? "应用替换 #" + (state.form.selectedInstructionIndex + 1) : "应用替换";
            }
            finally { refreshing = false; }
        }

        private void RefreshStructure(TacticsCandidateState state)
        {
            var scenario = state.workbench.draftScenario;
            var owner = scenario.id + ":" + state.form.selectedUnitId;
            if (owner != filterOwner) { filterOwner = owner; effectFilter = ""; actionSignature = null; }
            var signature = scenario.id + ":" + state.workbench.draftRevision + ":" + state.form.selectedUnitId + ":" + state.form.selectedInstructionIndex;
            if (structureSignature == signature) return;
            structureSignature = signature;
            ArgonUi.Clear(roleContent); roleButtons.Clear();
            ArgonUi.Clear(enemyContent);
            foreach (var unit in state.workbench.draftPreview.units)
            {
                var friendly = unit.team == scenario.playerTeam;
                var button = BuildUnitCard(friendly ? roleContent : enemyContent, unit, friendly);
                if (friendly) { roleButtons.Add(button); ArgonUi.Selected(button, unit.id == state.form.selectedUnitId); }
            }
            ArgonUi.Clear(ruleContent); ruleButtons.Clear();
            var selected = scenario.units.FirstOrDefault(unit => unit.id == state.form.selectedUnitId);
            var instructions = selected == null || selected.instructions == null ? new CombatInstruction[0] : selected.instructions;
            var preview = state.workbench.draftPreview.units.FirstOrDefault(unit => unit.id == state.form.selectedUnitId);
            for (var i = 0; i < instructions.Length; i++)
            {
                var index = i;
                var instruction = instructions[i];
                var button = ArgonUi.Button("Argon_Rule_" + i, ruleContent, font, "", () => shared.SelectInstruction(index), -1, 96);
                UnityEngine.Object.Destroy(button.transform.Find("Label").gameObject);
                ArgonUi.Selected(button, i == state.form.selectedInstructionIndex);
                var name = ArgonUi.Text("RuleTitle", button.transform, font, "#" + (i + 1) + "  " + instruction.label, 15, ArgonTheme.Brass, true);
                PlaceText(name, 5, 2, 31, 22);
                var invalid = preview != null && preview.instructions != null && i < preview.instructions.Length && !string.IsNullOrEmpty(preview.instructions[i].validationReason);
                if (invalid) { name.text += " · 失效 !"; name.color = ArgonTheme.Enemy; }
                var conditionText = RuleSegment("Condition", button.transform, "条件｜" + ConditionSummary(instruction.condition, state.catalog),
                    invalid ? ArgonTheme.Warning : ArgonTheme.Focus);
                PlaceText(conditionText, 5, 25, 31, 22);
                var scopeText = RuleSegment("Scope", button.transform, "范围｜" + OptionLabel(state.catalog.scopes, instruction.scope), ArgonTheme.Secondary);
                scopeText.rectTransform.anchorMin = new Vector2(0, 1); scopeText.rectTransform.anchorMax = new Vector2(.45f, 1);
                scopeText.rectTransform.offsetMin = new Vector2(5, -69); scopeText.rectTransform.offsetMax = new Vector2(0, -47);
                var selectorText = RuleSegment("Selector", button.transform, "选择｜" + OptionLabel(state.catalog.selectors, instruction.selector), ArgonTheme.Secondary);
                selectorText.rectTransform.anchorMin = new Vector2(.45f, 1); selectorText.rectTransform.anchorMax = Vector2.one;
                selectorText.rectTransform.offsetMin = new Vector2(0, -69); selectorText.rectTransform.offsetMax = new Vector2(-31, -47);
                var actionText = RuleSegment("Action", button.transform, "行动｜" + ActionLabel(state.catalog.actions.FirstOrDefault(action => action.id == instruction.actionId)), ArgonTheme.Brass);
                PlaceText(actionText, 5, 69, 31, 22);
                var up = ArgonUi.Button("MoveUp", button.transform, font, "↑", () => { shared.SelectInstruction(index); shared.Move(-1); }, 24, 24);
                AnchorRight(up.GetComponent<RectTransform>(), 3, 6, 24, 27);
                up.interactable = i > 0;
                var down = ArgonUi.Button("MoveDown", button.transform, font, "↓", () => { shared.SelectInstruction(index); shared.Move(1); }, 24, 24);
                AnchorRight(down.GetComponent<RectTransform>(), 3, 41, 24, 27);
                down.interactable = i < instructions.Length - 1;
                ruleButtons.Add(button);
            }
            if (instructions.Length == 0)
            {
                var empty = ArgonUi.Text("EmptyRules", ruleContent, font, "尚未装载指令。\n从空白完整选择四段，再新增。", 14, ArgonTheme.Muted);
                ArgonUi.Size(empty.gameObject, -1, 80, 1);
            }
        }

        private Button BuildUnitCard(Transform parent, CombatUnitSnapshot unit, bool friendly)
        {
            var button = ArgonUi.Button("Argon_Crew_" + unit.id, parent, font, "", friendly ? (UnityEngine.Events.UnityAction)(() => shared.SelectUnit(unit.id)) : null, -1, 84);
            UnityEngine.Object.Destroy(button.transform.Find("Label").gameObject);
            button.interactable = friendly;
            var sprite = shared.GetPortrait(unit.id);
            if (sprite != null)
            {
                var portrait = ArgonUi.Image("Portrait", button.transform, Color.white);
                portrait.sprite = sprite; portrait.preserveAspect = true;
                ArgonUi.Place(portrait.rectTransform, 4, 14, 50, 58);
            }
            var text = ArgonUi.Text("UnitFacts", button.transform, font, unit.name + "\nHP " + unit.hp + "/" + unit.maxHp + "\nMP " + unit.mp + "/" + unit.maxMp + " · " + ArgonBattlePage.Rank(unit.row), 14,
                friendly ? ArgonTheme.Ink : ArgonTheme.Secondary, true);
            PlaceText(text, sprite == null ? 5 : 57, 3, 3, 76);
            return button;
        }

        private void RefreshForm(TacticsCandidateState state)
        {
            var form = state.form;
            LabelInput.SetTextWithoutNotify(form.label ?? "");
            SetOption(OperationDropdown, state.catalog.operations, form.operation, "选择逻辑组合");
            SetOption(GroupingDropdown, state.catalog.groupings, form.grouping, "选择显式括号");
            SetBinary(LeftOperatorDropdown, form.operators != null && form.operators.Length > 0 ? form.operators[0] : "");
            SetBinary(RightOperatorDropdown, form.operators != null && form.operators.Length > 1 ? form.operators[1] : "");
            var mixed = form.operation == "mixed";
            GroupingDropdown.gameObject.SetActive(mixed);
            LeftOperatorDropdown.gameObject.SetActive(mixed);
            RightOperatorDropdown.gameObject.SetActive(mixed);
            expressionText.text = Expression(form);
            if (scopeButtons.Count != state.catalog.scopes.Length)
            {
                ArgonUi.Clear(scopeContent); scopeButtons.Clear();
                foreach (var scope in state.catalog.scopes)
                {
                    var id = scope.id;
                    scopeButtons.Add(ArgonUi.Button("Argon_Scope_" + id, scopeContent, font, scope.label,
                        () => Edit(input => input.scope = id), -1, 28));
                }
            }
            for (var i = 0; i < scopeButtons.Count; i++) ArgonUi.Selected(scopeButtons[i], state.catalog.scopes[i].id == form.scope);
            SetOption(SelectorDropdown, state.catalog.selectors, form.selector, "选择目标方式");
            scopeHint.text = OptionDescription(state.catalog.selectors, form.selector);
            var rawTerms = form.terms ?? new TacticsFormTerm[0];
            // Keep all three explicit rows and the mixed-logic parentheses at the
            // authored size, without reserving their empty space for one-term rules.
            ArgonUi.Size(conditionSection.gameObject, -1, 88 + 50 * Mathf.Clamp(rawTerms.Length, 1, 3), 1);
            var signature = form.selectedUnitId + ":" + rawTerms.Length + ":" + string.Join("|", rawTerms.Select(term => term.kind));
            if (formStructureSignature != signature)
            {
                formStructureSignature = signature;
                ArgonUi.Clear(termsContent); terms.Clear();
                for (var i = 0; i < rawTerms.Length; i++) BuildTerm(i, state);
            }
            for (var i = 0; i < terms.Count; i++)
            {
                var raw = rawTerms[i];
                var controls = terms[i];
                var option = state.catalog.conditions.FirstOrDefault(item => item.id == raw.kind);
                SetOption(controls.SubjectDropdown, state.catalog.subjects, raw.subject, "来源");
                controls.SubjectDropdown.GetComponent<Image>().color = raw.subject == "actor" ? new Color32(12, 66, 86, 255) : new Color32(75, 40, 33, 255);
                SetOption(controls.KindDropdown, state.catalog.conditions, raw.kind, "选择条件", true);
                controls.ThresholdInput.SetTextWithoutNotify(raw.thresholdText ?? "");
                controls.ValueInput.SetTextWithoutNotify(raw.value ?? "");
                controls.ThresholdInput.gameObject.SetActive(option != null && option.usesThreshold);
                controls.ValueInput.gameObject.SetActive(option != null && option.usesValue);
                controls.UnitText.gameObject.SetActive(option != null && option.usesThreshold);
                controls.UnitText.text = Unit(option);
                controls.HintText.text = TermHint(option, raw, i >= state.catalog.conditionCapacity);
                controls.HintText.color = i >= state.catalog.conditionCapacity ? ArgonTheme.Enemy : ArgonTheme.Muted;
            }
        }

        private void BuildTerm(int index, TacticsCandidateState state)
        {
            var root = ArgonUi.Column("Argon_Term_" + index, termsContent, 0);
            ArgonUi.Size(root.gameObject, -1, 46, 1);
            var row = ArgonUi.Row("Inputs", root, 3);
            ArgonUi.Size(row.gameObject, -1, 27, 1);
            var tag = ArgonUi.Text("TermIndex", row, font, ((char)('A' + index)).ToString(), 14, ArgonTheme.Brass, true);
            ArgonUi.Size(tag.gameObject, 17, 27);
            var controls = new ArgonConditionControls();
            controls.SubjectDropdown = ArgonUi.Dropdown("Argon_TermSubject_" + index, row, font, 90, 27);
            controls.KindDropdown = ArgonUi.Dropdown("Argon_TermKind_" + index, row, font, -1, 27);
            controls.ThresholdInput = ArgonUi.Input("Argon_TermThreshold_" + index, row, font, "阈值", 59, 27);
            controls.UnitText = ArgonUi.Text("ThresholdUnit", row, font, "", 12, ArgonTheme.Focus);
            ArgonUi.Size(controls.UnitText.gameObject, 30, 27);
            controls.ValueInput = ArgonUi.Input("Argon_TermValue_" + index, row, font, "状态/标签ID", 94, 27);
            controls.RemoveButton = ArgonUi.Button("Argon_RemoveTerm_" + index, row, font, "×", () => Edit(form => form.terms = form.terms.Where((term, termIndex) => termIndex != index).ToArray()), 23, 27);
            controls.HintText = ArgonUi.Text("MetadataAndUnitHint", root, font, "", 12, ArgonTheme.Muted);
            controls.HintText.textWrappingMode = TextWrappingModes.NoWrap;
            controls.HintText.overflowMode = TextOverflowModes.Ellipsis;
            ArgonUi.Size(controls.HintText.gameObject, -1, 19, 1);
            controls.SubjectDropdown.onValueChanged.AddListener(_ =>
                Pick(controls.SubjectDropdown, snapshot => snapshot.catalog.subjects, (form, id) => form.terms[index].subject = id));
            controls.KindDropdown.onValueChanged.AddListener(_ =>
            {
                if (refreshing) return;
                var snapshot = shared.GetState();
                var id = SelectedId(controls.KindDropdown, snapshot.catalog.conditions);
                var option = snapshot.catalog.conditions.FirstOrDefault(item => item.id == id);
                Edit(form =>
                {
                    form.terms[index].kind = id;
                    form.terms[index].thresholdText = option != null && option.usesThreshold ? option.defaultThreshold.ToString("R", CultureInfo.InvariantCulture) : "";
                    form.terms[index].value = option == null ? "" : option.defaultValue;
                });
            });
            controls.ThresholdInput.onValueChanged.AddListener(value => Edit(form => form.terms[index].thresholdText = value));
            controls.ValueInput.onValueChanged.AddListener(value => Edit(form => form.terms[index].value = value));
            terms.Add(controls);
        }

        private void RefreshActions(TacticsCandidateState state)
        {
            ActionSearchInput.SetTextWithoutNotify(state.form.actionSearch ?? "");
            ActionDetailsText.text = SelectedActionSummary(state);
            actionMatch.text = state.actionMatchText;
            var columns = ((RectTransform)transform).rect.width > 1500 ? 3 : 2;
            var signature = state.form.selectedUnitId + ":" + state.form.actionId + ":" + state.form.actionSearch + ":" + effectFilter + ":" + columns + ":" + state.workbench.draftScenario.id;
            if (actionSignature == signature) return;
            actionSignature = signature;
            actionColumns = columns;
            ArgonUi.Clear(filterContent);
            var all = ArgonUi.Button("FilterAll", filterContent, font, "全部", () => SetFilter(""), -1, 23);
            all.GetComponentInChildren<TMP_Text>().fontSize = 12;
            ArgonUi.Selected(all, string.IsNullOrEmpty(effectFilter));
            foreach (var kind in state.catalog.effectKinds.Where(option => state.catalog.actions.Any(action => action.effectKind == option.id)))
            {
                var id = kind.id;
                var filter = ArgonUi.Button("Filter_" + id, filterContent, font, kind.label, () => SetFilter(id), -1, 23);
                filter.GetComponentInChildren<TMP_Text>().fontSize = 12;
                ArgonUi.Selected(filter, id == effectFilter);
            }
            ArgonUi.Clear(actionContent); actionButtons.Clear();
            var actions = (state.visibleActions ?? new CombatActionConfig[0]).Where(action => string.IsNullOrEmpty(effectFilter) || action.effectKind == effectFilter || action.id == state.form.actionId).ToArray();
            // The shared filter deliberately retains the selected action. It is labelled, never silently replaced.
            var genuineMatches = (state.visibleActions ?? new CombatActionConfig[0]).Where(action =>
                !(state.selectedActionRetained && action.id == state.form.actionId) &&
                (string.IsNullOrEmpty(effectFilter) || action.effectKind == effectFilter)).Count();
            var noMatches = state.matchingActionCount == 0 || genuineMatches == 0;
            emptyPanel.gameObject.SetActive(noMatches);
            actionContent.parent.parent.gameObject.SetActive(!noMatches);
            emptyText.text = "当前角色白名单无匹配结果。\n已选行动仍保留在上方详情；可一键清空恢复。";
            for (var i = 0; i < actions.Length; i += actionColumns)
            {
                var row = ArgonUi.Row("SkillRow_" + i, actionContent, 5);
                ArgonUi.Size(row.gameObject, -1, 78, 1);
                for (var j = 0; j < actionColumns; j++)
                {
                    if (i + j >= actions.Length) { var gap = ArgonUi.Rect("EmptyGridSpace", row); ArgonUi.Size(gap.gameObject, -1, 78, 1); continue; }
                    var action = actions[i + j];
                    var button = ArgonUi.Button("Argon_Action_" + action.id, row, font, "", () => Edit(form => form.actionId = action.id), -1, 78);
                    UnityEngine.Object.Destroy(button.transform.Find("Label").gameObject);
                    ArgonUi.Selected(button, state.form.actionId == action.id);
                    var name = ArgonUi.Text("ActionName", button.transform, font, (state.form.actionId == action.id ? "已选 · " : "") + action.displayName, 15, ArgonTheme.Ink, true);
                    PlaceText(name, 4, 2, 4, 21);
                    var cost = ArgonUi.Text("CostAndWait", button.transform, font, "MP " + action.mpCost + " · WAIT " + ArgonBattlePage.Number(action.baseWait) + (action.isMagic ? " · 魔法" : " · 非魔法"), 14, ArgonTheme.Wait);
                    PlaceText(cost, 4, 24, 4, 20);
                    var mode = OptionLabel(state.catalog.targetModes, string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode);
                    var effect = OptionLabel(state.catalog.effectKinds, action.effectKind);
                    var detail = ArgonUi.Text("ScopeAndEffect", button.transform, font, mode + " · " + effect + " " +
                        (action.effectKind == "apply_status" ? OptionLabel(state.catalog.statusKinds, action.statusKind) + " / W" + ArgonBattlePage.Number(action.statusDuration) :
                        action.effectKind == "damage" || action.effectKind == "heal" || action.effectKind == "restore_mp" || action.effectKind == "drain_mp" ? "基础" + action.magnitude : ""), 12, ArgonTheme.Secondary);
                    PlaceText(detail, 4, 45, 4, 29);
                    detail.overflowMode = TextOverflowModes.Ellipsis;
                    actionButtons.Add(button);
                }
            }
        }

        private void Edit(Action<TacticsFormInput> mutation)
        {
            if (refreshing || shared == null) return;
            var input = shared.GetState().form;
            if (input == null) return;
            mutation(input);
            shared.SetForm(input);
        }

        private static string SelectedActionSummary(TacticsCandidateState state)
        {
            var action = (state.catalog.actions ?? new CombatActionConfig[0]).FirstOrDefault(item => item.id == state.form.actionId);
            if (action == null) return "请从当前角色白名单选择行动。\n费用、WAIT与效果读取实际技能目录。";
            var mode = OptionLabel(state.catalog.targetModes, string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode);
            var effect = OptionLabel(state.catalog.effectKinds, action.effectKind);
            var payload = action.effectKind == "apply_status"
                ? " · " + OptionLabel(state.catalog.statusKinds, action.statusKind) + " · 量 " + action.statusMagnitude + " · 持续 WAIT " + ArgonBattlePage.Number(action.statusDuration)
                : action.effectKind == "damage" || action.effectKind == "heal" || action.effectKind == "restore_mp" || action.effectKind == "drain_mp"
                    ? " · 基础量 " + action.magnitude : "";
            var magic = action.isMagic ? "魔法动作（受沉默影响）" : "非魔法动作";
            if (action.effectKind == "damage") magic += " · 伤害类型 " + OptionLabel(state.catalog.damageTypes,
                string.IsNullOrEmpty(action.damageType) ? "legacy" : action.damageType);
            return "已选：" + action.displayName + "｜MP " + action.mpCost + " · 基础 WAIT " + ArgonBattlePage.Number(action.baseWait) + " · " + mode +
                "\n效果：" + effect + payload + "\n" + magic + "；主目标→覆盖，费用/WAIT各一次。";
        }

        private void AddTerm()
        {
            Edit(form => form.terms = (form.terms ?? new TacticsFormTerm[0]).Concat(new[] { new TacticsFormTerm { kind = "", subject = "target", thresholdText = "", value = "" } }).ToArray());
        }

        private void Pick(TMP_Dropdown dropdown, Func<TacticsCandidateState, CombatTacticsOption[]> options,
            Action<TacticsFormInput, string> mutation)
        {
            if (refreshing) return;
            var state = shared.GetState();
            var id = SelectedId(dropdown, options(state));
            Edit(form => mutation(form, id));
        }

        private void PickBinary(int index, TMP_Dropdown dropdown)
        {
            Edit(form =>
            {
                var operators = form.operators ?? new string[0];
                form.operators = new[] { operators.Length > 0 ? operators[0] : "", operators.Length > 1 ? operators[1] : "" };
                form.operators[index] = dropdown.value == 1 ? "and" : dropdown.value == 2 ? "or" : "";
            });
        }

        private void SetFilter(string id) { effectFilter = id; actionSignature = null; Refresh(shared.GetState()); }
        private void ClearSearch() { effectFilter = ""; actionSignature = null; shared.SetActionSearch(""); Refresh(shared.GetState()); }

        private void OnRectTransformDimensionsChange()
        {
            // Rect callbacks may be raised inside CanvasUpdateRegistry.PerformUpdate.
            // Rebuilding action cards there would remove graphics from its live list.
            layoutRefreshPending = true;
        }

        private void LateUpdate()
        {
            if (!layoutRefreshPending || shared == null || LabelInput == null || !gameObject.activeInHierarchy ||
                CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            layoutRefreshPending = false;
            var columns = ((RectTransform)transform).rect.width > 1500 ? 3 : 2;
            if (columns != actionColumns) { actionSignature = null; Refresh(shared.GetState()); }
        }

        private void Heading(Transform parent, string value)
        {
            var text = ArgonUi.Text("Heading", parent, font, value, 16, ArgonTheme.Brass, true);
            ArgonUi.Size(text.gameObject, -1, 25, 1);
        }

        private TMP_Text RuleSegment(string name, Transform parent, string value, Color color)
        {
            var text = ArgonUi.Text(name, parent, font, value, 14, color);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static void PlaceText(TMP_Text text, float left, float top, float right, float height)
        {
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorRight(RectTransform rect, float right, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-right, -top); rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetOption(TMP_Dropdown dropdown, CombatTacticsOption[] options, string id, string placeholder, bool category = false)
        {
            options = options ?? new CombatTacticsOption[0];
            ArgonUi.Options(dropdown, new[] { placeholder }.Concat(options.Select(option => (category && !string.IsNullOrEmpty(option.category) ? Category(option.category) + " · " : "") +
                (option.id == "target" ? "候选目标" : option.label))), Array.FindIndex(options, option => option.id == id) + 1);
        }

        private static string SelectedId(TMP_Dropdown dropdown, CombatTacticsOption[] options)
        {
            return options != null && dropdown.value > 0 && dropdown.value <= options.Length ? options[dropdown.value - 1].id : "";
        }

        private static void SetBinary(TMP_Dropdown dropdown, string id)
        {
            ArgonUi.Options(dropdown, new[] { "连接", "且 AND", "或 OR" }, id == "and" ? 1 : id == "or" ? 2 : 0);
        }

        private static string Unit(CombatTacticsOption option)
        {
            if (option == null) return "";
            switch (option.thresholdUnit) { case "ratio": return "比例"; case "count": return "人"; case "points": return "点"; case "wait": return "WAIT"; case "row": return "排号"; default: return "值"; }
        }

        private static string TermHint(CombatTacticsOption option, TacticsFormTerm term, bool overloaded)
        {
            if (overloaded) return "超载未生效：条件项超过当前角色容量；请删减。";
            if (option == null) return "请显式选择来源与真实条件。";
            var hint = option.description;
            double value;
            if (option.thresholdUnit == "ratio") hint = (double.TryParse(term.thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? ArgonBattlePage.Number(value * 100) + "% · " : "") + "比例原值0..1（0.35=35%）";
            else if (option.usesThreshold) hint = Unit(option) + " " + ArgonBattlePage.Number(option.minThreshold) + ".." + (option.maxThreshold > int.MaxValue ? "∞" : ArgonBattlePage.Number(option.maxThreshold)) + (option.integerThreshold ? "，整数" : "") + " · " + hint;
            if (option.usesValue) hint += " · " + option.valueHint;
            return hint;
        }

        private static string Expression(TacticsFormInput form)
        {
            var labels = Enumerable.Range(0, (form.terms ?? new TacticsFormTerm[0]).Length).Select(index => ((char)('A' + index)).ToString()).ToArray();
            if (form.operation == "mixed")
            {
                var ops = form.operators ?? new string[0];
                var left = ops.Length > 0 ? Binary(ops[0]) : "?";
                var right = ops.Length > 1 ? Binary(ops[1]) : "?";
                return form.grouping == "left" ? "( A " + left + " B ) " + right + " C" : form.grouping == "right" ? "A " + left + " ( B " + right + " C )" : "请选择明确括号分组";
            }
            return labels.Length == 0 ? "从空白添加条件项" : form.operation == "all" ? string.Join(" 且 ", labels) : form.operation == "any" ? string.Join(" 或 ", labels) : "请选择条件逻辑组合";
        }

        private static string ConditionSummary(CombatCondition condition, TacticsCatalogSnapshot catalog)
        {
            if (condition == null) return "未填";
            var texts = (condition.terms ?? new CombatConditionTerm[0]).Select(term =>
            {
                var option = catalog.conditions.FirstOrDefault(item => item.id == term.kind);
                return (term.subject == "actor" ? "施放者" : "目标") + (option == null ? term.kind : option.label) +
                    (option != null && option.usesThreshold ? option.thresholdUnit == "ratio" ? ArgonBattlePage.Number(term.threshold * 100) + "%" : ArgonBattlePage.Number(term.threshold) + Unit(option) : "") +
                    (option != null && option.usesValue ? "[" + term.value + "]" : "");
            }).ToArray();
            if (condition.operation == "mixed" && texts.Length == 3 && condition.operators != null && condition.operators.Length == 2)
                return condition.grouping == "left" ? "(" + texts[0] + Binary(condition.operators[0]) + texts[1] + ")" + Binary(condition.operators[1]) + texts[2] : texts[0] + Binary(condition.operators[0]) + "(" + texts[1] + Binary(condition.operators[1]) + texts[2] + ")";
            return string.Join(condition.operation == "any" ? " 或 " : " 且 ", texts);
        }

        private static string Binary(string id) { return id == "and" ? "且" : id == "or" ? "或" : "?"; }
        private static string ActionLabel(CombatActionConfig action) { return action == null ? "未选行动" : action.displayName + " · MP" + action.mpCost + " / W" + ArgonBattlePage.Number(action.baseWait); }
        private static string OptionLabel(CombatTacticsOption[] options, string id) { var option = (options ?? new CombatTacticsOption[0]).FirstOrDefault(item => item.id == id); return option == null ? id ?? "未选" : option.label; }
        private static string OptionDescription(CombatTacticsOption[] options, string id) { var option = (options ?? new CombatTacticsOption[0]).FirstOrDefault(item => item.id == id); return option == null ? "先选主目标，再依技能覆盖方式扩展；费用WAIT各一次。" : option.description; }
        private static string Category(string id) { switch (id) { case "basic": return "基础"; case "resource": return "资源"; case "position": return "站位"; case "status": return "状态"; case "tag": return "标签"; case "team": return "人数"; case "time": return "时间"; case "relation": return "关系"; default: return id; } }
    }
}
