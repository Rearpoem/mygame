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
    public sealed partial class HydrogenCandidateView
    {
        private RectTransform queueContent;
        private RectTransform eventList;
        private RectTransform battleDetails;
        private RectTransform waitQueueBand;
        private ScrollRect eventScroll;
        private TMP_Text clockText;
        private TMP_Text playbackStatus;
        private TMP_Text historyBanner;
        private TMP_Text bridgeActor;
        private TMP_Text bridgeAction;
        private TMP_Text bridgeTargets;
        private TMP_Text bridgeCosts;
        private TMP_Text bridgeArrow;
        private string eventSignature;
        private string queueSignature;
        private int previousEventCount;
        private bool reverseEvents;
        private bool showUnitDetail;
        private bool technical;
        private string lastInspectedUnitId;
        private string reasonSignature;
        private bool reasonScrollPending;
        private readonly List<Button> eventButtons = new List<Button>();
        public Button StepButton { get; private set; }
        public Button AutoButton { get; private set; }
        public Button PauseButton { get; private set; }
        public Button BattleReplayButton { get; private set; }
        public Button EventDetailsButton { get; private set; }
        public Button UnitDetailsButton { get; private set; }
        public Button TechnicalFieldsButton { get; private set; }
        public Button LatestEventButton { get; private set; }
        public TMP_InputField IntervalInput { get; private set; }
        public TMP_Text ReasonText { get; private set; }
        public ScrollRect ReasonScroll { get; private set; }
        public TMP_Text HistoryBannerText { get { return historyBanner; } }
        public TMP_Text ClockText { get { return clockText; } }
        public HydrogenBattleBoard FriendlyBoard { get; private set; }
        public HydrogenBattleBoard EnemyBoard { get; private set; }
        public IReadOnlyList<Button> EventButtons { get { return eventButtons; } }
        public string[] HighlightedTargetIds { get { return state?.highlightedTargetIds ?? Array.Empty<string>(); } }
        public string HighlightedActorId { get { return state?.highlightedActorId; } }
        public string SelectedEffectsSummary { get { return state?.selectedEffectsSummary ?? ""; } }
        public int SelectedEventIndex { get { return state?.selectedEventIndex ?? -1; } }
        public bool IsAutoPlaying { get { return state != null && state.isAutoPlaying; } }

        private void BuildBattle()
        {
            var layout = battleRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var toolbar = HydrogenUi.Row("Hydrogen_LivePlaybackControls", battleRoot, 6);
            Ui.FixedHeight(toolbar.gameObject, 36);
            HydrogenUi.Button("Hydrogen_ReturnToAssembly", toolbar, font, "← 返回编排", () => RequestSwitch(ShowAssembly), 116);
            StepButton = HydrogenUi.Button("Hydrogen_Step", toolbar, font, "单步", () => shared.Step(), 70);
            AutoButton = HydrogenUi.Button("Hydrogen_Auto", toolbar, font, "自动播放", () => shared.StartAuto(), 98);
            PauseButton = HydrogenUi.Button("Hydrogen_Pause", toolbar, font, "暂停", () => shared.Pause(), 70);
            BattleReplayButton = HydrogenUi.Button("Hydrogen_BattleReplay", toolbar, font, "重放本场", Replay, 100);
            var intervalLabel = HydrogenUi.Text("Hydrogen_IntervalLabel", toolbar, font, "间隔秒", 13, T.Secondary, true);
            Ui.Width(intervalLabel.gameObject, 44);
            intervalLabel.alignment = TextAlignmentOptions.MidlineLeft;
            IntervalInput = HydrogenUi.Input("Hydrogen_Interval", toolbar, font, 66);
            IntervalInput.onEndEdit.AddListener(raw =>
            {
                if (refreshing) return;
                double seconds;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) shared.SetPlaybackInterval(seconds);
                else shared.NotifyUser("播放间隔请输入秒数。", true);
                RefreshView();
            });
            playbackStatus = HydrogenUi.Text("Hydrogen_PlaybackState", toolbar, font, "", 14, T.Gold);
            playbackStatus.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Flexible(playbackStatus.gameObject);

            var queue = HydrogenUi.Row("Hydrogen_WaitSequenceTrack", battleRoot, 8);
            waitQueueBand = queue;
            Ui.FixedHeight(queue.gameObject, 38);
            clockText = HydrogenUi.Text("Hydrogen_ActualWaitClock", queue, font, "WAIT —", 16, T.Cyan, true);
            clockText.fontStyle = FontStyles.Bold;
            clockText.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Width(clockText.gameObject, 110);
            var track = HydrogenUi.Panel("Hydrogen_QueuedReadyTimes", queue, T.Input);
            Ui.Flexible(track.gameObject);
            var queueScroll = track.gameObject.AddComponent<ScrollRect>();
            queueScroll.horizontal = true;
            queueScroll.vertical = false;
            queueScroll.movementType = ScrollRect.MovementType.Clamped;
            queueScroll.scrollSensitivity = 24;
            var viewport = HydrogenUi.Rect("Viewport", track.transform);
            Ui.Stretch(viewport, 4, 2, 4, 2);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            queueContent = HydrogenUi.Row("ActualReadyOrder", viewport, 6);
            queueContent.anchorMin = new Vector2(0, 0);
            queueContent.anchorMax = new Vector2(0, 1);
            queueContent.pivot = new Vector2(0, .5f);
            queueContent.offsetMin = queueContent.offsetMax = Vector2.zero;
            queueContent.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            queueScroll.viewport = viewport;
            queueScroll.content = queueContent;
            historyBanner = HydrogenUi.Text("Hydrogen_HistoryIsNotRewind", battleRoot, font, "", 13, T.Cyan);
            Ui.FixedHeight(historyBanner.gameObject, 23);

            var stage = HydrogenUi.Rect("Hydrogen_TwoBoardsAndActionBridge", battleRoot);
            Ui.Flexible(stage.gameObject);
            stage.GetComponent<LayoutElement>().minHeight = 366;
            FriendlyBoard = BuildSide(stage, "Hydrogen_FriendlyBoard", "己方阵位 · C0 / C1 / C2", 0, .43f, T.Emerald);
            EnemyBoard = BuildSide(stage, "Hydrogen_EnemyBoard", "敌方阵位 · C0 / C1 / C2", .57f, 1, T.Crimson);
            var bridge = HydrogenUi.Column("Hydrogen_ActionVectorBridge", stage, 10, 10, T.Panel);
            bridge.anchorMin = new Vector2(.43f, 0);
            bridge.anchorMax = new Vector2(.57f, 1);
            bridge.offsetMin = new Vector2(5, 5);
            bridge.offsetMax = new Vector2(-5, -5);
            var bridgeTitle = HydrogenUi.Text("BridgeHeading", bridge, font, "实际行动矢量", 13, T.Secondary);
            Ui.FixedHeight(bridgeTitle.gameObject, 23);
            bridgeActor = HydrogenUi.Text("BridgeActor", bridge, font, "尚未开战", 16, T.Gold);
            Ui.FixedHeight(bridgeActor.gameObject, 43);
            bridgeArrow = HydrogenUi.Text("BridgeDirection", bridge, font, "→", 30, T.Gold);
            bridgeArrow.alignment = TextAlignmentOptions.Center;
            Ui.FixedHeight(bridgeArrow.gameObject, 38);
            bridgeAction = HydrogenUi.Text("BridgeAction", bridge, font, "应用编排后显示真实行动", 18, T.Ink);
            bridgeAction.alignment = TextAlignmentOptions.Center;
            Ui.FixedHeight(bridgeAction.gameObject, 57);
            bridgeCosts = HydrogenUi.Text("BridgeActualCosts", bridge, font, "", 14, T.Cyan);
            Ui.FixedHeight(bridgeCosts.gameObject, 66);
            bridgeTargets = HydrogenUi.Text("BridgeActualCoverage", bridge, font, "", 13, T.Secondary);
            Ui.Flexible(bridgeTargets.gameObject);

            var details = HydrogenUi.Rect("Hydrogen_EventsAndCausalInspector", battleRoot);
            battleDetails = details;
            Ui.FixedHeight(details.gameObject, 220);
            var eventPane = Region("Hydrogen_ActualEventStream", details, 0, .385f);
            var eventHeader = HydrogenUi.Row("EventStreamCommands", eventPane, 5);
            Ui.FixedHeight(eventHeader.gameObject, 28);
            var eventTitle = HydrogenUi.Text("EventTitle", eventHeader, font, "真实事件流", 15, T.Gold);
            Ui.Flexible(eventTitle.gameObject);
            LatestEventButton = HydrogenUi.Button("Hydrogen_LatestEvent", eventHeader, font, "最新", () =>
            {
                showUnitDetail = false;
                reasonScrollPending = true;
                var history = shared.GetState().history;
                if (history.Length > 0) shared.SelectEvent(history.Length - 1);
            }, 58, false, 28);
            HydrogenUi.Button("Hydrogen_ReverseEvents", eventHeader, font, "倒序", () => { reverseEvents = !reverseEvents; eventSignature = null; RefreshView(); }, 58, false, 28);
            eventList = HydrogenUi.ScrollColumn("Hydrogen_SelectableActualEvents", eventPane, out eventScroll, 3, 3);
            var inspector = Region("Hydrogen_ProgressiveCausalInspector", details, .385f, 1);
            var tabs = HydrogenUi.Row("Hydrogen_CausalityAndUnitTabs", inspector, 6);
            Ui.FixedHeight(tabs.gameObject, 28);
            EventDetailsButton = HydrogenUi.Button("Hydrogen_EventCausalityTab", tabs, font, "事件因果解释", () => { showUnitDetail = false; RefreshView(); }, 130, false, 28);
            UnitDetailsButton = HydrogenUi.Button("Hydrogen_UnitDetailsTab", tabs, font, "单位完整详情", () => { showUnitDetail = true; RefreshView(); }, 130, false, 28);
            TechnicalFieldsButton = HydrogenUi.Button("Hydrogen_TechnicalFields", tabs, font, "展开技术字段", () => { technical = !technical; RefreshView(); }, 132, false, 28);
            ReasonText = HydrogenUi.ScrollText("Hydrogen_SelectedActualReasons", inspector, font, out var reasons, 14);
            ReasonText.margin = new Vector4(0, 2, 0, 2);
            ReasonText.extraPadding = false;
            ReasonScroll = reasons;
        }

        private HydrogenBattleBoard BuildSide(RectTransform stage, string name, string title, float left, float right, Color color)
        {
            var panel = HydrogenUi.Column(name + "_Panel", stage, 4, 4, T.Panel);
            panel.anchorMin = new Vector2(left, 0);
            panel.anchorMax = new Vector2(right, 1);
            panel.offsetMin = new Vector2(0, 0);
            panel.offsetMax = new Vector2(0, 0);
            var heading = HydrogenUi.Text(name + "_Heading", panel, font, title, 15, color);
            Ui.FixedHeight(heading.gameObject, 23);
            var grid = HydrogenUi.Rect(name, panel);
            Ui.Flexible(grid.gameObject);
            var board = grid.gameObject.AddComponent<HydrogenBattleBoard>();
            board.Initialize(font, id => shared.GetPortrait(id), id => { showUnitDetail = true; shared.InspectUnit(id); });
            return board;
        }

        private void RefreshBattle()
        {
            Ui.FixedHeight(waitQueueBand.gameObject, compactLayout ? 30 : 38);
            Ui.FixedHeight(historyBanner.gameObject, compactLayout ? 22 : 23);
            Ui.FixedHeight(battleDetails.gameObject, compactLayout
                ? (switchGuard.gameObject.activeSelf ? 132 : 168)
                : (switchGuard.gameObject.activeSelf ? 180 : 220));
            var snapshot = state.battle;
            var history = state.history ?? Array.Empty<CombatStepResult>();
            var selected = state.selectedEventIndex >= 0 && state.selectedEventIndex < history.Length ? history[state.selectedEventIndex] : null;
            bool playable = snapshot != null && !snapshot.finished && !snapshot.blocked;
            StepButton.interactable = playable && !state.isAutoPlaying;
            AutoButton.interactable = playable && !state.isAutoPlaying;
            PauseButton.interactable = state.isAutoPlaying;
            TintPlaybackButton(AutoButton, state.isAutoPlaying, state.isAutoPlaying ? "播放中" : "自动播放");
            TintPlaybackButton(PauseButton, snapshot != null && !state.isAutoPlaying && !snapshot.finished,
                snapshot != null && !state.isAutoPlaying && !snapshot.finished ? "已暂停" : "暂停");
            BattleReplayButton.interactable = snapshot != null;
            if (!IntervalInput.isFocused) IntervalInput.SetTextWithoutNotify(HydrogenBattleText.N(state.intervalSeconds));
            clockText.text = snapshot == null ? "WAIT —" : "WAIT " + HydrogenBattleText.N(snapshot.time);
            playbackStatus.text = snapshot == null ? "尚未应用编排" : snapshot.finished ? Outcome(snapshot.outcome)
                : state.isAutoPlaying ? "正在播放 · 逐步推进" : "已暂停 · 可单步";
            historyBanner.text = selected != null && state.selectedEventIndex < history.Length - 1
                ? "历史回看 #" + selected.index + "：棋盘保持当前 WAIT " + HydrogenBattleText.N(snapshot.time) + "，不倒转战局。"
                : snapshot == null ? "尚未应用编排；两侧为空阵位。请应用并开战。" : "棋盘为当前状态；边框显示所选真实事件的行动者与全部覆盖者。";
            var player = state.workbench.draftScenario.playerTeam;
            var enemy = state.workbench.draftScenario.units.FirstOrDefault(unit => unit.team != player)?.team;
            FriendlyBoard.Render(snapshot, player, selected, state.highlightedTargetIds);
            EnemyBoard.Render(snapshot, enemy, selected, state.highlightedTargetIds);
            RefreshQueue();
            RefreshEvents();
            if (selected == null)
            {
                bridgeActor.text = "尚无实际行动";
                bridgeAction.text = "应用编排\n开始观察";
                bridgeCosts.text = "费用与 WAIT\n均由核心返回";
                bridgeTargets.text = "单步与自动共用同一战斗。";
            }
            else
            {
                bridgeActor.text = HydrogenUi.Esc(selected.actorName) + "\nWAIT " + HydrogenBattleText.N(selected.time);
                bridgeAction.text = HydrogenUi.Esc(selected.actionName ?? (selected.status == "Skipped" ? "行动受阻" : "状态事件"));
                bridgeCosts.text = "一次 " + selected.mpCost + " MP\n一次 " + HydrogenBattleText.N(selected.actualWait) + " WAIT\n覆盖 " + HighlightedTargetIds.Length + " 个目标";
                bridgeTargets.text = string.Join("\n", HighlightedTargetIds.Select(id => HydrogenBattleText.Name(snapshot, id)));
                if (!string.IsNullOrEmpty(selected.targetId)) bridgeTargets.text += "\n锚点：" + HydrogenBattleText.Name(snapshot, selected.targetId);
                bridgeArrow.text = snapshot?.units.FirstOrDefault(unit => unit.id == selected.actorId)?.team == player ? "→" : "←";
            }
            if (!string.IsNullOrEmpty(state.inspectedUnitId) && state.inspectedUnitId != lastInspectedUnitId) showUnitDetail = true;
            lastInspectedUnitId = state.inspectedUnitId;
            var unitDetail = snapshot?.units.FirstOrDefault(unit => unit.id == state.inspectedUnitId);
            string detail = showUnitDetail ? HydrogenBattleText.UnitDetail(unitDetail, technical) : HydrogenBattleText.Causality(selected, technical, StatusDisplay);
            // Replace diagnostic unit IDs with their real display names in the player-facing layer only.
            if (!technical && snapshot != null)
                foreach (var unit in snapshot.units.OrderByDescending(unit => unit.id.Length)) detail = detail.Replace(unit.id, unit.name);
            ReasonText.text = HydrogenUi.Esc(detail);
            string selectedReason = (selected == null ? "none" : selected.index + ":" + selected.actorId + ":" + selected.actionId)
                + "|" + showUnitDetail + "|" + technical + "|" + state.inspectedUnitId;
            if (reasonSignature != selectedReason)
            {
                reasonSignature = selectedReason;
                reasonScrollPending = true;
            }
            HydrogenUi.ButtonTitle(TechnicalFieldsButton, technical ? "收起技术字段" : "展开技术字段");
            TintTab(EventDetailsButton, !showUnitDetail);
            TintTab(UnitDetailsButton, showUnitDetail);
        }

        private static void TintPlaybackButton(Button button, bool active, string title)
        {
            button.GetComponent<Image>().color = active ? T.Gold : T.Elevated;
            button.GetComponent<Outline>().effectColor = active ? T.Gold : T.Border;
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = title;
            label.color = active ? T.Abyss : T.Ink;
            // The active state is intentionally non-clickable; don't fade away its indicator.
            var colors = button.colors;
            colors.disabledColor = active ? Color.white : new Color(.55f, .55f, .55f, .45f);
            button.colors = colors;
        }

        private void RefreshReasonViewport()
        {
            HydrogenUi.FitWholeTextLines(ReasonScroll, ReasonText);
            if (!reasonScrollPending) return;
            reasonScrollPending = false;
            ReasonScroll.StopMovement();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ReasonText.rectTransform);
            ReasonScroll.verticalNormalizedPosition = 1;
        }

        private void RefreshQueue()
        {
            var snapshot = state.battle;
            string signature = snapshot == null ? "none" : string.Join("|", snapshot.units.Select(unit => unit.id + ":" + unit.name + ":" + unit.hp + ":" + unit.dormant + ":" + unit.nextActionTime));
            if (signature == queueSignature) return;
            queueSignature = signature;
            HydrogenUi.Clear(queueContent);
            if (snapshot == null) return;
            var queue = snapshot.units.Where(unit => unit.hp > 0 && !unit.dormant).OrderBy(unit => unit.nextActionTime).Take(6).ToArray();
            for (int i = 0; i < queue.Length; i++)
            {
                var unit = queue[i];
                var pill = HydrogenUi.Panel("Hydrogen_Queue_" + unit.id, queueContent, T.Elevated);
                Ui.Width(pill.gameObject, 152);
                var text = HydrogenUi.Text("RealReadyTime", pill.transform, font, unit.name + "  W " + HydrogenBattleText.N(unit.nextActionTime)
                    + (queue.Count(other => other.nextActionTime == unit.nextActionTime) > 1 ? " #" + (i + 1) : ""), 13, T.Cyan, true);
                Ui.Stretch(text.rectTransform, 6, 0, 6, 0);
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        private void RefreshEvents()
        {
            var history = state.history ?? Array.Empty<CombatStepResult>();
            var signature = state.workbench.draftScenario.id + "|" + state.selectedEventIndex + "|" + reverseEvents + "|"
                + string.Join(";", history.Select(step => step.index + ":" + step.time + ":" + step.actorName + ":" + step.actionName + ":" + step.status));
            if (signature == eventSignature) return;
            eventSignature = signature;
            float scrollPosition = eventScroll.verticalNormalizedPosition;
            HydrogenUi.Clear(eventList);
            eventButtons.Clear();
            var indices = Enumerable.Range(0, history.Length);
            if (reverseEvents) indices = indices.Reverse();
            foreach (var index in indices)
            {
                int selectedIndex = index;
                var step = history[index];
                var button = HydrogenUi.Button("Hydrogen_Event_" + index, eventList, font,
                    "#" + step.index + " W" + HydrogenBattleText.N(step.time) + "  " + step.actorName + " → "
                    + (string.IsNullOrEmpty(step.actionName) ? step.status == "Skipped" ? "行动受阻" : "状态到期" : step.actionName),
                    () => { showUnitDetail = false; reasonScrollPending = true; shared.SelectEvent(selectedIndex); }, 0, false, 32);
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                if (index == state.selectedEventIndex) button.GetComponent<Outline>().effectColor = T.Gold;
                eventButtons.Add(button);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(eventList);
            if (history.Length > previousEventCount && state.selectedEventIndex == history.Length - 1)
                eventScroll.verticalNormalizedPosition = reverseEvents ? 1 : 0;
            else eventScroll.verticalNormalizedPosition = scrollPosition;
            previousEventCount = history.Length;
        }

        private static string Outcome(string outcome)
        {
            switch (outcome) { case "Victory": return "战斗结束 · 胜利"; case "Defeat": return "战斗结束 · 败北"; case "Stalemate": return "战斗结束 · 无可执行行动"; case "Stopped": return "已达停止边界"; default: return "战斗结束 · " + outcome; }
        }

        private string StatusDisplay(string statusId)
        {
            var scenario = state.workbench.committedScenario ?? state.workbench.draftScenario;
            var status = scenario.units.SelectMany(unit => unit.statuses ?? Array.Empty<CombatStatusConfig>()).FirstOrDefault(value => value.id == statusId);
            if (status != null) return HydrogenBattleText.StatusName(status.kind);
            var action = scenario.actions.FirstOrDefault(value => value.statusId == statusId);
            return action == null ? "状态" : HydrogenBattleText.StatusName(action.statusKind);
        }
    }
}
