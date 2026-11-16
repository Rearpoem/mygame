using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Prototype.Combat;
using Prototype.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    public sealed class TacticsPanelController : MonoBehaviour
    {
        private sealed class CaseInput
        {
            internal string label;
            internal string json;
        }
        private readonly List<CaseInput> cases = new List<CaseInput>();
        private TMP_FontAsset font;
        private bool built;
        private bool selectingCase;
        private RectTransform battlePane;
        private RectTransform comparisonPane;
        private TMP_Text revisionText;
        private TMP_Text footerText;
        private TMP_Text comparisonText;

        public TacticsWorkbenchSession Workbench { get; private set; }
        public event Action Changed;
        public string[] CaseTitles { get { return cases.Select(input => input.label).ToArray(); } }
        public int SelectedCaseIndex { get { return ScenarioDropdown == null ? 0 : ScenarioDropdown.value; } }
        public TacticsInstructionEditor InstructionEditor { get; private set; }
        public TacticsBattleView BattleView { get; private set; }
        public Func<CombatScenario, CombatEditResult> ExpeditionReceiver { get; set; }
        public string SelectedUnitId { get { return InstructionEditor == null ? null : InstructionEditor.SelectedUnitId; } }
        public int SelectedInstructionIndex { get { return InstructionEditor == null ? -1 : InstructionEditor.SelectedInstructionIndex; } }
        public CombatEditResult LastEditResult { get; private set; }
        public CombatEditResult LastApplyResult { get; private set; }
        public CombatEditResult LastSendResult { get; private set; }
        public TacticsComparisonResult LastComparison { get; private set; }
        public string LastError { get; private set; }
        public string LastActionMessage { get; private set; }
        public string LastExportDirectory { get; private set; }
        public string LastExportError { get; private set; }
        public string ReportDirectoryOverride { get; set; }
        public bool IsConfigured { get { return Workbench != null; } }
        public bool IsComparisonVisible { get { return comparisonPane != null && comparisonPane.gameObject.activeSelf; } }
        public TMP_Dropdown ScenarioDropdown { get; private set; }
        public TMP_Text RevisionText { get { return revisionText; } }
        public TMP_Text FeedbackText { get { return footerText; } }
        public TMP_Text ExportPathText { get { return footerText; } }
        public TMP_Text ComparisonText { get { return comparisonText; } }
        public ScrollRect FooterScroll { get; private set; }
        public ScrollRect ComparisonScroll { get; private set; }
        public Button ApplyBattleButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public Button CompareButton { get; private set; }
        public Button SendExpeditionButton { get; private set; }
        public Button ExportButton { get; private set; }
        public Button ResetButton { get; private set; }
        public Button BackToBattleButton { get; private set; }
        public Button NewBlankButton { get { return InstructionEditor.NewBlankButton; } }
        public Button InsertButton { get { return InstructionEditor.InsertButton; } }
        public Button ReplaceButton { get { return InstructionEditor.ReplaceButton; } }
        public Button DeleteButton { get { return InstructionEditor.DeleteButton; } }
        public Button MoveUpButton { get { return InstructionEditor.MoveUpButton; } }
        public Button MoveDownButton { get { return InstructionEditor.MoveDownButton; } }

        public void Initialize(TMP_FontAsset value)
        {
            if (built) return;
            font = value;
            DiscoverCases();
            BuildInterface();
            built = true;
            try
            {
                if (cases.Count == 0) throw new FileNotFoundException("缺少 StreamingAssets/Prototype/Combat 下的 round3_ 或 round4_ 战例。");
                LoadConfiguration(cases[0].json);
            }
            catch (Exception exception) { ShowException("加载战术工作台", exception); }
        }

        private void DiscoverCases()
        {
            var folder = Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat");
            if (!Directory.Exists(folder)) return;
            foreach (var path in Directory.GetFiles(folder, "round*_*.json")
                .Where(path => Path.GetFileName(path).StartsWith("round3_", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).StartsWith("round4_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(CasePriority)
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var json = File.ReadAllText(path);
                var scenario = JsonUtility.FromJson<CombatScenario>(json);
                // Expedition envelopes can share the round prefix but are not standalone battles.
                if (scenario == null || scenario.units == null || scenario.units.Length == 0) continue;
                cases.Add(new CaseInput { json = json, label = string.IsNullOrWhiteSpace(scenario.title) ? Path.GetFileNameWithoutExtension(path) : scenario.title });
            }
        }

        private static int CasePriority(string path)
        {
            var file = Path.GetFileName(path);
            if (string.Equals(file, "round4_mage_library.json", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(file, "round3_tactics.json", StringComparison.OrdinalIgnoreCase)) return 1;
            return file.StartsWith("round4_", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
        }

        private void BuildInterface()
        {
            var toolbar = Ui.Row("TacticsToolbar", transform, 6);
            Ui.FixedHeight(toolbar.gameObject, 38);
            ScenarioDropdown = TacticsUiControls.Dropdown("Tactics_Scenario", toolbar, font, 151);
            TacticsUiControls.Options(ScenarioDropdown, cases.Count == 0 ? new[] { "无战例配置" } : cases.Select(input => input.label));
            ScenarioDropdown.onValueChanged.AddListener(index =>
            {
                if (selectingCase || index < 0 || index >= cases.Count) return;
                try { LoadConfiguration(cases[index].json); }
                catch (Exception exception) { ShowException("切换战例", exception); }
            });
            ApplyBattleButton = Ui.Button("Tactics_ApplyBattle", toolbar, font, "应用并开战", ApplyBattle, 124, true);
            ReplayButton = Ui.Button("Tactics_Replay", toolbar, font, "同开局重放", Replay, 122);
            CompareButton = Ui.Button("Tactics_Compare", toolbar, font, "单项对照", Compare, 106);
            SendExpeditionButton = Ui.Button("Tactics_SendExpedition", toolbar, font, "发送战术到出征", SendToExpedition, 174);
            ExportButton = Ui.Button("Tactics_Export", toolbar, font, "导出输入与战报", Export, 152);
            ResetButton = Ui.Button("Tactics_Reset", toolbar, font, "重置本战例", ResetWorkbench, 120);
            revisionText = Ui.Text("TacticsRevision", toolbar, font, "", 14, Ui.Muted);
            revisionText.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Flexible(revisionText.gameObject);
            var details = Ui.Row("TacticsDetails", transform, 12);
            Ui.Flexible(details.gameObject);
            var editorRoot = Ui.Column("TacticsEditorPane", details, 6, 8, Ui.Card);
            Ui.Width(editorRoot.gameObject, 440);
            Ui.Flexible(editorRoot.gameObject, 0, 1);
            var right = Ui.Rect("TacticsRightPane", details);
            Ui.Flexible(right.gameObject);
            battlePane = Ui.Image("TacticsBattlePane", right, Ui.Card).rectTransform;
            Ui.Stretch(battlePane);
            var battleMinimum = battlePane.gameObject.AddComponent<LayoutElement>();
            battleMinimum.minHeight = 590;
            BattleView = battlePane.gameObject.AddComponent<TacticsBattleView>();
            BattleView.Initialize(font);
            BattleView.PlaybackChanged += RefreshStatus;
            comparisonPane = Ui.Column("TacticsComparisonPane", right, 6, 8, Ui.Card);
            Ui.Stretch(comparisonPane);
            BackToBattleButton = Ui.Button("Tactics_BackToBattle", comparisonPane, font, "返回实战视图", ShowBattle, 180);
            Ui.FixedHeight(BackToBattleButton.gameObject, 35);
            comparisonText = Ui.ScrollText("TacticsComparison", comparisonPane, font, out var comparisonScroll, 16);
            comparisonText.margin = new Vector4(4, 4, 4, 4);
            ComparisonScroll = comparisonScroll;
            comparisonPane.gameObject.SetActive(false);
            footerText = Ui.ScrollText("TacticsFeedback", transform, font, out var footerScroll, 15);
            footerText.margin = new Vector4(4, 1, 4, 1);
            footerScroll.viewport.offsetMin = new Vector2(10, 3);
            footerScroll.viewport.offsetMax = new Vector2(-10, -3);
            FooterScroll = footerScroll;
            Ui.FixedHeight(footerScroll.gameObject, 52);
            InstructionEditor = editorRoot.gameObject.AddComponent<TacticsInstructionEditor>();
        }

        public void LoadConfiguration(string scenarioJson)
        {
            BattleView.Pause();
            var scenario = JsonUtility.FromJson<CombatScenario>(scenarioJson);
            var playerTeam = TacticsUiControls.PlayerTeam(scenario);
            var session = new TacticsWorkbenchSession(scenario);
            Workbench = session;
            if (InstructionEditor.UnitDropdown == null)
            {
                InstructionEditor.Initialize(font, session);
                InstructionEditor.NewBlankRequested += NewBlank;
                InstructionEditor.InsertRequested += Insert;
                InstructionEditor.ReplaceRequested += Replace;
                InstructionEditor.DeleteRequested += Delete;
                InstructionEditor.MoveRequested += Move;
                InstructionEditor.SelectionChanged += RefreshStatus;
                InstructionEditor.FormEdited += RefreshStatus;
            }
            else InstructionEditor.BindWorkbench(session);
            BattleView.FriendlyTeam = playerTeam;
            BattleView.Bind(() => Workbench.GetBattleSnapshot(), () => Workbench.GetBattleHistory(),
                () => Workbench.StepBattle(), () => Workbench.ReplayCommitted());
            LastEditResult = null;
            LastApplyResult = null;
            LastSendResult = null;
            LastComparison = null;
            LastError = null;
            LastExportDirectory = null;
            LastExportError = null;
            selectingCase = true;
            var found = cases.FindIndex(input => input.json == scenarioJson);
            if (found >= 0) ScenarioDropdown.SetValueWithoutNotify(found);
            selectingCase = false;
            ShowBattle();
            RefreshStatus();
            Feedback("先选队员和已有规则，或新建空白四段；修改表单后先新增/替换，再应用到战斗。", false);
        }

        public void SelectCharacter(string unitId) { InstructionEditor.SelectUnit(unitId); RefreshStatus(); }
        public void SelectInstruction(int index) { InstructionEditor.SelectInstruction(index); RefreshStatus(); }
        public void SelectCase(int index)
        {
            if (index < 0 || index >= cases.Count) throw new ArgumentOutOfRangeException(nameof(index));
            LoadConfiguration(cases[index].json);
        }
        public void NotifyUser(string message, bool error = false) { Feedback(message, error); }

        public void NewBlank()
        {
            BattleView.Pause();
            InstructionEditor.SetDraft(Workbench.CreateBlankInstruction());
            Feedback("空白草稿已创建。填完四段后可新增或替换；若指令槽已满，请应用替换所选条，或先删除一条腾位。", false);
        }

        public void Insert()
        {
            Edit("新增指令", () =>
            {
                if (!InstructionEditor.TryReadInstruction(out var instruction, out var error)) return InputFailure(error);
                var count = Workbench.GetDraftScenario().units.First(unit => unit.id == SelectedUnitId).instructions.Length;
                var result = Workbench.InsertInstruction(SelectedUnitId, count, instruction);
                if (result.success) InstructionEditor.SelectInstruction(count);
                return result;
            });
        }

        public void Replace()
        {
            Edit("替换指令", () =>
            {
                if (!InstructionEditor.TryReadInstruction(out var instruction, out var error)) return InputFailure(error);
                var result = Workbench.ReplaceInstruction(SelectedUnitId, SelectedInstructionIndex, instruction);
                if (result.success) InstructionEditor.SelectInstruction(SelectedInstructionIndex);
                return result;
            });
        }

        public void Delete()
        {
            Edit("删除指令", () =>
            {
                var index = SelectedInstructionIndex;
                var result = Workbench.RemoveInstruction(SelectedUnitId, index);
                if (result.success)
                {
                    var count = Workbench.GetDraftScenario().units.First(unit => unit.id == SelectedUnitId).instructions.Length;
                    InstructionEditor.SelectInstruction(Math.Min(index, count - 1));
                }
                return result;
            });
        }

        public void Move(int direction)
        {
            Edit("调整顺序", () =>
            {
                var target = SelectedInstructionIndex + direction;
                var result = Workbench.MoveInstruction(SelectedUnitId, SelectedInstructionIndex, target);
                if (result.success) InstructionEditor.SelectInstruction(target);
                return result;
            });
        }

        private void Edit(string label, Func<CombatEditResult> action)
        {
            if (!RequireWorkbench()) return;
            BattleView.Pause();
            try
            {
                LastEditResult = action();
                RefreshStatus();
                Feedback(label + "：" + LastEditResult.code + " · " + LastEditResult.message
                    + (LastEditResult.success ? "；应用开战后使用此战术，当前实战仍保留已提交输入。" : ""), !LastEditResult.success);
            }
            catch (Exception exception) { ShowException(label, exception); }
        }

        public void ApplyBattle()
        {
            if (!RequireWorkbench() || !RequireSubmittedForm()) return;
            try
            {
                BattleView.Pause();
                LastApplyResult = Workbench.CommitBattle();
                if (!LastApplyResult.success) { Feedback(LastApplyResult.code + " · " + LastApplyResult.message, true); return; }
                BattleView.SetFollowLatest(true);
                ShowBattle();
                BattleView.Refresh();
                BattleView.StartAuto();
                RefreshStatus();
                Feedback("已将当前战术应用到相同开局并开始播放。可暂停、单步或选中历史事件查看实际指令原因。", false);
            }
            catch (Exception exception) { ShowException("应用并开战", exception); }
        }

        public void Replay()
        {
            if (!RequireWorkbench()) return;
            try
            {
                if (!Workbench.GetSnapshot().hasCommittedBattle) { Feedback("尚未应用战术，请先点「应用并开战」。", true); return; }
                ShowBattle();
                BattleView.Replay();
                RefreshStatus();
                Feedback("已恢复本场已应用的相同开局；未提交的新草稿不参与此次重放。", false);
            }
            catch (Exception exception) { ShowException("同开局重放", exception); }
        }

        public void Compare()
        {
            if (!RequireWorkbench() || !RequireSubmittedForm()) return;
            try
            {
                BattleView.Pause();
                LastComparison = Workbench.CompareWithLoaded();
                if (!LastComparison.success) { Feedback(LastComparison.code + " · " + LastComparison.message, true); return; }
                battlePane.gameObject.SetActive(false);
                comparisonPane.gameObject.SetActive(true);
                comparisonText.text = FormatComparison(LastComparison);
                ComparisonScroll.verticalNormalizedPosition = 1;
                RefreshStatus();
                Feedback("单项对照已从加载开局与当前战术实际运行。当前实战没有推进；完整指令原因与事件在对照中。", false);
            }
            catch (Exception exception) { ShowException("单项对照", exception); }
        }

        private static string FormatComparison(TacticsComparisonResult result)
        {
            var comparison = result.comparison;
            var divergence = comparison.firstDivergence;
            var first = divergence == null ? "没有实际行动分歧。" : "首次实际分歧 WAIT "
                + Number((divergence.baseline ?? divergence.variant).time) + "\n加载配置：" + DescribeStep(divergence.baseline)
                + "\n当前战术：" + DescribeStep(divergence.variant);
            return "单项对照：" + result.changeKind + " / " + result.changedUnitId + "\n" + result.message
                + "\n加载配置结束 WAIT " + Number(comparison.baseline.time) + "；当前战术结束 WAIT " + Number(comparison.variant.time)
                + "\n" + first + "\n\n" + CombatSampleRuns.FormatComparisonMarkdown(comparison)
                + "\n\n加载配置完整原因\n" + result.baselineLogText + "\n\n当前战术完整原因\n" + result.variantLogText;
        }

        private static string DescribeStep(CombatStepResult step)
        {
            if (step == null) return "没有对应事件";
            return step.actorName + "→" + step.actionName + "→" + step.targetName + " · " + step.message;
        }

        public void SendToExpedition()
        {
            if (!RequireWorkbench() || !RequireSubmittedForm()) return;
            try
            {
                BattleView.Pause();
                LastSendResult = ExpeditionReceiver == null ? InputFailure("出征入口尚未接入。")
                    : ExpeditionReceiver(Workbench.GetDraftScenario());
                Feedback("发送战术及所需动作：" + LastSendResult.code + " · " + LastSendResult.message, !LastSendResult.success);
            }
            catch (Exception exception) { ShowException("发送战术到出征", exception); }
        }

        public void ResetWorkbench()
        {
            if (!RequireWorkbench()) return;
            try
            {
                BattleView.Pause();
                Workbench.Reset();
                BattleView.SetFollowLatest(true);
                InstructionEditor.BindWorkbench(Workbench);
                LastEditResult = null;
                LastApplyResult = null;
                LastComparison = null;
                LastSendResult = null;
                ShowBattle();
                BattleView.Refresh();
                RefreshStatus();
                Feedback("已恢复本战例加载时的战术与开局，清空本页战斗和对照；已有导出文件保留。", false);
            }
            catch (Exception exception) { ShowException("重置本战例", exception); }
        }

        public void ShowBattle()
        {
            if (battlePane == null) return;
            comparisonPane.gameObject.SetActive(false);
            battlePane.gameObject.SetActive(true);
        }

        public void RefreshStatus()
        {
            if (Workbench == null) return;
            var snapshot = Workbench.GetSnapshot();
            revisionText.text = "草稿 v" + snapshot.draftRevision + " / "
                + (InstructionEditor.HasFormChanges ? "表单未提交" : !snapshot.hasCommittedBattle ? "尚未开战"
                    : snapshot.hasUncommittedChanges ? "待应用" : "已应用 v" + snapshot.committedRevision);
            ReplayButton.interactable = snapshot.hasCommittedBattle;
            Changed?.Invoke();
        }

        public void Export()
        {
            if (!RequireWorkbench()) return;
            try
            {
                BattleView.Pause();
                LastExportDirectory = TacticsReportExporter.Export(Workbench, InstructionEditor.GetFormInputSnapshot(),
                    InstructionEditor.Catalog, ReportDirectoryOverride);
                LastExportError = null;
                Feedback("已导出当前编辑输入、表单、目录与实际战报。", false);
            }
            catch (Exception exception)
            {
                LastExportError = exception.ToString();
                ShowException("导出输入与战报", exception);
            }
        }

        private bool RequireSubmittedForm()
        {
            if (!InstructionEditor.HasFormChanges) return true;
            Feedback("表单有尚未提交的修改。请先点「新增到末尾」或「应用替换」，再应用、对照或发送当前战术。", true);
            return false;
        }

        private bool RequireWorkbench()
        {
            if (Workbench != null) return true;
            Feedback("战术工作台未加载，请检查所选战例的 JSON 配置。", true);
            return false;
        }

        private void Feedback(string message, bool error)
        {
            LastActionMessage = message;
            footerText.text = message + (string.IsNullOrEmpty(LastExportDirectory) ? "" : "\n导出目录：" + LastExportDirectory);
            footerText.color = error ? Ui.Bad : Ui.Good;
            FooterScroll.verticalNormalizedPosition = 1;
            Changed?.Invoke();
        }

        private void ShowException(string operation, Exception exception)
        {
            LastError = exception.ToString();
            Feedback(operation + "失败：" + exception.GetType().Name + " · " + exception.Message, true);
        }

        private static CombatEditResult InputFailure(string message)
        {
            return new CombatEditResult { success = false, code = "InputRequired", message = message };
        }
        private static string Number(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
    }
}
