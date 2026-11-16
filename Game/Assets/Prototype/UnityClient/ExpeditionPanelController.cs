using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.Expedition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    /// <summary>Presentation and file export only; the slice session owns every game command.</summary>
    public sealed class ExpeditionPanelController : MonoBehaviour
    {
        private TMP_FontAsset font;
        private bool built;
        private string loadedIntegrationJson;
        private string loadedCombatJson;
        private string loadedPreparationJson;
        private TMP_Text phaseText;
        private TMP_Text summaryText;
        private TMP_Text stateText;
        private TMP_Text reportText;
        private TMP_Text feedbackText;
        private TMP_Text exportPathText;
        private Button reportTab;
        private Button ledgerTab;
        private Button comparisonTab;
        private string activeReportPage = "Report";

        public ExpeditionSliceSession SliceSession { get; private set; }
        public ExpeditionSliceSnapshot Snapshot { get { return SliceSession == null ? null : SliceSession.GetSnapshot(); } }
        public bool IsConfigured { get { return SliceSession != null; } }
        public SliceCommandResult LastCommand { get; private set; }
        public ExpeditionAdvanceResult LastAdvance { get; private set; }
        public EquipmentComparisonReport LastComparison { get; private set; }
        public VerificationBattleReport LastVerification { get; private set; }
        public string LastActionMessage { get; private set; }
        public string LastError { get; private set; }
        public string LastExportDirectory { get; private set; }
        public string LastExportError { get; private set; }
        public string ReportDirectoryOverride { get; set; }
        public string ActiveReportPage { get { return activeReportPage; } }
        public TMP_Text SummaryText { get { return summaryText; } }
        public TMP_Text StateText { get { return stateText; } }
        public TMP_Text ReportText { get { return reportText; } }
        public TMP_Text FeedbackText { get { return feedbackText; } }
        public TMP_Text ExportPathText { get { return exportPathText; } }
        public Button ExpeditionReportTabButton { get { return reportTab; } }
        public Button ExpeditionLedgerTabButton { get { return ledgerTab; } }
        public Button ExpeditionComparisonTabButton { get { return comparisonTab; } }
        public ScrollRect StateScroll { get; private set; }
        public ScrollRect ReportScroll { get; private set; }
        public Button ExpeditionNextButton { get; private set; }
        public Button ExpeditionDispatchButton { get; private set; }
        public Button ExpeditionResetButton { get; private set; }
        public Button ExpeditionDefaultsButton { get { return ExpeditionResetButton; } }
        public Button ExpeditionHealUpButton { get; private set; }
        public Button ExpeditionHealDownButton { get; private set; }
        public Button ExpeditionApplyButton { get; private set; }
        public TMP_InputField ExpeditionThresholdInput { get; private set; }
        public Button ExpeditionPurchaseButton { get; private set; }
        public Button ExpeditionEnhanceButton { get; private set; }
        public Button ExpeditionReadReportButton { get; private set; }
        public Button ExpeditionReplaySettlementButton { get; private set; }
        public Button ExpeditionCompareButton { get; private set; }
        public Button ExpeditionContinueButton { get; private set; }
        public Button ExpeditionExportButton { get; private set; }

        public void Initialize(TMP_FontAsset uiFont)
        {
            if (built) return;
            font = uiFont;
            BuildInterface();
            built = true;
            try
            {
                var integrationJson = ReadStreamingJson("Prototype/Expedition/default.json");
                var integration = JsonUtility.FromJson<ExpeditionSliceConfig>(integrationJson);
                LoadConfiguration(integrationJson, ReadStreamingJson(integration.combatConfigPath),
                    ReadStreamingJson(integration.preparationConfigPath));
            }
            catch (Exception ex) { ShowException("加载出征配置", ex); DisableUnconfiguredControls(); }
        }

        /// <summary>Loads the same DTOs as the player. Useful for alternative JSON and UI integration tests.</summary>
        public void LoadConfiguration(string integrationJson, string combatJson, string preparationJson)
        {
            var integration = JsonUtility.FromJson<ExpeditionSliceConfig>(integrationJson);
            var combat = JsonUtility.FromJson<CombatExpeditionConfig>(combatJson);
            var preparation = JsonUtility.FromJson<PreparationScenario>(preparationJson);
            var session = new ExpeditionSliceSession(integration, combat, preparation);
            loadedIntegrationJson = integrationJson;
            loadedCombatJson = combatJson;
            loadedPreparationJson = preparationJson;
            SliceSession = session;
            ClearClientResults();
            activeReportPage = "Report";
            Refresh(true);
            SetFeedback("本轮路线：一键派遣 → 采购 → 强化 → 同起点对照 → 携带状态再战 → 导出。全部为测试参数。", false);
        }

        /// <summary>Receive rules through the atomic slice adapter; keep this camp's real wallets and ledger.</summary>
        public CombatEditResult ReceiveTactics(CombatScenario scenario)
        {
            if (SliceSession == null)
                return new CombatEditResult { success = false, message = "出征配置尚未加载，无法接收战术。" };
            try
            {
                LastCommand = SliceSession.ApplyTactics(scenario);
                LastActionMessage = LastCommand.reason;
                Refresh(true);
                SetFeedback(LastCommand.success
                    ? "已接收此战术并保留当前营地钱包与账本。完整新增/编辑请回「战术编排与战斗」；旧急救快捷按钮可能不适用于新技能。"
                    : "接收战术失败：" + LastCommand.reason, !LastCommand.success);
                return new CombatEditResult { success = LastCommand.success, message = LastCommand.reason, code = LastCommand.code };
            }
            catch (Exception exception)
            {
                ShowException("接收此战术", exception);
                return new CombatEditResult { success = false, message = exception.Message };
            }
        }

        private static string ReadStreamingJson(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new ArgumentException("配置必须使用 StreamingAssets 内的相对路径。");
            var streamingRoot = Path.GetFullPath(Application.streamingAssetsPath);
            var path = Path.GetFullPath(Path.Combine(streamingRoot, relativePath));
            if (!path.StartsWith(streamingRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("配置路径超出 StreamingAssets。");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private void BuildInterface()
        {
            var actions = Ui.Row("ExpeditionActions", transform, 8);
            Ui.FixedHeight(actions.gameObject, 41);
            ExpeditionNextButton = Ui.Button("Expedition_Next", actions, font, "逐场：下一战", () => Advance(false), 132, true);
            ExpeditionDispatchButton = Ui.Button("Expedition_Dispatch", actions, font, "一键派遣", () => Advance(true), 122, true);
            ExpeditionReadReportButton = Ui.Button("Expedition_ReadReport", actions, font, "只读查看战报", ShowReport, 136);
            ExpeditionReplaySettlementButton = Ui.Button("Expedition_RetrySettlement", actions, font, "重试最近奖励结算", RetrySettlement, 172);
            ExpeditionResetButton = Ui.Button("Expedition_Reset", actions, font, "重置加载开局", ResetExperiment, 142);
            phaseText = Ui.Text("ExpeditionPhase", actions, font, "", 16, Ui.Accent);
            phaseText.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Flexible(phaseText.gameObject);

            var edits = Ui.Row("ExpeditionInstructionControls", transform, 8);
            Ui.FixedHeight(edits.gameObject, 40);
            Ui.Label("ExpeditionInstructionLabel", edits, font, "法师急救", 85);
            ExpeditionHealUpButton = Ui.Button("Expedition_HealUp", edits, font, "优先级 ↑", () => MoveHealing(-1), 99);
            ExpeditionHealDownButton = Ui.Button("Expedition_HealDown", edits, font, "优先级 ↓", () => MoveHealing(1), 99);
            Ui.Label("ExpeditionThresholdLabel", edits, font, "HP 阈值 %", 107);
            ExpeditionThresholdInput = Ui.Input("Expedition_Threshold", edits, font, 76);
            ExpeditionApplyButton = Ui.Button("Expedition_Apply", edits, font, "应用条件", ApplyThreshold, 112);
            var editHint = Ui.Text("ExpeditionEditHint", edits, font, "营地可编辑；出征中冻结。重置会恢复全部加载配置。", 14, Ui.Muted);
            editHint.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Flexible(editHint.gameObject);

            var preparation = Ui.Row("ExpeditionPreparationControls", transform, 9);
            Ui.FixedHeight(preparation.gameObject, 41);
            ExpeditionPurchaseButton = Ui.Button("Expedition_Purchase", preparation, font, "采购辅料", PurchaseAuxiliary, 185);
            ExpeditionEnhanceButton = Ui.Button("Expedition_Enhance", preparation, font, "强化已装备武器", EnhanceWeapon, 160);
            ExpeditionCompareButton = Ui.Button("Expedition_Compare", preparation, font, "同起点强化对照", CompareEquipment, 175);
            ExpeditionContinueButton = Ui.Button("Expedition_Verify", preparation, font, "携带状态再战", VerifyBattle, 164);
            ExpeditionExportButton = Ui.Button("Expedition_Export", preparation, font, "导出本次战报与账本", ExportCurrentSession, 206);

            summaryText = Ui.Text("ExpeditionSummary", transform, font, "", 18, Ui.Good);
            Ui.FixedHeight(summaryText.gameObject, 58);
            var details = Ui.Row("ExpeditionDetails", transform, 12);
            Ui.Flexible(details.gameObject);
            var stateColumn = Ui.Column("ExpeditionStateColumn", details, 6, 10, Ui.Card);
            Ui.Flexible(stateColumn.gameObject, 1, 1);
            var stateHeading = Ui.Text("ExpeditionStateHeading", stateColumn, font, "队伍、归属与营地配置", 18, Ui.Accent);
            Ui.FixedHeight(stateHeading.gameObject, 29);
            stateText = Ui.ScrollText("ExpeditionState", stateColumn, font, out var stateScroll, 16);
            stateText.margin = new Vector4(4, 4, 4, 4);
            StateScroll = stateScroll;
            var reportColumn = Ui.Column("ExpeditionReportColumn", details, 6, 10, Ui.Card);
            Ui.Flexible(reportColumn.gameObject, 1.2f, 1);
            var tabs = Ui.Row("ExpeditionReportTabs", reportColumn, 7);
            Ui.FixedHeight(tabs.gameObject, 34);
            reportTab = Ui.Button("Expedition_TabReport", tabs, font, "完整战报", ShowReport, 120);
            ledgerTab = Ui.Button("Expedition_TabLedger", tabs, font, "公共账本", ShowLedger, 120);
            comparisonTab = Ui.Button("Expedition_TabComparison", tabs, font, "同起点对照", ShowComparison, 132);
            reportText = Ui.ScrollText("ExpeditionReport", reportColumn, font, out var scroll, 16);
            reportText.margin = new Vector4(4, 4, 4, 4);
            ReportScroll = scroll;
            var hint = Ui.Text("ExpeditionScrollHint", reportColumn, font,
                "滚轮查看完整事件、跳过原因、状态到期与转账双方。对照与验证战均无额外掉落。", 14, Ui.Muted);
            Ui.FixedHeight(hint.gameObject, 40);
            feedbackText = Ui.Text("ExpeditionFeedback", transform, font, "", 16, Ui.Good);
            Ui.FixedHeight(feedbackText.gameObject, 50);
            exportPathText = Ui.Text("ExpeditionExportPath", transform, font, "", 14, Ui.Muted);
            Ui.FixedHeight(exportPathText.gameObject, 44);
        }

        private void Advance(bool runToEnd)
        {
            if (!RequireConfigured()) return;
            try
            {
                var snapshot = SliceSession.GetSnapshot();
                if (!snapshot.expeditionActive)
                {
                    if (snapshot.phase != "Camp" || (snapshot.report != null && snapshot.report.encounters.Length > 0))
                    {
                        SetFeedback("本次出征已停止或结束。可查看战报、备战或运行无掉落验证战；重开实验请重置。", true);
                        return;
                    }
                    LastCommand = SliceSession.StartExpedition();
                    if (!LastCommand.success) { Refresh(false); ShowCommandResult("开始出征", LastCommand); return; }
                }
                LastAdvance = runToEnd ? SliceSession.RunToEnd() : SliceSession.RunNextEncounter();
                LastCommand = SliceSession.GetSnapshot().lastCommand;
                activeReportPage = "Report";
                Refresh(false);
                SetFeedback(LastAdvance.success ? RecentActionSummary(LastAdvance)
                    : (runToEnd ? "一键派遣" : "逐场推进") + "失败：" + LastAdvance.code + " · " + LastAdvance.reason,
                    !LastAdvance.success);
            }
            catch (Exception ex) { ShowException(runToEnd ? "一键派遣" : "逐场推进", ex); }
        }

        private void PurchaseAuxiliary()
        {
            ExecuteCommand("采购辅料", () => SliceSession.PurchaseAuxiliary("ui:purchase:auxiliary",
                SliceSession.GetPreparationConfiguration().purchaseQuantity));
            activeReportPage = "Ledger";
            Refresh(false);
        }

        private void EnhanceWeapon()
        {
            ExecuteCommand("强化武器", () => SliceSession.EnhanceEquippedWeapon("ui:enhance:weapon"));
            activeReportPage = "Ledger";
            Refresh(false);
        }

        private void RetrySettlement()
        {
            if (!RequireConfigured()) return;
            var receipt = SliceSession.GetSnapshot().settlements.LastOrDefault(value => value.rewardsEligible);
            if (receipt == null) { SetFeedback("尚无可重试的获奖结算。请先完成一场胜利。", true); return; }
            ExecuteCommand("重试奖励结算", () => SliceSession.RetrySettlement(receipt.resultId));
        }

        private void ExecuteCommand(string label, Func<SliceCommandResult> command)
        {
            if (!RequireConfigured()) return;
            try
            {
                LastCommand = command();
                Refresh(false);
                ShowCommandResult(label, LastCommand);
            }
            catch (Exception ex) { ShowException(label, ex); }
        }

        private void ShowCommandResult(string label, SliceCommandResult result)
        {
            SetFeedback(label + "：" + result.code + " · " + result.reason
                + (result.isReplay ? "（同一操作重试，未重复入账）" : ""), !result.success);
        }

        private void MoveHealing(int direction)
        {
            if (!RequireConfigured()) return;
            try
            {
                var mage = MageConfiguration();
                var index = HealingIndex(mage);
                if (index < 0) { SetFeedback("当前配置没有法师急救指令。", true); return; }
                var result = SliceSession.MoveInstruction("mage", index, index + direction);
                Refresh(true);
                SetFeedback("调整优先级：" + result.message, !result.success);
            }
            catch (Exception ex) { ShowException("调整优先级", ex); }
        }

        private void ApplyThreshold()
        {
            if (!RequireConfigured()) return;
            if (!double.TryParse(ExpeditionThresholdInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                || double.IsNaN(percent) || double.IsInfinity(percent) || percent < 0 || percent > 100)
            { SetFeedback("治疗阈值请输入 0 至 100 的百分比。", true); return; }
            try
            {
                var index = HealingIndex(MageConfiguration());
                if (index < 0) { SetFeedback("当前配置没有法师急救指令。", true); return; }
                var result = SliceSession.SetConditionThreshold("mage", index, 0, percent / 100);
                Refresh(true);
                SetFeedback("修改条件：" + result.message, !result.success);
            }
            catch (Exception ex) { ShowException("修改条件", ex); }
        }

        private void CompareEquipment()
        {
            if (!RequireConfigured()) return;
            try
            {
                LastComparison = SliceSession.CompareEquipmentFromCurrentState();
                activeReportPage = "Comparison";
                Refresh(false);
                SetFeedback("同起点对照：" + LastComparison.code + " · " + LastComparison.reason
                    + "；对照不改变当前队伍、公库或经验。", !LastComparison.success);
            }
            catch (Exception ex) { ShowException("同起点对照", ex); }
        }

        private void VerifyBattle()
        {
            if (!RequireConfigured()) return;
            try
            {
                LastVerification = SliceSession.RunVerificationBattle();
                activeReportPage = "Report";
                Refresh(false);
                SetFeedback("携带状态再战：" + LastVerification.code + " · " + LastVerification.reason
                    + "；无现金、物资与经验奖励。", !LastVerification.success);
            }
            catch (Exception ex) { ShowException("携带状态再战", ex); }
        }

        private void ResetExperiment()
        {
            if (!RequireConfigured()) return;
            try
            {
                SliceSession.Reset();
                ClearClientResults();
                activeReportPage = "Report";
                Refresh(true);
                SetFeedback("已恢复本轮加载开局：指令、阈值、HP/MP、状态、XP、钱包、装备、账本与去重记录均重置。", false);
            }
            catch (Exception ex) { ShowException("重置加载开局", ex); }
        }

        private void ClearClientResults()
        {
            LastCommand = null;
            LastAdvance = null;
            LastComparison = null;
            LastVerification = null;
            LastError = null;
            LastExportDirectory = null;
            LastExportError = null;
        }

        private CombatUnitConfig MageConfiguration()
        {
            return SliceSession.GetCombatConfiguration().encounters[0].scenario.units.FirstOrDefault(unit => unit.id == "mage");
        }

        private static int HealingIndex(CombatUnitConfig mage)
        {
            return mage == null || mage.instructions == null ? -1
                : Array.FindIndex(mage.instructions, instruction => instruction.actionId == "first_aid");
        }

        public void ShowReport()
        {
            if (!RequireConfigured()) return;
            try { activeReportPage = "Report"; Refresh(false); SetFeedback("只读查看既有战报；时间、奖励和账本均未推进。", false); }
            catch (Exception ex) { ShowException("查看战报", ex); }
        }

        public void ShowLedger()
        {
            if (!RequireConfigured()) return;
            try { activeReportPage = "Ledger"; Refresh(false); SetFeedback("只读查看逐笔公共账本与私人收款方。", false); }
            catch (Exception ex) { ShowException("查看账本", ex); }
        }

        public void ShowComparison()
        {
            if (!RequireConfigured()) return;
            try { activeReportPage = "Comparison"; Refresh(false); SetFeedback("只读查看已生成的同起点对照；生成新对照请点上方按钮。", false); }
            catch (Exception ex) { ShowException("查看对照", ex); }
        }

        private void Refresh(bool updateInputs)
        {
            if (SliceSession == null) return;
            var snapshot = SliceSession.GetSnapshot();
            var preparation = SliceSession.GetPreparationConfiguration();
            var combat = SliceSession.GetCombatConfiguration();
            var report = snapshot.report;
            var xpTotal = snapshot.experience == null ? 0 : snapshot.experience.Sum(grant => grant.amount);
            var victoryCount = report == null || report.encounters == null ? 0 : report.encounters.Count(battle => battle.rewardsEligible);
            var recordedCount = report == null || report.encounters == null ? 0 : report.encounters.Length;
            var mainWait = report == null || report.finalSnapshot == null ? "未派遣" : Number(report.finalSnapshot.time);
            phaseText.text = "阶段：" + PhaseName(snapshot.phase) + "\n已记录 " + recordedCount + "/" + combat.encounters.Length + " 场";
            summaryText.text = "主线 " + victoryCount + " 胜 / " + combat.encounters.Length + " 场  ·  WAIT " + mainWait
                + "  ·  XP " + xpTotal + "  |  公库 " + snapshot.preparation.treasuryCash + "  ·  " + preparation.residentName + " " + snapshot.preparation.residentCash
                + "\n公有 " + preparation.oreItemName + " " + snapshot.preparation.publicOre + " / " + preparation.auxiliaryItemName + " " + snapshot.preparation.publicAuxiliary
                + "  ·  " + preparation.residentName + "私有 " + preparation.auxiliaryItemName + " " + snapshot.preparation.residentAuxiliary
                + "  |  武器 Lv" + snapshot.preparation.weaponEnhancementLevel + " / +" + snapshot.preparation.weaponAttackBonus;
            stateText.text = FormatState(snapshot, preparation, combat);
            var mage = MageConfiguration();
            var healingIndex = HealingIndex(mage);
            ExpeditionHealUpButton.interactable = snapshot.canEditConfiguration && healingIndex > 0;
            ExpeditionHealDownButton.interactable = snapshot.canEditConfiguration && healingIndex >= 0 && healingIndex < mage.instructions.Length - 1;
            ExpeditionThresholdInput.interactable = snapshot.canEditConfiguration && healingIndex >= 0;
            ExpeditionApplyButton.interactable = snapshot.canEditConfiguration && healingIndex >= 0;
            if (updateInputs && healingIndex >= 0 && mage.instructions[healingIndex].condition.terms.Length > 0)
                ExpeditionThresholdInput.SetTextWithoutNotify(Number(mage.instructions[healingIndex].condition.terms[0].threshold * 100));
            var canAdvance = snapshot.expeditionActive || (snapshot.phase == "Camp" && recordedCount == 0);
            ExpeditionNextButton.interactable = canAdvance;
            ExpeditionDispatchButton.interactable = canAdvance;
            ExpeditionReadReportButton.interactable = true;
            ExpeditionResetButton.interactable = true;
            ExpeditionPurchaseButton.interactable = true;
            ExpeditionEnhanceButton.interactable = true;
            ExpeditionCompareButton.interactable = true;
            ExpeditionContinueButton.interactable = true;
            ExpeditionExportButton.interactable = true;
            ExpeditionPurchaseButton.GetComponentInChildren<TMP_Text>().text = "采购 " + preparation.purchaseQuantity + " 份" + preparation.auxiliaryItemName + " @" + preparation.auxiliaryUnitPrice;
            ExpeditionReplaySettlementButton.interactable = snapshot.settlements != null && snapshot.settlements.Any(receipt => receipt.rewardsEligible);
            reportTab.interactable = activeReportPage != "Report";
            ledgerTab.interactable = activeReportPage != "Ledger";
            comparisonTab.interactable = activeReportPage != "Comparison";
            if (activeReportPage == "Ledger") reportText.text = SliceSession.ExportLedgerText();
            else if (activeReportPage == "Comparison") reportText.text = FormatComparison(snapshot.lastComparison);
            else reportText.text = SliceSession.ExportReportText();
            if (string.IsNullOrWhiteSpace(reportText.text))
                reportText.text = "尚无战斗记录。\n\n「逐场」与「一键派遣」推进同一个出征会话。\n每场胜利奖励只进入公共账本一次。\n\n先预测指令会如何选择，再派遣查看完整原因。";
            exportPathText.text = string.IsNullOrEmpty(LastExportDirectory)
                ? "导出尚未执行。点击导出后才写文件；默认保存到用户数据目录的 Reports 独立子目录。"
                : "已导出：" + LastExportDirectory;
            ReportScroll.verticalNormalizedPosition = 1;
        }

        private string FormatState(ExpeditionSliceSnapshot snapshot, PreparationScenario preparation, CombatExpeditionConfig combat)
        {
            var text = new StringBuilder();
            var party = snapshot.partyState;
            if (party == null)
            {
                // This is a kernel-owned, read-only initial preview; no Step or rewards are executed.
                party = new CombatSession(CombatSession.CopyScenario(combat.encounters[0].scenario)).GetSnapshot();
            }
            text.Append("当前队伍 · WAIT ").Append(Number(party.time)).AppendLine();
            foreach (var unit in party.units.Where(unit => unit.team == combat.carryTeam))
            {
                var mapping = SliceSession.GetConfiguration().equipmentMappings.FirstOrDefault(value => value.combatUnitId == unit.id);
                var name = mapping == null ? unit.name + " [" + unit.id + "]"
                    : preparation.combatantName + " [" + unit.id + " / " + mapping.preparationCombatantId + "]";
                var xp = snapshot.experience == null ? null : snapshot.experience.FirstOrDefault(grant => grant.unitId == unit.id);
                text.Append(name).Append("\nHP ").Append(unit.hp).Append('/').Append(unit.maxHp)
                    .Append("  MP ").Append(unit.mp).Append('/').Append(unit.maxMp).Append("  XP ").Append(xp == null ? 0 : xp.amount)
                    .Append("\n下次 WAIT ").Append(Number(unit.nextActionTime)).Append("  容量 ").Append(unit.instructionCapacity)
                    .Append("  智力 ").Append(unit.intelligence).AppendLine();
                text.Append("状态：");
                if (unit.statuses == null || unit.statuses.Length == 0) text.Append("无");
                else foreach (var status in unit.statuses)
                    text.Append(status.id).Append("(到 ").Append(Number(status.expiresAt)).Append(") ");
                text.Append("\n装备来源：").Append(string.IsNullOrEmpty(unit.equipmentSourceId) ? "无额外装备加值" : unit.equipmentSourceId)
                    .Append("  +").Append(unit.equipmentDamageBonus).AppendLine("\n");
            }
            text.Append("公共与私人归属\n公库现金 ").Append(snapshot.preparation.treasuryCash).Append("；")
                .Append(preparation.residentName).Append("钱包 ").Append(snapshot.preparation.residentCash)
                .Append("；合计 ").Append(snapshot.preparation.totalCash).AppendLine();
            text.Append("公有 ").Append(preparation.oreItemName).Append(' ').Append(snapshot.preparation.publicOre)
                .Append(" / ").Append(preparation.auxiliaryItemName).Append(' ').Append(snapshot.preparation.publicAuxiliary)
                .Append("\n").Append(preparation.residentName).Append("私有 ").Append(preparation.auxiliaryItemName).Append(' ').Append(snapshot.preparation.residentAuxiliary).AppendLine();
            text.Append("已装备：").Append(preparation.weaponName).Append(" [").Append(preparation.weaponInstanceId).Append("]\n所有者 ")
                .Append(snapshot.preparation.weaponOwnerId).Append("；装备者 ").Append(preparation.combatantName).Append(" [")
                .Append(preparation.combatantId).Append(" → ")
                .Append(SliceSession.GetConfiguration().equipmentMappings.First(value => value.preparationCombatantId == preparation.combatantId).combatUnitId).Append("]\n等级 ")
                .Append(snapshot.preparation.weaponEnhancementLevel).Append("；攻击加值 +").Append(snapshot.preparation.weaponAttackBonus).AppendLine();
            text.Append("强化配方：").Append(preparation.oreRequired).Append(' ').Append(preparation.oreItemName)
                .Append(" + ").Append(preparation.auxiliaryRequired).Append(' ').Append(preparation.auxiliaryItemName)
                .Append("\n累计实际消耗：").Append(snapshot.preparation.oreConsumed).Append(" / ").Append(snapshot.preparation.auxiliaryConsumed).AppendLine("\n");
            var mage = new CombatSession(CombatSession.CopyScenario(combat.encounters[0].scenario)).GetSnapshot().units.FirstOrDefault(unit => unit.id == "mage");
            text.AppendLine("法师当前配置 · 四段指令");
            if (mage != null)
            {
                for (var i = 0; i < mage.instructions.Length; i++)
                    text.Append(i + 1).Append(". ").Append(mage.instructions[i].display)
                        .Append(mage.instructions[i].capacityAllowed ? "" : " [容量不足]").AppendLine();
            }
            text.Append("\n验证与对照：回营间隔 ").Append(Number(SliceSession.GetConfiguration().comparison.betweenWait))
                .Append(" WAIT；无现金、物资或经验奖励。\n");
            if (snapshot.lastVerification != null && snapshot.lastVerification.success)
                text.Append("最近真实验证：").Append(snapshot.lastVerification.result.outcome).Append("，结束 WAIT ")
                    .Append(Number(snapshot.lastVerification.result.time)).AppendLine();
            return text.ToString();
        }

        private static string FormatComparison(EquipmentComparisonReport comparison)
        {
            if (comparison == null) return "尚未生成对照。\n\n完成出征、采购并强化后，点「同起点强化对照」。\n两分支携带相同 HP/MP、状态、WAIT、指令、敌情与种子，仅比较装备加值；不写公库或当前队伍。";
            if (!comparison.success) return comparison.code + " · " + comparison.reason;
            var display = new CombatComparisonReport
            {
                id = "slice-equipment-comparison",
                baselineLabel = "未强化",
                variantLabel = "已强化",
                changedSetting = comparison.changedSetting,
                onlyDeclaredSettingChanged = comparison.onlyEquipmentChanged,
                baselineConfiguration = comparison.baselineConfiguration,
                variantConfiguration = comparison.enhancedConfiguration,
                baseline = comparison.baseline,
                variant = comparison.enhanced,
                firstDivergence = comparison.firstDivergence,
                observableImpact = "此处为同一起点的只读对照，不改变当前队伍或发放奖励。"
            };
            var divergence = comparison.firstDivergence;
            var firstDifference = divergence == null ? "首次行动分歧：无"
                : "首次行动分歧 WAIT " + Number(divergence.baseline.time) + "\n未强化 " + DescribeEffects(divergence.baseline)
                    + " / 已强化 " + DescribeEffects(divergence.variant);
            return "同起点 WAIT " + Number(comparison.baselineStart.time) + "；仅装备 +" + comparison.baselineEquipmentBonus + " → +" + comparison.enhancedEquipmentBonus
                + "\n未强化结束 WAIT " + Number(comparison.baseline.time) + " / 已强化结束 WAIT " + Number(comparison.enhanced.time)
                + "\n" + firstDifference
                + "\n装备来源：" + comparison.equipmentSourceId + " → " + comparison.combatUnitId
                + "\n两分支均无额外掉落；当前真实队伍未改变。\n\n" + CombatSampleRuns.FormatComparisonMarkdown(display);
        }

        private static string DescribeEffects(CombatStepResult step)
        {
            if (step == null) return "无对应事件";
            var effects = step.effects == null ? "" : string.Join("；", step.effects.Select(effect =>
                effect.targetName + " HP " + effect.hpBefore + "→" + effect.hpAfter).ToArray());
            return step.actorName + "→" + step.actionName + (string.IsNullOrEmpty(effects) ? " " + step.message : " " + effects);
        }

        private static string RecentActionSummary(ExpeditionAdvanceResult advance)
        {
            var encounter = advance.encounters == null ? null : advance.encounters.LastOrDefault();
            if (encounter == null) return advance.code + " · " + advance.reason;
            var first = encounter.steps == null ? null : encounter.steps.FirstOrDefault(step => step.status == "Executed");
            if (first == null) return "第 " + encounter.encounterIndex + " 场 " + encounter.outcome + " · " + encounter.stopReason;
            var selectedAttempt = first.attempts == null ? null : first.attempts.LastOrDefault();
            return "第 " + encounter.encounterIndex + " 场 " + encounter.outcome + "，结束 WAIT " + Number(encounter.battleEnd.time)
                + " · 首行动 t=" + Number(first.time) + " " + first.actorName + "→" + first.actionName + "→" + first.targetName
                + "\n选中原因：" + (selectedAttempt == null ? first.message : selectedAttempt.reason) + "（完整候选与跳过原因见战报）";
        }

        private void ExportCurrentSession()
        {
            if (!RequireConfigured()) return;
            try
            {
                var root = ResolveReportRoot();
                var directory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                    + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var acceptance = SliceSession.GetAcceptance();
                var configurationDirectory = Path.Combine(directory, "configuration");
                Directory.CreateDirectory(configurationDirectory);
                WriteUtf8(Path.Combine(directory, "battle_report.txt"), SliceSession.ExportReportText());
                WriteUtf8(Path.Combine(directory, "preparation_ledger.txt"), SliceSession.ExportLedgerText());
                WriteUtf8(Path.Combine(directory, "preparation_ledger.csv"), SliceSession.ExportLedgerCsv());
                WriteUtf8(Path.Combine(directory, "session_result.json"), JsonUtility.ToJson(acceptance, true));
                WriteUtf8(Path.Combine(configurationDirectory, "integration_loaded.json"), loadedIntegrationJson);
                WriteUtf8(Path.Combine(configurationDirectory, "combat_loaded.json"), loadedCombatJson);
                WriteUtf8(Path.Combine(configurationDirectory, "preparation_loaded.json"), loadedPreparationJson);
                WriteUtf8(Path.Combine(configurationDirectory, "integration_current.json"), JsonUtility.ToJson(SliceSession.GetConfiguration(), true));
                WriteUtf8(Path.Combine(configurationDirectory, "combat_current.json"), JsonUtility.ToJson(SliceSession.GetCombatConfiguration(), true));
                WriteUtf8(Path.Combine(configurationDirectory, "preparation_current.json"), JsonUtility.ToJson(SliceSession.GetPreparationConfiguration(), true));
                WriteUtf8(Path.Combine(directory, "export_manifest.json"), JsonUtility.ToJson(new ExportManifest
                {
                    schemaVersion = 1,
                    exportedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    currentExpeditionId = acceptance.snapshot.currentExpeditionId,
                    outputDirectory = directory,
                    recordedComparison = acceptance.comparison != null && acceptance.comparison.success,
                    recordedVerificationCount = acceptance.verifications == null ? 0 : acceptance.verifications.Length,
                    readOnlyExport = true
                }, true));
                LastExportDirectory = directory;
                LastExportError = null;
                Refresh(false);
                SetFeedback("已导出当前服务真实记录与配置快照。导出没有启动战斗、生成对照或重复结算。", false);
            }
            catch (Exception ex)
            {
                LastExportError = ex.ToString();
                ShowException("导出本次记录", ex);
            }
        }

        private string ResolveReportRoot()
        {
            var requested = ReportDirectoryOverride;
            if (string.IsNullOrWhiteSpace(requested))
            {
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], "--prototype-report-dir", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                            throw new ArgumentException("--prototype-report-dir 后需要绝对路径。");
                        requested = args[i + 1];
                        break;
                    }
            }
            if (string.IsNullOrWhiteSpace(requested)) return Path.Combine(Application.persistentDataPath, "Reports");
            if (!Path.IsPathRooted(requested)) throw new ArgumentException("战报导出目录必须是绝对路径：" + requested);
            var pathRoot = Path.GetPathRoot(requested);
            if (Path.DirectorySeparatorChar == '\\' && !(pathRoot.StartsWith("\\\\", StringComparison.Ordinal)
                || (pathRoot.Length >= 3 && pathRoot[1] == ':' && (pathRoot[2] == '\\' || pathRoot[2] == '/'))))
                throw new ArgumentException("战报导出目录必须包含盘符根目录或完整 UNC 路径：" + requested);
            return Path.GetFullPath(requested);
        }

        private static void WriteUtf8(string path, string text)
        {
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
        }

        private bool RequireConfigured()
        {
            if (SliceSession != null) return true;
            SetFeedback("出征配置尚未成功加载；请检查 StreamingAssets/Prototype/Expedition/default.json 与其引用路径。", true);
            return false;
        }

        private void DisableUnconfiguredControls()
        {
            foreach (var button in GetComponentsInChildren<Button>(true)) button.interactable = false;
            ExpeditionThresholdInput.interactable = false;
        }

        private void ShowException(string operation, Exception exception)
        {
            LastError = exception.ToString();
            SetFeedback(operation + "失败：" + exception.GetType().Name + " · " + exception.Message, true);
        }

        private void SetFeedback(string message, bool error)
        {
            LastActionMessage = message;
            feedbackText.text = message;
            feedbackText.color = error ? Ui.Bad : Ui.Good;
        }

        private static string PhaseName(string phase)
        {
            return phase == "Camp" ? "营地" : phase == "Expedition" ? "出征中" : phase == "Completed" ? "本次结束" : phase;
        }

        private static string Number(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }

        [Serializable]
        private sealed class ExportManifest
        {
            public int schemaVersion;
            public string exportedAtUtc;
            public string currentExpeditionId;
            public string outputDirectory;
            public bool recordedComparison;
            public int recordedVerificationCount;
            public bool readOnlyExport;
        }
    }
}
