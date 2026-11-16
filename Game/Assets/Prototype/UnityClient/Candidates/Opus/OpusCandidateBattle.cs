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
    public sealed partial class OpusCandidateView
    {
        private RectTransform battleColumns, waitPane, fieldPane, causePane, queueRows, eventRows, causeRows, inspector;
        private ScrollRect eventScroll;
        private TMP_Text justActed, clash, historyWarning, eventCount, inspectorText;
        private Image inspectorPortrait;
        private string queueKey, eventKey, causeKey;
        private bool diagnostics;
        private readonly HashSet<int> expandedAttempts = new HashSet<int>();
        private readonly List<Button> eventButtons = new List<Button>();
        public Button StepButton { get; private set; }
        public Button AutoButton { get; private set; }
        public Button PauseButton { get; private set; }
        public Button BattleReplayButton { get; private set; }
        public Button FollowLatestButton { get; private set; }
        public Button DiagnosticsButton { get; private set; }
        public Button CloseInspectorButton { get; private set; }
        public TMP_InputField IntervalInput { get; private set; }
        public TMP_Text ClockText { get; private set; }
        public TMP_Text ReasonText { get; private set; }
        public ScrollRect CauseScroll { get; private set; }
        public OpusBattleBoard FriendlyBoard { get; private set; }
        public OpusBattleBoard EnemyBoard { get; private set; }
        public IReadOnlyList<Button> EventButtons { get { return eventButtons; } }
        public string[] HighlightedTargetIds { get { return state?.highlightedTargetIds ?? Array.Empty<string>(); } }
        public string SelectedEffectsSummary { get { return state?.selectedEffectsSummary ?? ""; } }
        public bool IsAutoPlaying { get { return state != null && state.isAutoPlaying; } }
        private void BuildBattle()
        {
            var controls = OpusUi.Row("Opus_PlaybackBar", combat, 5); Ui.FixedHeight(controls.gameObject, 48);
            StepButton = OpusUi.Button("Opus_Step", controls, font, "单步", () => shared.Step(), 70, 36);
            AutoButton = OpusUi.Button("Opus_Auto", controls, font, "自动播放", () => shared.StartAuto(), 100, 36);
            PauseButton = OpusUi.Button("Opus_Pause", controls, font, "暂停", () => shared.Pause(), 70, 36);
            BattleReplayButton = OpusUi.Button("Opus_BattleReplay", controls, font, "同开局重放", Replay, 126, 36);
            OpusUi.Button("Opus_IntervalMinus", controls, font, "−", () => shared.SetPlaybackInterval(shared.GetState().intervalSeconds - .05), 28, 32);
            IntervalInput = OpusUi.Input("Opus_PlaybackInterval", controls, font, 58);
            IntervalInput.onEndEdit.AddListener(raw => { double value; if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) shared.SetPlaybackInterval(value); else shared.NotifyUser("间隔请输入秒数。", true); pendingRefresh = true; });
            OpusUi.Button("Opus_IntervalPlus", controls, font, "+", () => shared.SetPlaybackInterval(shared.GetState().intervalSeconds + .05), 28, 32);
            var seconds = OpusUi.Text("SecondsUnit", controls, font, "秒", 13, T.Secondary, true); Ui.Width(seconds.gameObject, 22); seconds.alignment = TextAlignmentOptions.MidlineLeft;
            ClockText = OpusUi.Text("Opus_ActualClockAndThreeLayers", controls, font, "", 13, T.Amber); Ui.Flexible(ClockText.gameObject);
            OpusUi.Button("Opus_BackToInscription", controls, font, "返回编排", () => ShowPage(false), 110, 36);
            battleColumns = OpusUi.Rect("Opus_QueueFieldCauseColumns", combat); Ui.Flexible(battleColumns.gameObject);
            waitPane = OpusUi.Column("Opus_VerticalWaitQueue", battleColumns, 8, 8, T.Panel);
            fieldPane = OpusUi.Column("Opus_EnemyAboveFriendlyField", battleColumns, 4, 4, T.Panel);
            causePane = OpusUi.Column("Opus_CausePanel", battleColumns, 6, 8, T.Panel);
            OpusUi.Label("WaitTitle", waitPane, font, "行动队列 · WAIT", 15, 28);
            justActed = OpusUi.Label("Opus_ActuallyLastActor", waitPane, font, "尚无行动", 13, 56);
            queueRows = OpusUi.Scroll("Opus_NextActualReadyTimes", waitPane, out _, 6);
            var queueRule = OpusUi.Label("Opus_TieOrderHint", waitPane, font, "同刻按配置顺序\n倒下单位不列入", 12, 40); queueRule.color = T.Secondary;
            OpusUi.Label("EnemyHeading", fieldPane, font, "敌方阵位 · 后排在上", 15, 24).color = T.Red;
            var enemyGrid = OpusUi.Rect("Opus_EnemyTrue3x3", fieldPane); Ui.Flexible(enemyGrid.gameObject); enemyGrid.GetComponent<LayoutElement>().minHeight = 288;
            EnemyBoard = enemyGrid.gameObject.AddComponent<OpusBattleBoard>(); EnemyBoard.Initialize(font, true, id => shared.GetPortrait(id), id => shared.InspectUnit(id));
            var clashBand = OpusUi.Panel("Opus_HorizontalClashLine", fieldPane, T.Top); Ui.FixedHeight(clashBand.gameObject, 52);
            var lineTop = OpusUi.Panel("InscribedLineTop", clashBand.transform, T.StrongLine); OpusUi.Band(lineTop.rectTransform, 2, 0, 2, 1);
            clash = OpusUi.Text("Opus_ActualStepSentence", clashBand.transform, font, "尚未应用编排", 15, T.Ink); Ui.Stretch(clash.rectTransform, 8, 2, 8, 2); clash.alignment = TextAlignmentOptions.Center;
            var friendlyGrid = OpusUi.Rect("Opus_FriendlyTrue3x3", fieldPane); Ui.Flexible(friendlyGrid.gameObject); friendlyGrid.GetComponent<LayoutElement>().minHeight = 288;
            FriendlyBoard = friendlyGrid.gameObject.AddComponent<OpusBattleBoard>(); FriendlyBoard.Initialize(font, false, id => shared.GetPortrait(id), id => shared.InspectUnit(id));
            OpusUi.Label("FriendlyHeading", fieldPane, font, "己方阵位 · 后排在下", 15, 24).color = T.Green;
            var legend = OpusUi.Label("Opus_FieldIsCurrentLegend", fieldPane, font, "琥珀：行动 · 实线：覆盖 · 蓝角标：附加 · 棋盘为当前状态", 12, 20); legend.color = T.Secondary;

            var eventHead = OpusUi.Row("EventHeader", causePane, 5); Ui.FixedHeight(eventHead.gameObject, 32);
            eventCount = OpusUi.Text("Opus_RecordedEventCount", eventHead, font, "事件记录", 15); Ui.Flexible(eventCount.gameObject);
            FollowLatestButton = OpusUi.Button("Opus_FollowLatest", eventHead, font, "跟随最新 ✓", () => shared.SetFollowLatest(!shared.GetState().followLatest), 140, 32);
            eventRows = OpusUi.Scroll("Opus_ActualEventLog", causePane, out eventScroll, 3); Ui.FixedHeight(eventRows.parent.parent.gameObject, 220);
            historyWarning = OpusUi.Text("Opus_HistoricalEffectCurrentBoard", causePane, font, "", 13, T.History); Ui.FixedHeight(historyWarning.gameObject, 40);
            DiagnosticsButton = OpusUi.Button("Opus_Diagnostics", causePane, font, "[+] 诊断字段", () => { diagnostics = !diagnostics; OpusUi.Title(DiagnosticsButton, diagnostics ? "[-] 诊断字段" : "[+] 诊断字段"); causeKey = null; pendingRefresh = true; }, 0, 32);
            causeRows = OpusUi.Scroll("Opus_CausalDetails", causePane, out var causeScroll, 7); CauseScroll = causeScroll;
            BuildInspector();
        }
        private void BuildInspector()
        {
            inspector = OpusUi.Column("Opus_UnitInspectorDrawer", causePane, 10, 12, T.Panel); Ui.Stretch(inspector);
            var ignore = inspector.gameObject.AddComponent<LayoutElement>(); ignore.ignoreLayout = true;
            var head = OpusUi.Row("InspectorHeader", inspector); Ui.FixedHeight(head.gameObject, 96);
            inspectorPortrait = OpusUi.Panel("ActualPortrait", head, Color.white); inspectorPortrait.preserveAspect = true; Ui.Width(inspectorPortrait.gameObject, 96);
            var label = OpusUi.Text("InspectorTitle", head, font, "角色检视\n当前真实状态", 17); Ui.Flexible(label.gameObject);
            CloseInspectorButton = OpusUi.Button("Opus_CloseInspector", head, font, "×", () => shared.SelectEvent(shared.GetState().selectedEventIndex), 34, 34);
            inspectorText = OpusUi.ScrollText("Opus_UnitFullStatusAndCommittedRules", inspector, font, out _, 15);
            inspector.gameObject.SetActive(false);
        }
        private void LayoutBattle(float width)
        {
            float extra = Mathf.Clamp01((width - 1248) / 320), left = 176 + 40 * extra, middle = 560 + 80 * extra;
            SetColumn(waitPane, 0, left); SetColumn(fieldPane, left + 12, middle); SetColumn(causePane, left + middle + 24, Mathf.Max(400, width - left - middle - 24));
        }
        private void RenderBattle()
        {
            var snapshot = state.battle; var history = state.history ?? Array.Empty<CombatStepResult>();
            var selected = state.selectedEventIndex >= 0 && state.selectedEventIndex < history.Length ? history[state.selectedEventIndex] : null;
            bool playable = snapshot != null && !snapshot.finished && !snapshot.blocked;
            StepButton.interactable = AutoButton.interactable = playable && !state.isAutoPlaying; PauseButton.interactable = state.isAutoPlaying; BattleReplayButton.interactable = snapshot != null;
            SetText(IntervalInput, OpusUi.N(state.intervalSeconds));
            ClockText.text = snapshot == null ? "尚未开战 · 表单→草稿→上场" : "t=" + OpusUi.N(snapshot.time) + " WAIT · "
                + (snapshot.finished ? "已结束 " + snapshot.outcome : state.isAutoPlaying ? "播放中" : "已暂停") + " · 上场 v" + state.workbench.committedRevision
                + "\n" + (state.form.hasUnsubmittedChanges ? "表单未写入 · " : "表单已同步 · ") + "草稿 v" + state.workbench.draftRevision + (state.workbench.hasUncommittedChanges ? " 未上场，重放仍用旧配置" : " 与上场一致");
            bool historical = selected != null && state.selectedEventIndex < history.Length - 1;
            var team = state.workbench.draftScenario.playerTeam; var enemy = state.workbench.draftScenario.units.FirstOrDefault(unit => unit.team != team)?.team;
            FriendlyBoard.Render(snapshot, team, selected, state.highlightedTargetIds, historical); EnemyBoard.Render(snapshot, enemy, selected, state.highlightedTargetIds, historical);
            historyWarning.text = historical ? "正在查看历史 #" + selected.index + "（t=" + OpusUi.N(selected.time) + "）；效果为当时前后值，棋盘保持当前 t=" + OpusUi.N(snapshot.time)
                : "事件详情为实际结算记录；棋盘始终显示当前状态。";
            if (snapshot == null) clash.text = "尚未应用编排\n棋盘没有运行中的战斗";
            else if (snapshot.finished) clash.text = (snapshot.outcome == "Victory" ? "胜利" : snapshot.outcome == "Defeat" ? "失败" : "本场结束") + " · t=" + OpusUi.N(snapshot.time) + "\n" + snapshot.stopReason;
            else clash.text = selected == null ? "尚无实际事件" : selected.actorName + " → " + (string.IsNullOrEmpty(selected.actionName) ? "状态 / 控制事件" : selected.actionName)
                + " → 覆盖 " + HighlightedTargetIds.Length + " 人\n一次扣费 MP " + selected.mpCost + " · 一次 WAIT +" + OpusUi.N(selected.actualWait);
            RenderQueue(); RenderEvents(); RenderCause(selected);
            var unit = snapshot?.units.FirstOrDefault(item => item.id == state.inspectedUnitId);
            inspector.gameObject.SetActive(unit != null);
            if (unit != null)
            {
                inspector.SetAsLastSibling(); inspectorPortrait.sprite = shared.GetPortrait(unit.id); inspectorPortrait.color = inspectorPortrait.sprite == null ? T.Raised : Color.white;
                inspectorText.text = OpusUi.Esc(OpusBattleText.Unit(unit, state.workbench.committedRevision));
            }
        }
        private void RenderQueue()
        {
            var history = state.history ?? Array.Empty<CombatStepResult>(); var last = history.LastOrDefault();
            justActed.text = last == null ? "刚行动：无\n尚无实际事件" : "刚行动\nt=" + OpusUi.N(last.time) + " " + last.actorName + "\n" + last.actionName;
            var units = state.battle?.units ?? Array.Empty<CombatUnitSnapshot>();
            string key = string.Join(";", units.Select(unit => unit.id + ":" + unit.name + ":" + unit.hp + ":" + unit.nextActionTime + ":" + unit.dormant + ":"
                + string.Join(",", (unit.statuses ?? Array.Empty<CombatStatusConfig>()).Select(status => status.id + ":" + status.expiresAt))));
            if (key == queueKey) return; queueKey = key; OpusUi.Clear(queueRows);
            foreach (var unit in units.Where(unit => unit.hp > 0 && !unit.dormant).OrderBy(unit => unit.nextActionTime))
            {
                var row = OpusUi.Row("Opus_Queue_" + unit.id, queueRows, 4); Ui.FixedHeight(row.gameObject, 56);
                var time = OpusUi.Text("ActualReady", row, font, OpusUi.N(unit.nextActionTime), 20, unit.team == state.workbench.draftScenario.playerTeam ? T.Green : T.Red, true);
                Ui.Width(time.gameObject, 48); time.alignment = TextAlignmentOptions.MidlineLeft;
                OpusUi.Sprite("Portrait", row, font, shared.GetPortrait(unit.id), unit.name.Substring(0, 1), 24);
                var name = OpusUi.Text("NameAndStatus", row, font, unit.name + "\n" + OpusBattleText.ShortStatuses(unit.statuses), 13, T.Secondary); Ui.Flexible(name.gameObject);
            }
        }
        private void RenderEvents()
        {
            var history = state.history ?? Array.Empty<CombatStepResult>(); eventCount.text = "事件记录 · " + history.Length + " 条";
            OpusUi.Title(FollowLatestButton, state.followLatest ? "跟随最新 ✓" : "跟随最新 ○");
            string key = state.selectedEventIndex + ":" + string.Join(";", history.Select(step => step.index + ":" + step.actorName + ":" + step.actionName + ":" + step.targetName + ":" + step.time));
            if (key == eventKey) return; eventKey = key; OpusUi.Clear(eventRows); eventButtons.Clear();
            for (int i = history.Length - 1; i >= 0; i--)
            {
                int at = i; var step = history[i];
                var button = OpusUi.Button("Opus_Event_" + i, eventRows, font, "#" + step.index + " t=" + OpusUi.N(step.time) + " " + step.actorName + " · "
                    + (string.IsNullOrEmpty(step.actionName) ? "状态 / 控制" : step.actionName) + " → " + step.targetName,
                    () => { shared.SelectEvent(at); expandedAttempts.Clear(); causeKey = null; }, 0, 30);
                button.GetComponentInChildren<TMP_Text>().fontSize = 13;
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                button.GetComponent<Image>().color = i == state.selectedEventIndex ? T.Raised : T.Panel;
                eventButtons.Add(button);
            }
            if (state.selectedEventIndex == history.Length - 1) eventScroll.verticalNormalizedPosition = 1;
        }
        private void RenderCause(CombatStepResult step)
        {
            string key = (step == null ? "none" : JsonUtility.ToJson(step))
                + ":" + diagnostics + ":" + string.Join(",", expandedAttempts.OrderBy(x => x));
            if (key == causeKey) return; causeKey = key; OpusUi.Clear(causeRows);
            if (step == null) { ReasonText = OpusUi.Text("Opus_NoActualCause", causeRows, font, "尚无本场事件。应用编排后可单步观察。", 15, T.Secondary); OpusUi.ContentText(ReasonText); return; }
            ReasonText = OpusUi.Text("Opus_ActualSelectedEvent", causeRows, font, step.actorName + " → " + step.actionName + "\n覆盖：" + string.Join("、", HighlightedTargetIds.Select(id => OpusBattleText.Name(state.battle, id)))
                + "\n施放者 MP " + step.mpBefore + "→" + step.mpAfter + " · 一次费用 " + step.mpCost + "\nWAIT " + OpusUi.N(step.baseWait) + "×" + OpusUi.N(step.baseAgilityFactor) + "×" + OpusUi.N(step.statusWaitFactor)
                + "=" + OpusUi.N(step.actualWait) + " → 下次 t=" + OpusUi.N(step.nextActionTime), 15);
            OpusUi.ContentText(ReasonText);
            if (!string.IsNullOrEmpty(step.controlReason)) CauseText(step.controlReason == "Stunned" ? "眩晕阻止行动" : step.controlReason == "Silenced" ? "沉默限制法术" : step.controlReason, T.Purple);
            if (step.timeTieResolved) CauseText("同刻次序：" + step.tieBreakReason, T.Secondary);
            foreach (var attempt in step.attempts ?? Array.Empty<CombatInstructionAttempt>())
            {
                int index = attempt.instructionIndex;
                var toggle = OpusUi.Button("Opus_AttemptCandidates_" + index, causeRows, font,
                    (attempt.outcome == "Selected" ? "✓ " : "× ") + "规则 " + (index + 1) + (expandedAttempts.Contains(index) ? " [-] 候选" : " [+] 候选"),
                    () => { if (!expandedAttempts.Add(index)) expandedAttempts.Remove(index); causeKey = null; pendingRefresh = true; }, 0, 32);
                toggle.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                CauseText(attempt.display + "\n" + attempt.reason, attempt.outcome == "Selected" ? T.Green : attempt.reasonCode == "Silenced" ? T.Purple : T.Secondary);
                if (expandedAttempts.Contains(index)) foreach (var candidate in attempt.candidates ?? Array.Empty<CombatCandidateTrace>())
                    CauseText("· " + candidate.unitName + (candidate.eligible ? " ✓ " : " × ") + candidate.reason, T.Secondary);
                if (diagnostics) CauseText("instructionId=" + attempt.instructionId + " reasonCode=" + attempt.reasonCode, T.Secondary);
            }
            var effects = step.effects ?? Array.Empty<CombatEffect>();
            foreach (bool main in new[] { true, false })
            {
                var group = effects.Where(effect => HighlightedTargetIds.Contains(effect.targetId) == main).ToArray(); if (group.Length == 0) continue;
                CauseText(main ? "主覆盖" : "附加效果（非覆盖目标）", main ? T.Green : T.Blue);
                foreach (var effect in group) CauseText(OpusBattleText.Effect(effect, StatusDisplay, diagnostics), OpusBattleBoard.EffectColor(effect.kind));
            }
            CauseText(step.message, T.Secondary);
            if (diagnostics) CauseText("status=" + step.status + " actionId=" + step.actionId + " anchor=" + step.targetId + " actual targets=[" + string.Join(",", HighlightedTargetIds) + "]", T.Secondary);
        }
        private void CauseText(string value, Color color)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!diagnostics && state.battle != null) foreach (var unit in state.battle.units.OrderByDescending(unit => unit.id.Length)) value = value.Replace(unit.id, unit.name);
            var text = OpusUi.Text("Opus_NativeCauseOrEffect", causeRows, font, OpusUi.Esc(value), 13, color); OpusUi.ContentText(text);
        }
        private string StatusDisplay(string id)
        {
            var scenario = state.workbench.committedScenario ?? state.workbench.draftScenario;
            var initial = scenario.units.SelectMany(unit => unit.statuses ?? Array.Empty<CombatStatusConfig>()).FirstOrDefault(status => status.id == id);
            if (initial != null) return OpusBattleText.StatusName(initial.kind);
            return OpusBattleText.StatusName(scenario.actions.FirstOrDefault(action => action.statusId == id)?.statusKind ?? "状态");
        }
    }
}
