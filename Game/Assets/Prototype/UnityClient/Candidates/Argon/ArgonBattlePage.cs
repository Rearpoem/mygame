using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Prototype.Combat;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Prototype.UnityClient.Candidates.Argon
{
    public sealed class ArgonBattlePage : MonoBehaviour
    {
        private sealed class Cell
        {
            internal Button button;
            internal TMP_Text name;
            internal TMP_Text coordinate;
            internal TMP_Text hp;
            internal TMP_Text mp;
            internal TMP_Text status;
            internal Image portrait;
            internal Image hpFill;
            internal Image mpFill;
            internal string unitId;
            internal int row;
            internal int column;
            internal bool friendly;
        }

        private TMP_FontAsset font;
        private TacticsSharedController shared;
        private readonly List<Cell> cells = new List<Cell>();
        private readonly List<Button> eventButtons = new List<Button>();
        private RectTransform eventContent;
        private ScrollRect eventScroll;
        private int lastSelectedEvent = -2;
        private int pendingVisibleEvent = -1;
        private int eventScrollPendingFrames;
        private TMP_Text historyNotice;
        private TMP_Text eventSummary;
        private TMP_Text inspector;
        private TMP_Text stateText;
        private TMP_Text queueText;
        private TMP_Text friendlyTitle;
        private TMP_Text hostileTitle;
        private Button detailsButton;
        private Button inspectorTechnicalButton;
        private bool refreshing;
        private bool detailsExpanded;
        private bool inspectorTechnicalExpanded;
        private string inspectedUnitId;
        private string historySignature;

        public Button StepButton { get; private set; }
        public Button AutoButton { get; private set; }
        public Button PauseButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public Button LatestEventButton { get; private set; }
        public Button DetailsButton { get { return detailsButton; } }
        public Button InspectorTechnicalButton { get { return inspectorTechnicalButton; } }
        public ScrollRect EventScroll { get { return eventScroll; } }
        public TMP_InputField IntervalInput { get; private set; }
        public TMP_Text StateText { get { return stateText; } }
        public TMP_Text QueueText { get { return queueText; } }
        public TMP_Text ReasonText { get { return eventSummary; } }
        public TMP_Text InspectorText { get { return inspector; } }
        public TMP_Text HistoryNotice { get { return historyNotice; } }
        public IReadOnlyList<Button> EventButtons { get { return eventButtons.AsReadOnly(); } }
        public IReadOnlyList<Button> UnitButtons { get { return cells.Select(cell => cell.button).ToArray(); } }

        public void Initialize(TMP_FontAsset value, TacticsSharedController controller)
        {
            font = value;
            shared = controller;
            var root = ArgonUi.Column("ArgonBattleLayers", transform, 8);
            ArgonUi.Fill(root);
            BuildTimeline(root);
            BuildBoards(root);
            BuildInspection(root);
        }

        private void BuildTimeline(Transform parent)
        {
            var content = ArgonUi.Frame("Argon_WaitStarTrack", parent, ArgonTheme.Brass);
            ArgonUi.Size(content.parent.gameObject, -1, 76, 1);
            var rows = ArgonUi.Column("TrackRows", content, 3);
            ArgonUi.Fill(rows);
            var top = ArgonUi.Row("PlaybackControls", rows, 6);
            ArgonUi.Size(top.gameObject, -1, 31, 1);
            AutoButton = ArgonUi.Button("Argon_Auto", top, font, "自动播放", () => shared.StartAuto(), 100, 31);
            PauseButton = ArgonUi.Button("Argon_Pause", top, font, "暂停", () => shared.Pause(), 70, 31);
            StepButton = ArgonUi.Button("Argon_Step", top, font, "单步推进", () => shared.Step(), 100, 31, true);
            ReplayButton = ArgonUi.Button("Argon_BattleReplay", top, font, "同开局重放", () => shared.Replay(), 116, 31);
            var label = ArgonUi.Text("IntervalLabel", top, font, "间隔 秒", 12, ArgonTheme.Muted);
            ArgonUi.Size(label.gameObject, 52, 31);
            IntervalInput = ArgonUi.Input("Argon_Interval", top, font, "0.45", 62, 31);
            IntervalInput.onEndEdit.AddListener(text =>
            {
                if (refreshing) return;
                double interval;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out interval))
                    shared.SetPlaybackInterval(interval);
                else Refresh(shared.GetState());
            });
            stateText = ArgonUi.Text("Argon_PlaybackState", top, font, "尚未应用编排", 14, ArgonTheme.Wait, true);
            stateText.alignment = TextAlignmentOptions.MidlineRight;
            ArgonUi.Size(stateText.gameObject, -1, 31, 1);
            queueText = ArgonUi.Text("Argon_WaitQueue", rows, font, "", 14, ArgonTheme.Secondary);
            queueText.textWrappingMode = TextWrappingModes.NoWrap;
            queueText.overflowMode = TextOverflowModes.Ellipsis;
            ArgonUi.Size(queueText.gameObject, -1, 23, 1);
        }

        private void BuildBoards(Transform parent)
        {
            var content = ArgonUi.Frame("Argon_OpposingFormation", parent);
            ArgonUi.Size(content.parent.gameObject, -1, 372, 1);
            var row = ArgonUi.Row("OpposingSides", content, 8);
            ArgonUi.Fill(row);
            BuildBoard(row, true);
            var frontLine = ArgonUi.Text("FrontLine", row, font, "前\n线\n交\n锋", 14, ArgonTheme.Brass, true);
            frontLine.alignment = TextAlignmentOptions.Center;
            ArgonUi.Size(frontLine.gameObject, 28, -1, 0, 1);
            BuildBoard(row, false);
        }

        private void BuildBoard(Transform parent, bool friendly)
        {
            var root = ArgonUi.Column(friendly ? "FriendlyFormation" : "HostileFormation", parent, 5);
            ArgonUi.Size(root.gameObject, -1, -1, 1, 1);
            var title = ArgonUi.Text("SideTitle", root, font, friendly ? "己方阵营 · 3×3" : "敌方阵营 · 3×3", 16,
                friendly ? ArgonTheme.Ally : ArgonTheme.Enemy, true);
            if (friendly) friendlyTitle = title; else hostileTitle = title;
            title.alignment = TextAlignmentOptions.Center;
            ArgonUi.Size(title.gameObject, -1, 25, 1);
            var labels = ArgonUi.Row("RankLabels", root, 6);
            ArgonUi.Size(labels.gameObject, -1, 19, 1);
            for (var x = 0; x < 3; x++)
            {
                var logicalRow = friendly ? 2 - x : x;
                var label = ArgonUi.Text("Rank_" + logicalRow, labels, font,
                    Rank(logicalRow) + " Row " + logicalRow, 12, ArgonTheme.Muted);
                label.alignment = TextAlignmentOptions.Center;
                ArgonUi.Size(label.gameObject, -1, 19, 1);
            }
            for (var column = 0; column < 3; column++)
            {
                var line = ArgonUi.Row("Column_" + column, root, 6);
                ArgonUi.Size(line.gameObject, -1, -1, 1, 1);
                for (var x = 0; x < 3; x++) BuildCell(line, friendly, friendly ? 2 - x : x, column);
            }
        }

        private void BuildCell(Transform parent, bool friendly, int row, int column)
        {
            var cell = new Cell { friendly = friendly, row = row, column = column };
            cell.button = ArgonUi.Button("Argon_Cell_" + (friendly ? "F_" : "E_") + row + "_" + column,
                parent, font, "", null, -1, -1);
            ArgonUi.Size(cell.button.gameObject, -1, -1, 1, 1);
            UnityEngine.Object.Destroy(cell.button.transform.Find("Label").gameObject);
            cell.button.onClick.AddListener(() =>
            {
                if (string.IsNullOrEmpty(cell.unitId)) return;
                inspectedUnitId = cell.unitId;
                shared.InspectUnit(cell.unitId);
                Refresh(shared.GetState());
            });
            cell.portrait = ArgonUi.Image("Portrait", cell.button.transform, Color.white);
            cell.portrait.preserveAspect = true;
            ArgonUi.Place(cell.portrait.rectTransform, 4, 3, 31, 34);
            cell.name = ArgonUi.Text("UnitName", cell.button.transform, font, "", 14, ArgonTheme.Ink, true);
            TopRect(cell.name.rectTransform, 37, 1, 3, 19);
            cell.coordinate = ArgonUi.Text("CoordinateAndWait", cell.button.transform, font, "", 12, ArgonTheme.Muted);
            TopRect(cell.coordinate.rectTransform, 37, 21, 3, 17);
            cell.hp = Bar("HP", cell.button.transform, 39, ArgonTheme.Ally, out cell.hpFill);
            cell.mp = Bar("MP", cell.button.transform, 58, ArgonTheme.Mp, out cell.mpFill);
            cell.status = ArgonUi.Text("StatusesAndHighlight", cell.button.transform, font, "", 12, ArgonTheme.Secondary);
            TopRect(cell.status.rectTransform, 3, 77, 3, 19);
            // This is a 19px single-line badge, not paragraph text. Noto's line metrics
            // cannot fit the generic 2px top/bottom paragraph inset before ellipsis.
            // Preserve the authored 12px badge and its full card footprint.
            cell.status.margin = new Vector4(3, 0, 3, 0);
            cell.status.textWrappingMode = TextWrappingModes.NoWrap;
            cell.status.overflowMode = TextOverflowModes.Ellipsis;
            cells.Add(cell);
        }

        private TMP_Text Bar(string name, Transform parent, float top, Color color, out Image fill)
        {
            var bg = ArgonUi.Image(name + "Track", parent, ArgonTheme.Well);
            TopRect(bg.rectTransform, 4, top, 4, 17);
            fill = ArgonUi.Image(name + "Fill", bg.transform, new Color(color.r, color.g, color.b, .43f));
            ArgonUi.Fill(fill.rectTransform);
            var text = ArgonUi.Text(name + "Value", bg.transform, font, "", 14, ArgonTheme.Ink, true);
            ArgonUi.Fill(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private static void TopRect(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void BuildInspection(Transform parent)
        {
            var row = ArgonUi.Row("Argon_CausalInspection", parent, 10);
            ArgonUi.Size(row.gameObject, -1, 260, 1, 1);
            var events = ArgonUi.Frame("HistoryRail", row);
            ArgonUi.Size(events.parent.gameObject, -1, -1, .21f, 1);
            var eventColumn = ArgonUi.Column("HistoryLayout", events, 5);
            ArgonUi.Fill(eventColumn);
            var heading = ArgonUi.Text("Heading", eventColumn, font, "事件流 · 点击回看", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(heading.gameObject, -1, 25, 1);
            eventContent = ArgonUi.Scroll("Argon_Events", eventColumn, out eventScroll, true, 2, 4);
            LatestEventButton = ArgonUi.Button("Argon_LatestEvent", eventColumn, font, "回到最新事件", () =>
            {
                shared.SetFollowLatest(true);
            }, -1, 28);
            var cause = ArgonUi.Frame("CausalDissection", row, ArgonTheme.Brass);
            ArgonUi.Size(cause.parent.gameObject, -1, -1, .47f, 1);
            var causeColumn = ArgonUi.Column("CauseLayout", cause, 3);
            ArgonUi.Fill(causeColumn);
            historyNotice = ArgonUi.Text("Argon_HistoryNotice", causeColumn, font, "", 12, ArgonTheme.Warning);
            ArgonUi.Size(historyNotice.gameObject, -1, 34, 1);
            detailsButton = ArgonUi.Button("Argon_ToggleRuleDetails", causeColumn, font, "展开逐条判定与候选真值", () =>
            {
                detailsExpanded = !detailsExpanded;
                Refresh(shared.GetState());
            }, -1, 28);
            ScrollRect reasonScroll;
            eventSummary = ArgonUi.Readout("Argon_EventCausality", causeColumn, font, out reasonScroll);
            var inspect = ArgonUi.Frame("UnitInspector", row);
            ArgonUi.Size(inspect.parent.gameObject, -1, -1, .32f, 1);
            var inspectColumn = ArgonUi.Column("InspectorLayout", inspect, 5);
            ArgonUi.Fill(inspectColumn);
            var inspectorHeading = ArgonUi.Row("InspectorHeading", inspectColumn, 4);
            ArgonUi.Size(inspectorHeading.gameObject, -1, 25, 1);
            var title = ArgonUi.Text("Heading", inspectorHeading, font, "单位状态检查器", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(title.gameObject, -1, 25, 1);
            inspectorTechnicalButton = ArgonUi.Button("Argon_InspectorTechnicalFields", inspectorHeading, font, "技术字段", () =>
            {
                inspectorTechnicalExpanded = !inspectorTechnicalExpanded;
                Refresh(shared.GetState());
            }, 116, 25);
            ScrollRect inspectorScroll;
            inspector = ArgonUi.Readout("Argon_UnitInspection", inspectColumn, font, out inspectorScroll);
        }

        public void Refresh(TacticsCandidateState state)
        {
            if (state == null || shared == null) return;
            refreshing = true;
            try
            {
                var snapshot = state.battle ?? (state.workbench == null ? null : state.workbench.draftPreview);
                var isPreview = state.battle == null;
                var canPlay = state.battle != null && !state.battle.finished && !state.battle.blocked;
                AutoButton.interactable = canPlay && !state.isAutoPlaying;
                PauseButton.interactable = state.isAutoPlaying;
                StepButton.interactable = canPlay;
                ReplayButton.interactable = state.battle != null;
                IntervalInput.SetTextWithoutNotify(Number(state.intervalSeconds));
                stateText.text = isPreview ? "尚未开战 · 初始站位预览" :
                    (state.battle.finished ? Outcome(state.battle.outcome) : state.battle.blocked ? "阻塞：" + state.battle.blockReason : state.isAutoPlaying ? "自动播放" : "已暂停 · 后台不推进") +
                    "  WAIT " + Number(state.battle.time);
                stateText.color = isPreview ? ArgonTheme.Muted : state.battle.finished ? ArgonTheme.Ally : state.isAutoPlaying ? ArgonTheme.Wait : ArgonTheme.Warning;
                queueText.text = isPreview ? "待命预览：点击「应用并开战」提交草稿；此处没有推进战斗。" : state.queueText;
                RefreshCells(state, snapshot, isPreview);
                RefreshHistory(state);
                RefreshCausality(state, snapshot);
                inspectedUnitId = state.inspectedUnitId;
                RefreshInspector(state, snapshot, isPreview);
            }
            finally { refreshing = false; }
        }

        private void RefreshCells(TacticsCandidateState state, CombatSnapshot snapshot, bool isPreview)
        {
            var units = snapshot == null || snapshot.units == null ? new CombatUnitSnapshot[0] : snapshot.units;
            var scenario = state.workbench == null ? null : state.workbench.draftScenario;
            var friendlyTeam = scenario == null ? "friendly" : scenario.playerTeam;
            var covered = new HashSet<string>(state.highlightedTargetIds ?? new string[0]);
            var step = SelectedStep(state);
            // Bars remain current snapshot values. Parentheses are the actual
            // selected event's effects, including when an older event is inspected.
            var changeLegend = step == null || isPreview ? "" : "｜当前值（所选事件变化）";
            friendlyTitle.text = "己方 · 3×3" + changeLegend;
            hostileTitle.text = "敌方 · 3×3" + changeLegend;
            foreach (var cell in cells)
            {
                var unit = units.FirstOrDefault(candidate => (candidate.team == friendlyTeam) == cell.friendly &&
                    candidate.row == cell.row && candidate.column == cell.column);
                cell.unitId = unit == null ? null : unit.id;
                cell.button.interactable = unit != null;
                cell.hp.transform.parent.gameObject.SetActive(unit != null);
                cell.mp.transform.parent.gameObject.SetActive(unit != null);
                cell.portrait.gameObject.SetActive(unit != null && shared.GetPortrait(unit.id) != null);
                if (unit == null)
                {
                    cell.name.text = "空位";
                    cell.coordinate.text = Rank(cell.row) + " (" + cell.row + "," + cell.column + ")";
                    cell.hp.text = cell.mp.text = cell.status.text = "";
                    cell.hpFill.color = cell.mpFill.color = Color.clear;
                    cell.button.GetComponent<Image>().color = ArgonTheme.Well;
                    cell.button.GetComponent<Outline>().effectColor = ArgonTheme.Border;
                    continue;
                }
                cell.portrait.sprite = shared.GetPortrait(unit.id);
                var actor = !isPreview && state.highlightedActorId == unit.id;
                var anchor = !isPreview && step != null && step.targetId == unit.id;
                var cover = !isPreview && covered.Contains(unit.id);
                var extra = !isPreview && step != null && !cover && (step.effects ?? new CombatEffect[0]).Any(effect => effect.targetId == unit.id && effect.mpDelta > 0);
                cell.name.text = unit.name + (actor ? " · 行动" : "") + (anchor ? " · 锚" : "");
                cell.coordinate.text = Rank(unit.row) + " (" + unit.row + "," + unit.column + ") · W" + Number(unit.nextActionTime);
                cell.coordinate.color = anchor ? (cell.friendly ? ArgonTheme.Ally : ArgonTheme.Enemy) : ArgonTheme.Muted;
                var effects = isPreview || step == null ? new CombatEffect[0] :
                    (step.effects ?? new CombatEffect[0]).Where(effect => effect.targetId == unit.id).ToArray();
                cell.hp.text = "HP " + unit.hp + " / " + unit.maxHp + Delta(effects.Sum(effect => effect.hpDelta));
                cell.mp.text = "MP " + unit.mp + " / " + unit.maxMp + Delta(effects.Sum(effect => effect.mpDelta));
                SetBar(cell.hpFill, unit.hp, unit.maxHp, cell.friendly ? ArgonTheme.Ally : ArgonTheme.Enemy);
                SetBar(cell.mpFill, unit.mp, unit.maxMp, ArgonTheme.Mp);
                cell.status.text = unit.hp <= 0 ? "已阵亡" : extra ? "附加回蓝 · 非主覆盖" :
                    (cover ? "[覆盖] " : "") + (unit.statuses != null && unit.statuses.Length > 0 ? string.Join(" · ", unit.statuses.Select(status => StatusLabel(status.kind) + "→" + Number(status.expiresAt))) : "状态：无");
                cell.status.color = extra ? ArgonTheme.Focus : unit.hp <= 0 ? ArgonTheme.Muted : cover ? (cell.friendly ? ArgonTheme.Ally : ArgonTheme.Cover) : ArgonTheme.Secondary;
                cell.button.GetComponent<Image>().color = unit.hp <= 0 ? ArgonTheme.Well : isPreview ? ArgonTheme.Panel : ArgonTheme.Card;
                cell.button.GetComponent<Outline>().effectColor = actor ? ArgonTheme.Actor : anchor ? (cell.friendly ? ArgonTheme.Ally : ArgonTheme.Enemy) : cover ? (cell.friendly ? ArgonTheme.Ally : ArgonTheme.Cover) : extra ? ArgonTheme.Focus : ArgonTheme.Border;
                cell.button.GetComponent<Outline>().effectDistance = new Vector2(actor || cover ? 2 : 1, actor || cover ? -2 : -1);
            }
        }

        private void RefreshHistory(TacticsCandidateState state)
        {
            var history = state.history ?? new CombatStepResult[0];
            var signature = history.Length + ":" + (history.Length == 0 ? "" : history[history.Length - 1].index + ":" + history[history.Length - 1].actionId);
            var rebuilt = signature != historySignature;
            if (rebuilt)
            {
                historySignature = signature;
                ArgonUi.Clear(eventContent);
                eventButtons.Clear();
                for (var i = 0; i < history.Length; i++)
                {
                    var index = i;
                    var step = history[i];
                    var label = "#" + step.index + "  W" + Number(step.time) + "  " + step.actorName + "\n" + (string.IsNullOrEmpty(step.actionName) ? step.status : step.actionName);
                    var button = ArgonUi.Button("Argon_Event_" + i, eventContent, font, label, () => shared.SelectEvent(index), -1, 46);
                    var text = button.GetComponentInChildren<TMP_Text>();
                    text.alignment = TextAlignmentOptions.MidlineLeft;
                    text.textWrappingMode = TextWrappingModes.Normal;
                    eventButtons.Add(button);
                }
                if (history.Length == 0)
                {
                    var empty = ArgonUi.Text("EmptyHistory", eventContent, font, "尚无事件\n应用开战后，单步或自动播放。", 14, ArgonTheme.Muted);
                    ArgonUi.Size(empty.gameObject, -1, 76, 1);
                }
            }
            for (var i = 0; i < eventButtons.Count; i++) ArgonUi.Selected(eventButtons[i], i == state.selectedEventIndex);
            if (rebuilt || lastSelectedEvent != state.selectedEventIndex)
            {
                lastSelectedEvent = state.selectedEventIndex;
                pendingVisibleEvent = state.selectedEventIndex;
                eventScrollPendingFrames = 1;
            }
            LatestEventButton.interactable = history.Length > 0 && (!state.followLatest || state.selectedEventIndex != history.Length - 1);
            LatestEventButton.GetComponentInChildren<TMP_Text>().text = state.followLatest ? "正在跟随最新事件" : "回到最新事件";
        }

        private void LateUpdate()
        {
            if (pendingVisibleEvent < 0 || !gameObject.activeInHierarchy || eventScroll == null ||
                CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            // Event rows have just been built. Position their scroll only after a
            // real layout frame; no shared selection, battle or playback is changed.
            if (eventScrollPendingFrames-- > 0) return;
            var index = pendingVisibleEvent;
            pendingVisibleEvent = -1;
            if (index >= eventButtons.Count) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(eventContent);
            eventScroll.StopMovement();
            if (index == eventButtons.Count - 1)
            {
                eventScroll.verticalNormalizedPosition = 0;
                return;
            }
            var target = eventButtons[index].GetComponent<RectTransform>();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(eventScroll.viewport, target);
            var viewport = eventScroll.viewport.rect;
            var shift = bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y :
                bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y : 0;
            eventContent.anchoredPosition += new Vector2(0, shift);
        }

        private void RefreshCausality(TacticsCandidateState state, CombatSnapshot snapshot)
        {
            var step = SelectedStep(state);
            detailsButton.GetComponentInChildren<TMP_Text>().text = detailsExpanded ? "收起逐条判定与候选真值" : "展开逐条判定与候选真值";
            if (step == null)
            {
                historyNotice.text = "事件解释与当前棋盘分别展示。";
                eventSummary.text = "尚无实际战斗事件。\n\n这里将显示命中规则、主选锚点、全部实际覆盖者、单次MP费用与WAIT，以及每条跳过原因。";
                return;
            }
            var history = state.history ?? new CombatStepResult[0];
            var old = state.selectedEventIndex >= 0 && state.selectedEventIndex < history.Length - 1;
            historyNotice.text = old ? "回看历史 #" + step.index + "（W" + Number(step.time) + "）；上方仍是当前 W" + Number(snapshot == null ? step.time : snapshot.time) + " 的棋盘，不倒转战局。" :
                "最新实际事件 #" + step.index + " · 棋盘显示当前状态";
            var builder = new StringBuilder();
            builder.AppendLine("<b>" + step.actorName + " · " + (step.actionName ?? step.status) + "</b>");
            var selected = (step.attempts ?? new CombatInstructionAttempt[0]).LastOrDefault(attempt => string.Equals(attempt.outcome, "Selected", StringComparison.OrdinalIgnoreCase));
            if (selected != null) builder.AppendLine("命中规则 #" + (selected.instructionIndex + 1) + "：" + InstructionLabel(state, step.actorId, selected.instructionIndex));
            var skipped = (step.attempts ?? new CombatInstructionAttempt[0]).Where(attempt => !string.Equals(attempt.outcome, "Selected", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (skipped.Length > 0)
            {
                var first = skipped[0];
                var candidate = (first.candidates ?? new CombatCandidateTrace[0]).FirstOrDefault(item => !item.passesCondition || !item.eligible);
                var reason = candidate != null && !string.IsNullOrEmpty(candidate.reason) ? candidate.reason : first.reason;
                builder.AppendLine("<color=#FBBF24>前序跳过 " + skipped.Length + " 条：#" + (first.instructionIndex + 1) + " " +
                    InstructionLabel(state, step.actorId, first.instructionIndex) + " · " + Brief(reason, 58) + "</color>");
            }
            builder.AppendLine("主选：" + (string.IsNullOrEmpty(step.targetName) ? "无" : step.targetName) + "；实际覆盖：" +
                string.Join("、", (step.targetIds ?? new string[0]).Select(id => UnitName(state, snapshot, step, id))));
            var effects = step.effects ?? new CombatEffect[0];
            var actorMpInEffects = effects.Any(effect => effect.targetId == step.actorId && effect.mpDelta != 0 && effect.mpBefore == step.mpBefore && effect.mpAfter == step.mpAfter);
            builder.AppendLine(actorMpInEffects ? "单次费用：" + step.mpCost + " MP" : "单次MP：" + step.mpBefore + " → " + step.mpAfter + "（费用 " + step.mpCost + "）");
            builder.AppendLine("WAIT：" + Number(step.actualWait) + "（基础 " + Number(step.baseWait) + " × 敏捷 " + Number(step.agilityFactor) + "）；下次 W" + Number(step.nextActionTime));
            if (!string.IsNullOrEmpty(step.controlReason)) builder.AppendLine(step.controlReason);
            foreach (var effect in effects)
            {
                builder.AppendLine("• " + effect.description);
                if (!string.IsNullOrEmpty(effect.statusId)) builder.AppendLine("  状态生效 W" + Number(effect.statusAppliedAt) + " → 到期 W" + Number(effect.statusExpiresAt));
                if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0) builder.AppendLine("  已移除 " + effect.removedStatusIds.Length + " 项状态");
            }
            if (detailsExpanded)
            {
                builder.AppendLine("\n<b>逐条判定 · 候选对象真值</b>");
                foreach (var attempt in step.attempts ?? new CombatInstructionAttempt[0])
                {
                    builder.AppendLine("#" + (attempt.instructionIndex + 1) + " " + attempt.display + "\n[" + attempt.outcome + "] " + attempt.reason);
                    foreach (var candidate in attempt.candidates ?? new CombatCandidateTrace[0])
                        builder.AppendLine("  候选 " + candidate.unitName + "：条件 " + (candidate.passesCondition ? "真" : "假") + " / 合法 " + (candidate.eligible ? "是" : "否") + "；" + candidate.reason +
                            (candidate.affectedTargetIds == null || candidate.affectedTargetIds.Length == 0 ? "" : "；覆盖 " + string.Join("、", candidate.affectedTargetIds.Select(id => UnitName(state, snapshot, step, id)))));
                }
                if (step.timeTieResolved) builder.AppendLine("同刻裁决：" + step.tieBreakReason);
                builder.AppendLine("基础敏捷系数 " + Number(step.baseAgilityFactor) + " · 状态WAIT倍率 " + Number(step.statusWaitFactor));
                builder.AppendLine("\n<color=#7E8FA6>技术字段 · 行动ID：" + step.actionId + "\n覆盖ID：" + string.Join("、", step.targetIds ?? new string[0]));
                foreach (var effect in effects)
                {
                    if (!string.IsNullOrEmpty(effect.statusId)) builder.AppendLine("状态ID：" + effect.statusId);
                    if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0) builder.AppendLine("移除状态ID：" + string.Join("、", effect.removedStatusIds));
                }
                builder.AppendLine("</color>");
            }
            eventSummary.text = builder.ToString();
        }

        private void RefreshInspector(TacticsCandidateState state, CombatSnapshot snapshot, bool preview)
        {
            var units = snapshot == null || snapshot.units == null ? new CombatUnitSnapshot[0] : snapshot.units;
            var unit = units.FirstOrDefault(candidate => candidate.id == inspectedUnitId) ?? units.FirstOrDefault();
            if (unit == null) { inspector.text = "点击阵位中的单位查看完整状态。"; return; }
            var builder = new StringBuilder();
            inspectorTechnicalButton.GetComponentInChildren<TMP_Text>().text = inspectorTechnicalExpanded ? "收起技术字段" : "技术字段";
            builder.AppendLine("<b>" + unit.name + "</b>");
            builder.AppendLine((preview ? "初始预览 · " : "当前状态 · ") + Rank(unit.row) + " (" + unit.row + "," + unit.column + ")");
            builder.AppendLine("HP " + unit.hp + "/" + unit.maxHp + "    MP " + unit.mp + "/" + unit.maxMp);
            builder.AppendLine("下次行动 W" + Number(unit.nextActionTime) + "    敏捷系数 " + Number(unit.effectiveAgilityFactor));
            builder.AppendLine("INT " + unit.intelligence + " · " + unit.instructionCapacity + " 槽 / 每条 " + unit.conditionTermCapacity + " 条件");
            builder.AppendLine("标签：" + (unit.tags == null ? 0 : unit.tags.Length) + " 项");
            builder.AppendLine("基础易伤：" + (unit.baseVulnerable ? "是" : "否") + " · 有效易伤：" + (unit.vulnerable ? "是" : "否"));
            builder.AppendLine("装备：" + (string.IsNullOrEmpty(unit.equipmentSourceId) ? "无来源记录" : "已装载") + " · 攻击加成 " + unit.equipmentDamageBonus);
            builder.AppendLine("\n<b>当前激活状态</b>");
            foreach (var status in unit.statuses ?? new CombatStatusConfig[0])
                builder.AppendLine(StatusLabel(status.kind) + " 量 " + status.magnitude + "\n施加 W" + Number(status.appliedAt) + " → 到期 W" + Number(status.expiresAt));
            if (unit.statuses == null || unit.statuses.Length == 0) builder.AppendLine("无");
            builder.AppendLine("\n<b>已装载指令</b>（实时判定请查看实际事件）");
            foreach (var instruction in unit.instructions ?? new CombatInstructionSnapshot[0])
                builder.AppendLine(instruction.display + (string.IsNullOrEmpty(instruction.validationReason) ? "" : "\n" + instruction.validationReason));
            if (inspectorTechnicalExpanded)
            {
                builder.AppendLine("\n<color=#7E8FA6><b>技术字段</b>\n单位ID：" + unit.id);
                builder.AppendLine("标签ID：" + string.Join("、", unit.tags ?? new string[0]));
                builder.AppendLine("装备来源ID：" + (unit.equipmentSourceId ?? "无"));
                foreach (var status in unit.statuses ?? new CombatStatusConfig[0]) builder.AppendLine("状态来源ID：" + status.id);
                builder.AppendLine("</color>");
            }
            inspector.text = builder.ToString();
        }

        private static string UnitName(TacticsCandidateState state, CombatSnapshot snapshot, CombatStepResult step, string id)
        {
            var unit = (snapshot == null ? new CombatUnitSnapshot[0] : snapshot.units ?? new CombatUnitSnapshot[0]).FirstOrDefault(item => item.id == id);
            if (unit != null && !string.IsNullOrEmpty(unit.name)) return unit.name;
            var committed = state.workbench == null ? null : state.workbench.committedScenario;
            var config = committed == null ? null : (committed.units ?? new CombatUnitConfig[0]).FirstOrDefault(item => item.id == id);
            if (config != null && !string.IsNullOrEmpty(config.name)) return config.name;
            var effect = (step.effects ?? new CombatEffect[0]).FirstOrDefault(item => item.targetId == id && !string.IsNullOrEmpty(item.targetName));
            return effect == null ? "未命名单位" : effect.targetName;
        }

        private static string InstructionLabel(TacticsCandidateState state, string actorId, int index)
        {
            var committed = state.workbench == null ? null : state.workbench.committedScenario;
            var unit = committed == null ? null : (committed.units ?? new CombatUnitConfig[0]).FirstOrDefault(item => item.id == actorId);
            return unit != null && unit.instructions != null && index >= 0 && index < unit.instructions.Length && !string.IsNullOrEmpty(unit.instructions[index].label)
                ? unit.instructions[index].label : "已装载规则";
        }

        private static string Brief(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "未提供原因；展开查看完整判定。";
            return value.Length > maximum ? value.Substring(0, maximum) + "…（展开详查）" : value;
        }

        private static string Delta(int value) { return value == 0 ? "" : " (" + (value > 0 ? "+" : "") + value + ")"; }

        private static CombatStepResult SelectedStep(TacticsCandidateState state)
        {
            return state.history != null && state.selectedEventIndex >= 0 && state.selectedEventIndex < state.history.Length ? state.history[state.selectedEventIndex] : null;
        }

        private static void SetBar(Image image, int value, int maximum, Color color)
        {
            image.color = new Color(color.r, color.g, color.b, .43f);
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = new Vector2(maximum <= 0 ? 0 : Mathf.Clamp01((float)value / maximum), 1);
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        }

        internal static string Number(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        internal static string Rank(int row) { return row == 0 ? "前排" : row == 1 ? "中排" : "后排"; }
        private static string Outcome(string value) { return value == "Victory" ? "战斗胜利" : value == "Defeat" ? "战斗失败" : value; }
        internal static string StatusLabel(string kind)
        {
            var option = CombatTacticsCatalog.GetStatusKinds().FirstOrDefault(item => item.id == kind);
            return option == null ? kind : option.label;
        }
    }
}
