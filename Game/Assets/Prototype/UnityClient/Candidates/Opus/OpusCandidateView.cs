using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Opus.OpusTheme;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    [TacticsCandidate("opus", "C · 刻碑指挥台")]
    public sealed partial class OpusCandidateView : TacticsCandidateView
    {
        private TMP_FontAsset font;
        private TacticsSharedController shared;
        private TacticsCandidateState state;
        private bool pendingRefresh, rendering, battlePage;
        private RectTransform body, assembly, combat, commitBar, roster, ruleColumn, codex;
        private RectTransform rosterList, rulesList;
        private TMP_Text caseTitle, capacity, formState, liveState;
        private string rosterKey, rulesKey;
        private readonly List<Button> rosterButtons = new List<Button>();
        private readonly List<Button> ruleButtons = new List<Button>();
        private RectTransform modal;
        private TMP_Text modalText;
        private TMP_InputField modalPath;
        private Button modalAccept;
        private Action modalAction;
        private OpusCatalogPopup popup;
        private RectTransform tooltip;
        private TMP_Text tooltipText;
        private float previousWidth = -1;
        private Vector2 observedBodySize = new Vector2(-1, -1);

        public Button EditorTab { get; private set; }
        public Button BattleTab { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public Button CompareButton { get; private set; }
        public Button ExportButton { get; private set; }
        public Button SendButton { get; private set; }
        public Button ResetButton { get; private set; }
        public Button NewBlankButton { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button MoveUpButton { get; private set; }
        public Button MoveDownButton { get; private set; }
        public Button ReloadButton { get; private set; }
        public TMP_Dropdown CaseDropdown { get; private set; }
        public TMP_Text CommitStateText { get; private set; }
        public TMP_Text FeedbackText { get; private set; }
        public IReadOnlyList<Button> RuleButtons { get { return ruleButtons; } }
        public IReadOnlyList<Button> RosterButtons { get { return rosterButtons; } }
        public bool IsBattlePage { get { return battlePage; } }
        public Button ConfirmButton { get { return modalAccept; } }
        public Button DismissModalButton { get; private set; }
        public bool IsModalVisible { get { return modal != null && modal.gameObject.activeInHierarchy; } }
        public TMP_InputField ExportPathInput { get { return modalPath; } }

        public override void Initialize(TMP_FontAsset uiFont, TacticsSharedController controller)
        {
            font = uiFont; shared = controller;
            var background = gameObject.AddComponent<Image>(); background.color = T.Background;
            var root = OpusUi.Column("Opus_InscribedCommandDesk", transform, 8, 12); Ui.Stretch(root);
            var header = OpusUi.Row("Opus_LocalTopBar", root, 8); Ui.FixedHeight(header.gameObject, 44);
            var brand = OpusUi.Text("Opus_Brand", header, font, "刻碑指挥台\n条件指令 × 基地经营", 15, T.Ink);
            Ui.Width(brand.gameObject, 188);
            EditorTab = OpusUi.Button("Opus_EditorTab", header, font, "Ⅰ 战前编排", () => ShowPage(false), 150, 40);
            BattleTab = OpusUi.Button("Opus_BattleTab", header, font, "Ⅱ 战斗播放", () => ShowPage(true), 150, 40);
            liveState = OpusUi.Text("Opus_AlwaysVisibleLiveState", header, font, "", 13, T.Secondary);
            liveState.alignment = TextAlignmentOptions.MidlineLeft; Ui.Flexible(liveState.gameObject);
            ReplayButton = OpusUi.Button("Opus_ReplayCommitted", header, font, "同开局重放", Replay, 126, 36);
            body = OpusUi.Rect("Opus_Pages", root); Ui.Flexible(body.gameObject);
            assembly = OpusUi.Rect("Opus_InscriptionPage", body); Ui.Stretch(assembly);
            combat = OpusUi.Column("Opus_ObservationPage", body, 8, 0); Ui.Stretch(combat);
            BuildAssembly(); BuildBattle();
            commitBar = OpusUi.Row("Opus_ThreeLayerCommitBar", root, 12); Ui.FixedHeight(commitBar.gameObject, 56);
            var statusColumn = OpusUi.Column("Opus_FormDraftCommitted", commitBar, 1, 0); Ui.Flexible(statusColumn.gameObject);
            CommitStateText = OpusUi.Label("Opus_CommitChain", statusColumn, font, "", 13, 25);
            FeedbackText = OpusUi.Text("Opus_OperationFeedback", statusColumn, font, "", 12, T.Secondary, true);
            Ui.FixedHeight(FeedbackText.gameObject, 24);
            ApplyButton = OpusUi.Button("Opus_ApplyBattle", commitBar, font, "应用并开战", Apply, 180, 40, true);
            BuildModal();
            var popupRect = OpusUi.Rect("Opus_SearchableCatalogOverlay", transform); Ui.Stretch(popupRect);
            popup = popupRect.gameObject.AddComponent<OpusCatalogPopup>(); popup.Initialize(font);
            tooltip = OpusUi.Panel("Opus_ReadableTooltip", transform, T.Top).rectTransform;
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(.5f, .5f); tooltip.pivot = new Vector2(0, 1);
            tooltipText = OpusUi.Text("TooltipText", tooltip, font, "", 13); Ui.Stretch(tooltipText.rectTransform, 10, 8, 10, 8);
            tooltip.gameObject.SetActive(false);
            Tip(ReplayButton.gameObject, "使用最近一次上场配置的相同开局重放；草稿与表单修改不会自动带入。");
            pendingRefresh = true;
        }

        // Host callbacks may arrive while a Canvas is rebuilding. They only mark dirtiness.
        public override void RefreshView() { pendingRefresh = true; }
        private void OnRectTransformDimensionsChange() { pendingRefresh = true; }
        private void OnEnable() { pendingRefresh = true; }
        private void OnDisable() { shared?.Pause(); if (popup != null) popup.Close(); HideTooltip(); }
        private void Update()
        {
            HandleEscape();
            if (shared == null || body == null || CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            // The child LayoutGroup can finish sizing body without changing this root.
            // Observe that actual viewport here, never mutate children from a Canvas callback.
            var measured = body.rect.size;
            if (Mathf.Abs(measured.x - observedBodySize.x) > .1f || Mathf.Abs(measured.y - observedBodySize.y) > .1f)
            {
                observedBodySize = measured;
                pendingRefresh = true;
            }
            if (!pendingRefresh) return;
            // A newly created RectTransform still has its 100px default before the first
            // layout pass. Do not cache that width as the three-column presentation.
            float expectedWidth = Mathf.Max(0, ((RectTransform)transform).rect.width - 24);
            if (measured.x < expectedWidth - .5f) return;
            pendingRefresh = false; Render();
        }

        private void Render()
        {
            state = shared.GetState(); if (state?.form == null || state.workbench == null) return;
            rendering = true;
            try
            {
                LayoutAssembly();
                assembly.gameObject.SetActive(!battlePage); combat.gameObject.SetActive(battlePage); commitBar.gameObject.SetActive(!battlePage);
                EditorTab.GetComponent<Image>().color = !battlePage ? T.Raised : T.Top;
                BattleTab.GetComponent<Image>().color = battlePage ? T.Raised : T.Top;
                string applied = state.workbench.hasCommittedBattle ? "上场 v" + state.workbench.committedRevision + " · "
                    + (state.battle.finished ? "已结束" : state.isAutoPlaying ? "播放中" : "已暂停") + " · WAIT " + OpusUi.N(state.battle.time) : "尚未开战";
                liveState.text = applied + "\n" + (state.form.hasUnsubmittedChanges ? "● 表单未写入草稿" : "○ 表单无修改")
                    + (state.workbench.hasUncommittedChanges ? " · 草稿 v" + state.workbench.draftRevision + " 未上场" : "");
                CommitStateText.text = (state.form.hasUnsubmittedChanges ? "● 表单已修改" : "○ 表单无修改") + "  →  草稿 v" + state.workbench.draftRevision
                    + (state.workbench.hasUncommittedChanges ? " · 待上场" : " · 已同步") + "  →  " + applied;
                CommitStateText.color = state.form.hasUnsubmittedChanges || state.workbench.hasUncommittedChanges ? T.Amber : T.Green;
                FeedbackText.text = state.feedback;
                ReplayButton.interactable = state.workbench.hasCommittedBattle;
                if (!CaseDropdown.options.Select(option => option.text).SequenceEqual(shared.CaseTitles)) TacticsUiControls.Options(CaseDropdown, shared.CaseTitles, shared.SelectedCaseIndex);
                else CaseDropdown.SetValueWithoutNotify(shared.SelectedCaseIndex);
                caseTitle.text = OpusUi.Esc(state.workbench.draftScenario.title) + "\nseed " + state.workbench.draftScenario.seed;
                Tip(CaseDropdown.gameObject, state.workbench.draftScenario.title);
                RenderRoster(); RenderRules(); RenderForm(); RenderCodex(); RenderBattle();
            }
            finally { rendering = false; }
        }

        private void ShowPage(bool battle)
        {
            if (battlePage && !battle) shared.Pause();
            battlePage = battle; pendingRefresh = true;
        }
        private void Apply() { if (shared.CommitBattle()) { battlePage = true; pendingRefresh = true; } }
        private void Replay() { shared.Replay(); if (shared.GetState().workbench.hasCommittedBattle) ShowPage(true); }
        private void SelectSafely(Action action)
        {
            if (!shared.GetState().form.hasUnsubmittedChanges) { action(); return; }
            Confirm("表单尚未写入草稿。丢弃表单修改后切换？", () => { shared.LoadSelected(); action(); });
        }
        private void Mutate(Action<TacticsFormInput> action)
        {
            if (rendering) return;
            var form = shared.GetState().form; action(form); shared.SetForm(form);
        }

        private void BuildAssembly()
        {
            roster = OpusUi.Column("Opus_RosterRail", assembly, 6, 8, T.Panel);
            ruleColumn = OpusUi.Column("Opus_RuleColumn", assembly, 6, 8, T.Panel);
            codex = OpusUi.Column("Opus_Codex", assembly, 6, 8, T.Panel);
            caseTitle = OpusUi.Label("Opus_FullCaseTitle", roster, font, "", 13, 62);
            CaseDropdown = OpusUi.Dropdown("Opus_Case", roster, font);
            CaseDropdown.onValueChanged.AddListener(index => { if (!rendering) SelectSafely(() => { shared.SelectCase(index); Invalidate(); }); });
            OpusUi.Label("Opus_RosterTitle", roster, font, "队员 · 开局状态", 15, 27);
            rosterList = OpusUi.Scroll("Opus_RosterCards", roster, out _);
            OpusUi.Label("Opus_ToolsTitle", roster, font, "次级工具", 12, 20);
            CompareButton = OpusUi.Button("Opus_Compare", roster, font, "单项对照", () =>
            { if (shared.Compare()) Info("对照基准为实际加载战例；不推进当前实战。\n\n" + shared.GetState().comparisonText); });
            ExportButton = OpusUi.Button("Opus_Export", roster, font, "导出输入与战报", () =>
            { string path = shared.Export(); if (path != null) Info("已导出当前表单、草稿、上场与实际战报。不补跑战斗，不发奖。", path); });
            SendButton = OpusUi.Button("Opus_SendExpedition", roster, font, "发送战术到出征", () => Confirm(
                "发送当前草稿战术与必要行动定义到出征。保留营地钱包、账本以及出征角色 HP/MP、智力和装备。", () => shared.SendToExpedition()));
            ResetButton = OpusUi.Button("Opus_Reset", roster, font, "重置本战例", () => Confirm(
                "恢复本战例实际加载开局，清空当前草稿、上场与战斗记录；已有导出文件保留。", () => { shared.Reset(); battlePage = false; Invalidate(); }));

            capacity = OpusUi.Label("Opus_RuleStackHeader", ruleColumn, font, "", 15, 34);
            rulesList = OpusUi.Scroll("Opus_InscribedRuleStack", ruleColumn, out _, 6);
            Ui.FixedHeight(rulesList.parent.parent.gameObject, 310);
            var tools = OpusUi.Row("Opus_StackTools", ruleColumn, 4); Ui.FixedHeight(tools.gameObject, 32);
            NewBlankButton = OpusUi.Button("Opus_NewBlank", tools, font, "新建空白", () => SelectSafely(() => shared.NewBlank()), 0, 32);
            MoveUpButton = OpusUi.Button("Opus_MoveUp", tools, font, "↑", () => shared.Move(-1), 32, 32);
            MoveDownButton = OpusUi.Button("Opus_MoveDown", tools, font, "↓", () => shared.Move(1), 32, 32);
            DeleteButton = OpusUi.Button("Opus_Delete", tools, font, "删除", () => shared.Delete(), 62, 32);
            ReloadButton = OpusUi.Button("Opus_Reload", tools, font, "重载所选", () => SelectSafely(() => shared.LoadSelected()), 116, 32);
            BuildForm(ruleColumn); BuildCodex(codex);
        }

        private void LayoutAssembly()
        {
            float width = body.rect.width;
            if (Mathf.Abs(width - previousWidth) < .1f) return;
            previousWidth = width;
            float extra = Mathf.Clamp01((width - 1248) / 320);
            float left = 220 + extra * 40, right = 300 + extra * 80;
            SetColumn(roster, 0, left); SetColumn(codex, width - right, right);
            SetColumn(ruleColumn, left + 12, Mathf.Max(400, width - left - right - 24));
            LayoutBattle(width);
        }
        private static void SetColumn(RectTransform rect, float x, float width)
        {
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, .5f);
            rect.offsetMin = new Vector2(x, 0); rect.offsetMax = new Vector2(x + width, 0);
        }
        private void Invalidate() { rosterKey = rulesKey = null; previousWidth = -1; InvalidateForm(); pendingRefresh = true; }

        private void RenderRoster()
        {
            var scenario = state.workbench.draftScenario;
            string key = scenario.id + ":" + state.workbench.draftRevision + ":" + state.form.selectedUnitId;
            if (key == rosterKey) return; rosterKey = key; OpusUi.Clear(rosterList); rosterButtons.Clear();
            foreach (var unit in state.workbench.draftPreview.units.Where(unit => unit.team == scenario.playerTeam))
            {
                string id = unit.id;
                var button = OpusUi.Button("Opus_Roster_" + id, rosterList, font, "", () => SelectSafely(() => shared.SelectUnit(id)), 0, 142);
                button.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
                var content = OpusUi.Column("RosterContent", button.transform, 3, 8); Ui.Stretch(content);
                var top = OpusUi.Row("NameAndPortrait", content); Ui.FixedHeight(top.gameObject, 48);
                OpusUi.Sprite("Portrait", top, font, shared.GetPortrait(id), unit.name.Substring(0, 1), 48);
                var name = OpusUi.Text("Name", top, font, OpusUi.Esc(unit.name) + "\n(" + unit.row + "," + unit.column + ")", 15); Ui.Flexible(name.gameObject);
                var info = OpusUi.Text("CapacityAndOpeningResources", content, font, "智力 " + unit.intelligence + " · 条件≤" + unit.conditionTermCapacity + "/条\n"
                    + new string('■', unit.instructionCapacity) + " " + unit.instructions.Length + "/" + unit.instructionCapacity
                    + "\n开局 HP " + unit.hp + "/" + unit.maxHp + " · MP " + unit.mp + "/" + unit.maxMp, 13, T.Secondary);
                Ui.Flexible(info.gameObject);
                button.GetComponent<Image>().color = id == state.form.selectedUnitId ? T.Raised : T.Panel;
                rosterButtons.Add(button);
            }
        }

        private void RenderRules()
        {
            var unit = state.workbench.draftScenario.units.FirstOrDefault(unit => unit.id == state.form.selectedUnitId);
            var preview = state.workbench.draftPreview.units.FirstOrDefault(unit => unit.id == state.form.selectedUnitId);
            if (unit == null || preview == null) return;
            bool selected = state.form.selectedInstructionIndex >= 0;
            bool full = unit.instructions.Length >= preview.instructionCapacity;
            capacity.text = "指令栈 · 自上而下尝试   " + new string('■', preview.instructionCapacity) + " " + unit.instructions.Length + "/" + preview.instructionCapacity
                + " · 条件≤" + preview.conditionTermCapacity + (full ? " · 已满" : "");
            capacity.color = full ? T.Amber : T.Ink;
            MoveUpButton.interactable = selected && state.form.selectedInstructionIndex > 0;
            MoveDownButton.interactable = selected && state.form.selectedInstructionIndex < unit.instructions.Length - 1;
            DeleteButton.interactable = ReloadButton.interactable = selected;
            string key = state.workbench.draftScenario.id + ":" + unit.id + ":" + state.workbench.draftRevision + ":" + state.form.selectedInstructionIndex;
            if (key == rulesKey) return; rulesKey = key; OpusUi.Clear(rulesList); ruleButtons.Clear();
            for (int i = 0; i < unit.instructions.Length; i++)
            {
                int index = i; var rule = unit.instructions[i]; var visible = preview.instructions[i];
                var button = OpusUi.Button("Opus_Rule_" + i, rulesList, font, "", () => SelectSafely(() => { shared.SelectInstruction(index); activeSegment = 0; }), 0, 52);
                button.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
                var badge = OpusUi.Panel("PriorityBadge", button.transform, i == state.form.selectedInstructionIndex ? T.Green : T.StrongLine);
                OpusUi.FixedRect(badge.rectTransform, 4, 8, 36, 36);
                var number = OpusUi.Text("Priority", badge.transform, font, (i + 1).ToString(), 20, i == state.form.selectedInstructionIndex ? T.Top : T.Ink, true); Ui.Stretch(number.rectTransform); number.alignment = TextAlignmentOptions.Center;
                var title = OpusUi.Text("RuleLabel", button.transform, font, OpusUi.Esc(rule.label), 15, T.Ink, true);
                OpusUi.Band(title.rectTransform, 48, 2, 122, 24);
                var summary = OpusUi.Text("RuleSentence", button.transform, font, OpusUi.Esc(visible.display), 13, T.Secondary, true);
                OpusUi.Band(summary.rectTransform, 48, 28, 6, 22);
                var action = state.workbench.draftScenario.actions.FirstOrDefault(item => item.id == rule.actionId);
                var cost = OpusUi.Text("ActualActionCost", button.transform, font,
                    action == null ? "行动缺失" : "MP " + action.mpCost + " W " + OpusUi.N(action.baseWait) + " " + ModeShort(action.targetMode), 12, T.Secondary, true);
                cost.rectTransform.anchorMin = cost.rectTransform.anchorMax = Vector2.one; cost.rectTransform.pivot = Vector2.one;
                cost.rectTransform.anchoredPosition = new Vector2(-4, -2); cost.rectTransform.sizeDelta = new Vector2(116, 24); cost.alignment = TextAlignmentOptions.MidlineRight;
                button.GetComponent<Image>().color = !visible.capacityAllowed || !visible.actionAvailable ? new Color32(58, 26, 26, 255) : i == state.form.selectedInstructionIndex ? T.Raised : T.Panel;
                ruleButtons.Add(button);
                Tip(button.gameObject, visible.display + (string.IsNullOrEmpty(visible.validationReason) ? "" : "\n" + visible.validationReason));
            }
        }

        private void BuildModal()
        {
            modal = OpusUi.Rect("Opus_SecondaryToolModal", transform); Ui.Stretch(modal);
            var shade = modal.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .6f);
            var box = OpusUi.Column("Dialog", modal, 10, 16, T.Panel);
            box.anchorMin = new Vector2(.12f, .15f); box.anchorMax = new Vector2(.88f, .85f); box.offsetMin = box.offsetMax = Vector2.zero;
            modalText = OpusUi.ScrollText("ActualToolResult", box, font, out _, 15);
            modalPath = OpusUi.Input("ExportPathCopy", box, font); modalPath.readOnly = true;
            var commands = OpusUi.Row("DialogCommands", box); Ui.FixedHeight(commands.gameObject, 40);
            DismissModalButton = OpusUi.Button("Opus_CloseDialog", commands, font, "取消 / 关闭", () => modal.gameObject.SetActive(false), 150, 36);
            modalAccept = OpusUi.Button("Opus_ConfirmDialog", commands, font, "确认", () => { var action = modalAction; modal.gameObject.SetActive(false); action?.Invoke(); }, 150, 36, true);
            modal.gameObject.SetActive(false);
        }
        private void Confirm(string message, Action action) { modalText.text = message; modalPath.gameObject.SetActive(false); modalAccept.gameObject.SetActive(true); modalAction = action; modal.gameObject.SetActive(true); modal.SetAsLastSibling(); }
        private void Info(string message, string path = null) { modalText.text = message; modalPath.gameObject.SetActive(path != null); modalPath.SetTextWithoutNotify(path ?? ""); modalAccept.gameObject.SetActive(false); modalAction = null; modal.gameObject.SetActive(true); modal.SetAsLastSibling(); }
        private void Tip(GameObject target, string value)
        {
            var component = target.GetComponent<OpusTooltip>() ?? target.AddComponent<OpusTooltip>(); component.Configure(this, value);
        }
        internal void HideTooltip() { if (tooltip != null) tooltip.gameObject.SetActive(false); }
        internal void DisplayTooltip(string value, RectTransform target)
        {
            if (tooltip == null || !isActiveAndEnabled) return;
            tooltipText.text = OpusUi.Esc(value);
            float height = Mathf.Clamp(tooltipText.GetPreferredValues(tooltipText.text, 300, 0).y + 20, 44, 260);
            tooltip.sizeDelta = new Vector2(320, height);
            var host = (RectTransform)transform; var point = host.InverseTransformPoint(target.position);
            tooltip.anchoredPosition = new Vector2(Mathf.Clamp(point.x, host.rect.xMin + 8, host.rect.xMax - 328), Mathf.Clamp(point.y, host.rect.yMin + height + 8, host.rect.yMax - 8));
            tooltip.gameObject.SetActive(true); tooltip.SetAsLastSibling();
        }
        private void HandleEscape()
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            pressed = UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (!pressed) return;
            HideTooltip();
            if (popup != null && popup.gameObject.activeInHierarchy) popup.Close();
            else if (modal != null && modal.gameObject.activeInHierarchy) modal.gameObject.SetActive(false);
            else if (inspector != null && inspector.gameObject.activeInHierarchy && shared != null) shared.SelectEvent(shared.GetState().selectedEventIndex);
        }
        private static string ModeShort(string mode) { return mode == "all" ? "全" : mode == "row" ? "排" : mode == "column" ? "列" : "单"; }
    }
}
