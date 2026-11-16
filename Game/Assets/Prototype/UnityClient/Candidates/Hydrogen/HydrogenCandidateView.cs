using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Hydrogen.HydrogenThemeTokens;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    [TacticsCandidate("hydrogen", "B · 星象秘库")]
    public sealed partial class HydrogenCandidateView : TacticsCandidateView
    {
        private TMP_FontAsset font;
        private TacticsSharedController shared;
        private TacticsCandidateState state;
        private bool refreshing;
        private bool battlePage;
        private RectTransform assemblyRoot;
        private RectTransform battleRoot;
        private RectTransform comparisonRoot;
        private RectTransform switchGuard;
        private RectTransform shell;
        private RectTransform headerBand;
        private RectTransform feedbackRegion;
        private ScrollRect footerScroll;
        private bool compactLayout;
        private bool responsiveShellApplied;
        private bool layoutRefreshPending;
        private Action pendingSwitch;
        private TMP_Text comparisonText;
        private string partySignature;
        private string ruleSignature;
        private RectTransform partyList;
        private RectTransform ruleList;
        private TMP_Text caseTitle;
        private TMP_Text capacityText;
        private TMP_Text fullSlotText;
        private TMP_Text formTitle;
        private readonly List<Button> unitButtons = new List<Button>();
        private readonly List<Button> instructionButtons = new List<Button>();
        private Button newBlank;
        private Button insert;
        private Button replace;
        private Button discard;
        private TMP_Dropdown scenarioDropdown;

        public Button AssemblyTabButton { get; private set; }
        public Button BattleTabButton { get; private set; }
        public Button ApplyBattleButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public Button CompareButton { get; private set; }
        public Button ExportButton { get; private set; }
        public Button SendExpeditionButton { get; private set; }
        public Button ResetButton { get; private set; }
        public Button NewBlankButton { get { return newBlank; } }
        public Button InsertButton { get { return insert; } }
        public Button ReplaceButton { get { return replace; } }
        public Button DiscardFormButton { get { return discard; } }
        public IReadOnlyList<Button> UnitButtons { get { return unitButtons; } }
        public IReadOnlyList<Button> InstructionButtons { get { return instructionButtons; } }
        public TMP_Text RevisionText { get; private set; }
        public TMP_Text FeedbackText { get; private set; }
        public TMP_Text CapacityText { get { return capacityText; } }
        public TMP_Text FullSlotText { get { return fullSlotText; } }
        public TMP_Dropdown ScenarioDropdown { get { return scenarioDropdown; } }
        public bool IsBattlePage { get { return battlePage; } }

        public override void Initialize(TMP_FontAsset uiFont, TacticsSharedController controller)
        {
            font = uiFont;
            shared = controller;
            var background = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            background.color = T.Abyss;
            var root = HydrogenUi.Column("Hydrogen_StarArchive", transform, 8, 12);
            shell = root;
            Ui.Stretch(root);
            var band = HydrogenUi.Row("Hydrogen_PageAndRevisionBand", root, 8);
            headerBand = band;
            Ui.FixedHeight(band.gameObject, 56);
            AssemblyTabButton = HydrogenUi.Button("Hydrogen_AssemblyTab", band, font, "① 战前编排", () => RequestSwitch(ShowAssembly), 126);
            BattleTabButton = HydrogenUi.Button("Hydrogen_BattleTab", band, font, "② 战斗回溯", () => RequestSwitch(ShowBattlePaused), 126);
            RevisionText = HydrogenUi.Text("Hydrogen_ThreeStateCapsules", band, font, "", 14, T.Secondary);
            RevisionText.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Flexible(RevisionText.gameObject);
            ReplayButton = HydrogenUi.Button("Hydrogen_ReplayCommitted", band, font, "同开局重放", Replay, 116);
            ApplyBattleButton = HydrogenUi.Button("Hydrogen_CommitAndBattle", band, font, "应用并开战 ▶", Apply, 146, true);

            switchGuard = HydrogenUi.Row("Hydrogen_UnsubmittedSwitchGuard", root, 7);
            Ui.FixedHeight(switchGuard.gameObject, 35);
            var guardText = HydrogenUi.Text("Hydrogen_SwitchWarning", switchGuard, font, "表单未提交：保存替换、丢弃修改或取消此次切换。", 14, T.Amber);
            Ui.Flexible(guardText.gameObject);
            HydrogenUi.Button("Hydrogen_SaveAndSwitch", switchGuard, font, "保存再切换", SaveAndSwitch, 115);
            HydrogenUi.Button("Hydrogen_DiscardAndSwitch", switchGuard, font, "丢弃再切换", DiscardAndSwitch, 115);
            HydrogenUi.Button("Hydrogen_CancelSwitch", switchGuard, font, "取消", CancelSwitch, 60);
            switchGuard.gameObject.SetActive(false);

            var pages = HydrogenUi.Rect("Hydrogen_FullWidthPages", root);
            Ui.Flexible(pages.gameObject);
            assemblyRoot = HydrogenUi.Rect("Hydrogen_AssemblyDeck", pages);
            Ui.Stretch(assemblyRoot);
            battleRoot = HydrogenUi.Rect("Hydrogen_CombatStage", pages);
            Ui.Stretch(battleRoot);
            comparisonRoot = HydrogenUi.Column("Hydrogen_ComparisonOverlay", pages, 8, 10, T.Panel);
            Ui.Stretch(comparisonRoot);
            var comparisonTop = HydrogenUi.Row("Hydrogen_ComparisonTitle", comparisonRoot);
            Ui.FixedHeight(comparisonTop.gameObject, 34);
            var comparisonHeading = HydrogenUi.Text("Hydrogen_ComparisonHeading", comparisonTop, font, "单项对照 · 独立运行，不推进当前战斗", 16, T.Gold);
            Ui.Flexible(comparisonHeading.gameObject);
            HydrogenUi.Button("Hydrogen_CloseComparison", comparisonTop, font, "返回当前页", () => { comparisonRoot.gameObject.SetActive(false); }, 120);
            comparisonText = HydrogenUi.ScrollText("Hydrogen_ActualComparison", comparisonRoot, font, out _, 14);
            comparisonRoot.gameObject.SetActive(false);
            BuildAssembly();
            BuildBattle();

            FeedbackText = HydrogenUi.ScrollText("Hydrogen_FeedbackAndExportPath", root, font, out var feedbackScroll, 14);
            footerScroll = feedbackScroll;
            feedbackRegion = feedbackScroll.GetComponent<RectTransform>();
            Ui.FixedHeight(feedbackScroll.gameObject, 34);
            RefreshView();
        }

        private void OnDisable() { shared?.Pause(); }
        private void OnRectTransformDimensionsChange() { layoutRefreshPending = true; }
        private void Update()
        {
            if (shared == null || feedbackRegion == null
                || CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            if (layoutRefreshPending)
            {
                layoutRefreshPending = false;
                RefreshView();
            }
            if (battleRoot.gameObject.activeInHierarchy) RefreshReasonViewport();
        }

        private void RefreshResponsiveShell()
        {
            // With the shared .5 scaler, 1920x1080 has ~765 logical px after nav,
            // while the 1280x900 reference has 860. Keep the reference layout intact.
            float height = ((RectTransform)transform).rect.height;
            bool compact = height > 0 && height < 844;
            if (responsiveShellApplied && compact == compactLayout) return;
            responsiveShellApplied = true;
            compactLayout = compact;
            Ui.FixedHeight(headerBand.gameObject, compact ? 44 : 56);
            Ui.FixedHeight(feedbackRegion.gameObject, compact ? 28 : 34);
            shell.GetComponent<VerticalLayoutGroup>().spacing = compact ? 6 : 8;
            // 28 - 2 - 2 gives the 14pt Noto feedback line 24px of real viewport.
            Ui.Stretch(footerScroll.viewport, 10, compact ? 2 : 8, 10, compact ? 2 : 8);
        }

        public override void RefreshView()
        {
            if (shared == null || font == null) return;
            RefreshResponsiveShell();
            state = shared.GetState();
            if (state?.workbench == null || state.form == null) return;
            refreshing = true;
            try
            {
                RevisionText.text = "表单：" + (state.form.hasUnsubmittedChanges ? "未提交修改" : "已写入草稿")
                    + "   草稿 v" + state.workbench.draftRevision + "\n"
                    + (!state.workbench.hasCommittedBattle ? "尚未开战" : "本场 v" + state.workbench.committedRevision
                        + (state.workbench.hasUncommittedChanges ? " · 草稿待应用，重放仍用本场配置" : " · 已应用"));
                RevisionText.color = state.form.hasUnsubmittedChanges || state.workbench.hasUncommittedChanges ? T.Amber : T.Secondary;
                FeedbackText.text = state.feedback + (string.IsNullOrEmpty(state.exportDirectory) ? "" : "\n导出：" + state.exportDirectory);
                ReplayButton.interactable = state.workbench.hasCommittedBattle;
                var titles = shared.CaseTitles.Select(ShortCaseTitle).ToArray();
                if (!scenarioDropdown.options.Select(option => option.text).SequenceEqual(titles))
                    TacticsUiControls.Options(scenarioDropdown, titles, shared.SelectedCaseIndex);
                else scenarioDropdown.SetValueWithoutNotify(shared.SelectedCaseIndex);
                caseTitle.text = HydrogenUi.Esc(state.workbench.draftScenario.title) + "\nseed "
                    + state.workbench.draftScenario.seed + " · " + state.workbench.draftScenario.id;
                RefreshParty();
                RefreshRules();
                RefreshForm();
                RefreshBattle();
                assemblyRoot.gameObject.SetActive(!battlePage);
                battleRoot.gameObject.SetActive(battlePage);
                if (comparisonRoot.gameObject.activeSelf) comparisonText.text = state.comparisonText;
                TintTab(AssemblyTabButton, !battlePage);
                TintTab(BattleTabButton, battlePage);
            }
            finally { refreshing = false; }
        }

        private static void TintTab(Button button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? T.Elevated : T.Panel;
            var outline = button.GetComponent<Outline>();
            if (outline != null) outline.effectColor = selected ? T.Gold : T.Border;
        }

        private void Apply()
        {
            if (!shared.CommitBattle()) return;
            battlePage = true;
            comparisonRoot.gameObject.SetActive(false);
            CancelSwitch();
            RefreshView();
        }

        private void Replay()
        {
            shared.Replay();
            if (!shared.GetState().workbench.hasCommittedBattle) return;
            battlePage = true;
            comparisonRoot.gameObject.SetActive(false);
            RefreshView();
        }

        private void ShowAssembly()
        {
            shared.Pause();
            battlePage = false;
            comparisonRoot.gameObject.SetActive(false);
            RefreshView();
        }

        private void ShowBattlePaused()
        {
            shared.Pause();
            battlePage = true;
            comparisonRoot.gameObject.SetActive(false);
            RefreshView();
        }

        private void RequestSwitch(Action action)
        {
            shared.Pause();
            if (!shared.GetState().form.hasUnsubmittedChanges) { action(); return; }
            pendingSwitch = action;
            switchGuard.gameObject.SetActive(true);
            RefreshView();
        }

        private void SaveAndSwitch()
        {
            var form = shared.GetState().form;
            if (form.selectedInstructionIndex >= 0) shared.Replace();
            else shared.Insert();
            if (!shared.GetState().form.hasUnsubmittedChanges) CompleteSwitch();
        }

        private void DiscardAndSwitch() { shared.LoadSelected(); CompleteSwitch(); }
        private void CompleteSwitch() { var action = pendingSwitch; CancelSwitch(); action?.Invoke(); }
        private void CancelSwitch() { pendingSwitch = null; switchGuard.gameObject.SetActive(false); }

        private RectTransform Region(string name, RectTransform parent, float from, float to)
        {
            var panel = HydrogenUi.Column(name, parent, 8, 10, T.Panel);
            panel.anchorMin = new Vector2(from, 0);
            panel.anchorMax = new Vector2(to, 1);
            panel.offsetMin = new Vector2(from == 0 ? 0 : 6, 0);
            panel.offsetMax = new Vector2(to == 1 ? 0 : -6, 0);
            return panel;
        }

        private void BuildAssembly()
        {
            var party = Region("Hydrogen_PartyAndTools", assemblyRoot, 0, .216f);
            var rules = Region("Hydrogen_PriorityStack", assemblyRoot, .216f, .569f);
            var editor = Region("Hydrogen_FourPartAndSkills", assemblyRoot, .569f, 1);
            HydrogenUi.Heading("Hydrogen_AssemblyTitle", party, font, "星象秘库 · 编排队伍");
            caseTitle = HydrogenUi.Text("Hydrogen_FullCaseTitle", party, font, "", 14, T.Ink);
            // The full title and seed/id can occupy four 20.272px Noto lines at 1280.
            Ui.FixedHeight(caseTitle.gameObject, 88);
            scenarioDropdown = HydrogenUi.Dropdown("Hydrogen_Case", party, font);
            scenarioDropdown.onValueChanged.AddListener(index =>
            {
                if (!refreshing) RequestSwitch(() => { shared.SelectCase(index); CancelSwitch(); });
            });
            ResetButton = HydrogenUi.Button("Hydrogen_ResetCase", party, font, "重置本战例", () => RequestSwitch(() => { shared.Reset(); ShowAssembly(); }));
            HydrogenUi.Heading("Hydrogen_PartyHeading", party, font, "选择编排队员");
            partyList = HydrogenUi.ScrollColumn("Hydrogen_PartyCards", party, out _);
            HydrogenUi.Heading("Hydrogen_SecondaryTools", party, font, "次级工具");
            CompareButton = HydrogenUi.Button("Hydrogen_Compare", party, font, "单项对照", () =>
            {
                if (!shared.Compare()) return;
                comparisonRoot.gameObject.SetActive(true);
                comparisonText.text = shared.GetState().comparisonText;
            });
            var tools = HydrogenUi.Row("Hydrogen_SecondaryToolRow", party, 5);
            Ui.FixedHeight(tools.gameObject, 35);
            ExportButton = HydrogenUi.Button("Hydrogen_Export", tools, font, "导出输入战报", () => shared.Export());
            SendExpeditionButton = HydrogenUi.Button("Hydrogen_SendExpedition", party, font, "发送战术到出征", () => shared.SendToExpedition());
            var sendHint = HydrogenUi.Text("Hydrogen_CampBoundary", party, font, "只发送战术与必要行动；保留营地属性、装备与账本。", 12, T.Secondary);
            Ui.FixedHeight(sendHint.gameObject, 39);
            capacityText = HydrogenUi.Text("Hydrogen_Capacity", rules, font, "", 16, T.Gold);
            Ui.FixedHeight(capacityText.gameObject, 60);
            ruleList = HydrogenUi.ScrollColumn("Hydrogen_VisiblePriorityCards", rules, out _, 6, 3);
            fullSlotText = HydrogenUi.Text("Hydrogen_FullSlotRecovery", rules, font, "", 13, T.Amber);
            Ui.FixedHeight(fullSlotText.gameObject, 38);
            var ruleCommands = HydrogenUi.Row("Hydrogen_StackFooter", rules, 7);
            Ui.FixedHeight(ruleCommands.gameObject, 34);
            newBlank = HydrogenUi.Button("Hydrogen_NewBlank", ruleCommands, font, "+ 新建空白", () => RequestSwitch(() => shared.NewBlank()));
            HydrogenUi.Button("Hydrogen_ReloadSelected", ruleCommands, font, "重载所选", () => shared.LoadSelected());
            BuildForm(editor);
        }

        private static string ShortCaseTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "未命名战例";
            int suffix = title.IndexOfAny(new[] { '：', ':' });
            return suffix > 0 ? title.Substring(0, suffix) : title;
        }

        private void RefreshParty()
        {
            var preview = state.workbench.draftPreview;
            var scenario = state.workbench.draftScenario;
            var signature = scenario.id + "|" + state.form.selectedUnitId + "|" + state.workbench.draftRevision;
            if (signature == partySignature) return;
            partySignature = signature;
            HydrogenUi.Clear(partyList);
            unitButtons.Clear();
            foreach (var unit in preview.units.Where(unit => unit.team == scenario.playerTeam))
            {
                var id = unit.id;
                var card = HydrogenUi.Button("Hydrogen_Unit_" + id, partyList, font, "", () => RequestSwitch(() => shared.SelectUnit(id)), 0, false, 118);
                var label = card.GetComponentInChildren<TMP_Text>();
                label.text = HydrogenUi.Esc(unit.name) + " · (" + unit.row + "," + unit.column + ")\nINT " + unit.intelligence
                    + " · 指令 " + unit.instructions.Length + "/" + unit.instructionCapacity + "\nHP " + unit.hp + "/" + unit.maxHp
                    + "  MP " + unit.mp + "/" + unit.maxMp + "\n每条条件≤" + unit.conditionTermCapacity;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                Ui.Stretch(label.rectTransform, 10, 5, 48, 5);
                var portrait = HydrogenUi.Panel("Portrait", card.transform, Color.clear, false);
                portrait.sprite = shared.GetPortrait(id);
                portrait.preserveAspect = true;
                portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(1, 1);
                portrait.rectTransform.pivot = new Vector2(1, 1);
                portrait.rectTransform.anchoredPosition = new Vector2(-5, -5);
                portrait.rectTransform.sizeDelta = new Vector2(40, 40);
                portrait.color = portrait.sprite == null ? T.Elevated : Color.white;
                if (portrait.sprite == null)
                {
                    var placeholder = HydrogenUi.Text("PortraitPlaceholder", portrait.transform, font,
                        string.IsNullOrEmpty(unit.name) ? "?" : HydrogenUi.Esc(unit.name.Substring(0, 1)), 20, T.Cyan, true);
                    Ui.Stretch(placeholder.rectTransform);
                    placeholder.alignment = TextAlignmentOptions.Center;
                }
                if (id == state.form.selectedUnitId) card.GetComponent<Outline>().effectColor = T.Gold;
                unitButtons.Add(card);
            }
        }

        private void RefreshRules()
        {
            var unit = state.workbench.draftScenario.units.FirstOrDefault(unit => unit.id == state.form.selectedUnitId);
            var preview = state.workbench.draftPreview.units.FirstOrDefault(value => value.id == state.form.selectedUnitId);
            if (unit == null || preview == null) return;
            capacityText.text = unit.name + " · 智力 " + unit.intelligence + "\n指令 " + unit.instructions.Length + "/" + preview.instructionCapacity
                + " · 每条条件项容量 " + preview.conditionTermCapacity;
            bool full = unit.instructions.Length >= preview.instructionCapacity;
            newBlank.interactable = !full;
            insert.interactable = !full;
            replace.interactable = state.form.selectedInstructionIndex >= 0;
            fullSlotText.text = full ? "槽位已满：选中规则应用替换，或删除一条后新增。" : "从上到下尝试规则；首个可执行规则生效。";
            var signature = state.workbench.draftScenario.id + "|" + unit.id + "|" + state.workbench.draftRevision + "|" + state.form.selectedInstructionIndex;
            if (signature == ruleSignature) return;
            ruleSignature = signature;
            HydrogenUi.Clear(ruleList);
            instructionButtons.Clear();
            for (int i = 0; i < unit.instructions.Length; i++)
            {
                int index = i;
                var rule = unit.instructions[i];
                var info = preview.instructions[i];
                var card = HydrogenUi.Button("Hydrogen_Rule_" + i, ruleList, font, "", () => RequestSwitch(() => shared.SelectInstruction(index)), 0, false, 99);
                card.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
                var content = HydrogenUi.Column("CardContent", card.transform, 4, 8);
                Ui.Stretch(content);
                var header = HydrogenUi.Row("RuleHeader", content, 4);
                Ui.FixedHeight(header.gameObject, 24);
                var name = HydrogenUi.Text("RuleTitle", header, font, "#" + (i + 1) + " " + HydrogenUi.Esc(rule.label), 15, T.Ink, true);
                Ui.Flexible(name.gameObject);
                var up = HydrogenUi.Button("Hydrogen_RuleUp_" + i, header, font, "↑", () => RequestSwitch(() => { shared.SelectInstruction(index); shared.Move(-1); }), 26, false, 24);
                var down = HydrogenUi.Button("Hydrogen_RuleDown_" + i, header, font, "↓", () => RequestSwitch(() => { shared.SelectInstruction(index); shared.Move(1); }), 26, false, 24);
                HydrogenUi.Button("Hydrogen_RuleDelete_" + i, header, font, "×", () => RequestSwitch(() => { shared.SelectInstruction(index); shared.Delete(); }), 26, false, 24);
                up.interactable = i > 0;
                down.interactable = i + 1 < unit.instructions.Length;
                var summary = HydrogenUi.Text("FourPartRule", content, font, HydrogenUi.Esc(info.display), 14, T.Secondary);
                Ui.Flexible(summary.gameObject);
                summary.overflowMode = TextOverflowModes.Ellipsis;
                if (!info.capacityAllowed || !info.actionAvailable)
                {
                    card.GetComponent<Outline>().effectColor = T.Crimson;
                    summary.text = "超载或不可执行：" + info.validationReason + "\n" + summary.text;
                }
                else if (i == state.form.selectedInstructionIndex) card.GetComponent<Outline>().effectColor = T.Gold;
                instructionButtons.Add(card);
            }
        }
    }
}
