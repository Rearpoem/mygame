using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    /// <summary>Live playback only. Each visible event comes from the bound workbench Step callback.</summary>
    public sealed class TacticsBattleView : MonoBehaviour
    {
        private sealed class Cell
        {
            internal Image image;
            internal Outline outline;
            internal TMP_Text label;
            internal Button button;
            internal string unitId;
            internal string firstLineSource;
            internal string firstLineText;
            internal float firstLineWidth = -1;
        }

        private TMP_FontAsset font;
        private Func<CombatSnapshot> readSnapshot;
        private Func<CombatStepResult[]> readHistory;
        private Func<CombatStepResult> advance;
        private Action replay;
        private CombatSnapshot snapshot;
        private CombatStepResult[] history = Array.Empty<CombatStepResult>();
        private readonly List<string> highlightedTargetIds = new List<string>();
        private Cell[] friendlyCells;
        private Cell[] enemyCells;
        private ScrollRect reasonsScroll;
        private bool initialized;
        private int listedEventCount = -1;
        private double elapsed;
        private double interval = .45;
        private float lastCardWidth = -1;

        public Button StepButton { get; private set; }
        public Button AutoButton { get; private set; }
        public Button PauseButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public TMP_InputField IntervalInput { get; private set; }
        public TMP_Dropdown EventDropdown { get; private set; }
        public TMP_Text StateText { get; private set; }
        public TMP_Text QueueText { get; private set; }
        public TMP_Text ReasonText { get; private set; }
        public TMP_Text FeedbackText { get; private set; }
        public bool IsAutoPlaying { get; private set; }
        public bool ExternalPlaybackClock { get; set; }
        public bool FollowLatest { get; private set; } = true;
        public int SelectedEventIndex { get; private set; } = -1;
        public string HighlightedActorId { get; private set; } = string.Empty;
        public string HighlightedTargetId { get; private set; } = string.Empty;
        public IReadOnlyList<string> HighlightedTargetIds { get { return highlightedTargetIds.AsReadOnly(); } }
        public string SelectedEffectsSummary { get; private set; } = string.Empty;
        public string LastError { get; private set; }
        public string FriendlyTeam { get; set; } = "friendly";
        public Button[] FriendlyCellButtons { get { return friendlyCells == null ? Array.Empty<Button>() : friendlyCells.Select(cell => cell.button).ToArray(); } }
        public Button[] EnemyCellButtons { get { return enemyCells == null ? Array.Empty<Button>() : enemyCells.Select(cell => cell.button).ToArray(); } }
        public TMP_Text[] FriendlyCellTexts { get { return friendlyCells == null ? Array.Empty<TMP_Text>() : friendlyCells.Select(cell => cell.label).ToArray(); } }
        public TMP_Text[] EnemyCellTexts { get { return enemyCells == null ? Array.Empty<TMP_Text>() : enemyCells.Select(cell => cell.label).ToArray(); } }
        public event Action PlaybackChanged;

        public double PlaybackIntervalSeconds
        {
            get { return interval; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < .05 || value > 5)
                    throw new ArgumentOutOfRangeException(nameof(value), "播放间隔为 0.05–5 秒。");
                interval = value;
                if (IntervalInput != null) IntervalInput.SetTextWithoutNotify(Number(value));
            }
        }

        public void Initialize(TMP_FontAsset value)
        {
            if (initialized) return;
            font = value != null ? value : TMP_Settings.defaultFontAsset;
            if (font == null) throw new ArgumentException("战斗视图需要可用字体。");
            var layout = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var controls = Ui.Row("TacticsBattle_Controls", transform, 6);
            Ui.FixedHeight(controls.gameObject, 34);
            StepButton = Ui.Button("TacticsBattle_Step", controls, font, "单步", () => { Pause(); StepOnce(); }, 74, true);
            AutoButton = Ui.Button("TacticsBattle_Auto", controls, font, "自动播放", StartAuto, 96, true);
            PauseButton = Ui.Button("TacticsBattle_Pause", controls, font, "暂停", () => { Pause(); Feedback("已暂停；没有在后台推进战斗。", false); }, 74);
            ReplayButton = Ui.Button("TacticsBattle_Replay", controls, font, "重放本场", Replay, 104);
            Ui.Label("TacticsBattle_IntervalLabel", controls, font, "间隔秒", 56, 14);
            IntervalInput = Ui.Input("TacticsBattle_Interval", controls, font, 68);
            IntervalInput.SetTextWithoutNotify(Number(interval));
            IntervalInput.onEndEdit.AddListener(_ => ApplyInterval());

            StateText = Ui.Text("TacticsBattle_State", transform, font, "尚未应用编排", 16, Ui.Accent);
            Ui.FixedHeight(StateText.gameObject, 22);
            QueueText = Ui.Text("TacticsBattle_Queue", transform, font, "WAIT 队列将在应用后显示。", 14, Ui.Muted);
            QueueText.enableAutoSizing = true;
            QueueText.fontSizeMin = 11;
            QueueText.fontSizeMax = 14;
            QueueText.textWrappingMode = TextWrappingModes.NoWrap;
            QueueText.overflowMode = TextOverflowModes.Ellipsis;
            Ui.FixedHeight(QueueText.gameObject, 24);

            var boards = Ui.Row("TacticsBattle_Formations", transform, 8);
            Ui.FixedHeight(boards.gameObject, 218);
            friendlyCells = BuildBoard("TacticsBattle_Friendly", "己方 3×3 · 上前排 / 下后排", boards, true);
            enemyCells = BuildBoard("TacticsBattle_Enemy", "敌方 3×3 · 金边行动 / 红边目标", boards, false);

            var eventRow = Ui.Row("TacticsBattle_EventRow", transform, 7);
            Ui.FixedHeight(eventRow.gameObject, 30);
            Ui.Label("TacticsBattle_EventLabel", eventRow, font, "查看事件", 70, 14);
            EventDropdown = BuildEventDropdown(eventRow);
            Ui.Flexible(EventDropdown.gameObject);
            EventDropdown.onValueChanged.AddListener(SelectEvent);

            ReasonText = Ui.ScrollText("TacticsBattle_Reasons", transform, font, out reasonsScroll, 14);
            ReasonText.richText = false;
            ReasonText.margin = new Vector4(3, 2, 3, 2);
            reasonsScroll.GetComponent<LayoutElement>().minHeight = 106;
            FeedbackText = Ui.Text("TacticsBattle_Feedback", transform, font, "先编辑左侧战术，再点击应用并开战。", 14, Ui.Muted);
            FeedbackText.textWrappingMode = TextWrappingModes.NoWrap;
            FeedbackText.overflowMode = TextOverflowModes.Ellipsis;
            Ui.FixedHeight(FeedbackText.gameObject, 20);
            initialized = true;
            Refresh();
        }

        public void Bind(Func<CombatSnapshot> getSnapshot, Func<CombatStepResult[]> getHistory,
            Func<CombatStepResult> step, Action replayCommitted)
        {
            Pause();
            readSnapshot = getSnapshot;
            readHistory = getHistory;
            advance = step;
            replay = replayCommitted;
            FollowLatest = true;
            SelectedEventIndex = -1;
            listedEventCount = -1;
            LastError = null;
            Refresh();
        }

        public void Refresh()
        {
            if (!initialized) return;
            try
            {
                snapshot = readSnapshot == null ? null : readSnapshot();
                history = readHistory == null ? Array.Empty<CombatStepResult>() : readHistory() ?? Array.Empty<CombatStepResult>();
                if (snapshot == null) Pause();
                if (snapshot != null && snapshot.finished) Pause();
                if (history.Length != listedEventCount)
                {
                    if (history.Length == 0 || history.Length < listedEventCount)
                    {
                        FollowLatest = true;
                        SelectedEventIndex = history.Length - 1;
                    }
                    else if (FollowLatest || SelectedEventIndex < 0 || SelectedEventIndex >= history.Length)
                        SelectedEventIndex = history.Length - 1;
                    RefreshEventList();
                }
                else if (SelectedEventIndex >= history.Length) SelectedEventIndex = history.Length - 1;
                RefreshState();
                RefreshReasons();
                RefreshCards();
                RefreshControls();
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        public void StepOnce()
        {
            if (advance == null || readSnapshot == null) { Feedback("战斗尚未绑定；请先应用编排。", true); return; }
            try
            {
                CombatSnapshot before = readSnapshot();
                if (before == null) { Feedback("尚未应用编排；草稿不会自行开战。", true); Refresh(); return; }
                if (before.finished) { Pause(); Refresh(); Feedback("本场已结束；可查看事件或重放本场。", false); return; }
                CombatStepResult result = advance();
                if (result == null) { Pause(); Feedback("核心未返回事件；没有继续自动推进。", true); return; }
                LastError = null;
                Refresh();
                if (FollowLatest) SelectEvent(history.Length - 1);
                Feedback(result.status + " · WAIT " + Number(result.time) + " · " + result.message, false);
                if (snapshot != null && snapshot.finished)
                {
                    Pause();
                    Feedback("本场 " + snapshot.outcome + " · " + snapshot.stopReason + "；完整原因可从事件列表查看。", false);
                }
                PlaybackChanged?.Invoke();
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        public void StartAuto()
        {
            if (!ApplyInterval()) return;
            Refresh();
            if (snapshot == null || snapshot.finished || advance == null)
            { Feedback(snapshot == null ? "请先应用并开战。" : "本场已结束；请重放本场。", false); return; }
            elapsed = 0;
            IsAutoPlaying = true;
            Feedback("每 " + Number(interval) + " 秒结算一个真实事件；可随时暂停，切换入口会自动暂停。", false);
            RefreshControls();
        }

        public void Pause()
        {
            IsAutoPlaying = false;
            elapsed = 0;
            if (initialized) RefreshControls();
        }

        public void Replay()
        {
            Pause();
            if (replay == null || readSnapshot == null)
            { Feedback("尚无已提交的本场配置；请先应用并开战。", true); return; }
            try
            {
                if (readSnapshot() == null)
                { Feedback("尚无已提交的本场配置；请先应用并开战。", true); return; }
                replay();
                FollowLatest = true;
                SelectedEventIndex = -1;
                listedEventCount = -1;
                LastError = null;
                Refresh();
                Feedback("已恢复上次提交本场的完整开局与种子；当前未提交草稿没有混入重放。", false);
                PlaybackChanged?.Invoke();
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        public void SelectEvent(int index)
        {
            if (!initialized) return;
            SelectedEventIndex = index >= 0 && index < history.Length ? index : -1;
            if (SelectedEventIndex >= 0) FollowLatest = SelectedEventIndex == history.Length - 1;
            if (SelectedEventIndex >= 0) EventDropdown.SetValueWithoutNotify(SelectedEventIndex);
            RefreshReasons();
            RefreshCards();
            reasonsScroll.verticalNormalizedPosition = 1;
        }

        public void SetFollowLatest(bool follow)
        {
            FollowLatest = follow;
            if (!initialized) return;
            if (follow)
            {
                SelectedEventIndex = history.Length - 1;
                if (SelectedEventIndex >= 0) EventDropdown.SetValueWithoutNotify(SelectedEventIndex);
            }
            RefreshReasons();
            RefreshCards();
        }

        private void Update()
        {
            if (ExternalPlaybackClock) return;
            TickPlayback(Time.unscaledDeltaTime);
        }

        /// <summary>Only the shared host drives this clock when candidate views are installed.</summary>
        public void TickPlayback(double unscaledDeltaSeconds)
        {
            if (!IsAutoPlaying) return;
            elapsed += unscaledDeltaSeconds;
            if (elapsed < interval) return;
            // Intentionally one event per frame at most: no catch-up loop or precomputed full battle.
            elapsed = 0;
            StepOnce();
        }

        private void OnDisable() { Pause(); }

        private void LateUpdate()
        {
            if (!initialized || snapshot == null || friendlyCells == null) return;
            float width = friendlyCells[0].label.rectTransform.rect.width;
            if (Mathf.Abs(width - lastCardWidth) < .25f) return;
            lastCardWidth = width;
            RefreshCards();
        }

        private bool ApplyInterval()
        {
            double value;
            if (!double.TryParse(IntervalInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < .05 || value > 5)
            { Feedback("播放间隔请输入 0.05 至 5 秒。", true); return false; }
            interval = value;
            return true;
        }

        private void RefreshControls()
        {
            bool available = snapshot != null && advance != null;
            StepButton.interactable = available && !snapshot.finished && !IsAutoPlaying;
            AutoButton.interactable = available && !snapshot.finished && !IsAutoPlaying;
            PauseButton.interactable = IsAutoPlaying;
            ReplayButton.interactable = available && replay != null;
            EventDropdown.interactable = history.Length > 0;
            IntervalInput.interactable = !IsAutoPlaying;
        }

        private void RefreshState()
        {
            if (snapshot == null)
            {
                StateText.text = "尚未应用编排 · 棋盘没有运行中的战斗";
                QueueText.text = "左侧草稿可编辑；点击「应用并开战」才创建本场。";
                return;
            }
            CombatUnitSnapshot next = snapshot.units.FirstOrDefault(unit => unit.id == snapshot.nextActorId);
            StateText.text = "WAIT " + Number(snapshot.time) + "  ·  实际行动 " + snapshot.completedStepCount +
                (snapshot.finished ? "  ·  " + snapshot.outcome : "  ·  下一位 " + (next == null ? "等待事件" : next.name));
            CombatUnitSnapshot[] pending = snapshot.units.Where(unit => unit.hp > 0 && !unit.dormant).OrderBy(unit => unit.nextActionTime).ToArray();
            var queue = pending.Take(6).Select(unit => (unit.id == snapshot.nextActorId ? "[下一位] " : "") + unit.name + " @" + Number(unit.nextActionTime));
            QueueText.text = "近时刻：" + string.Join(" → ", queue.ToArray()) + (pending.Length > 6 ? " · 另" + (pending.Length - 6) + "位" : string.Empty);
            string sleeping = string.Join("、", snapshot.units.Where(unit => unit.hp > 0 && unit.dormant).Select(unit => unit.name).ToArray());
            if (!string.IsNullOrEmpty(sleeping)) QueueText.text += " · 休眠：" + sleeping;
            if (snapshot.finished) QueueText.text = "停止原因：" + snapshot.stopReason;
        }

        private void RefreshEventList()
        {
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (CombatStepResult step in history)
                options.Add(new TMP_Dropdown.OptionData("#" + step.index + " WAIT " + Number(step.time) + "  " +
                    (string.IsNullOrEmpty(step.actorName) ? step.status : step.actorName + " · " +
                    (string.IsNullOrEmpty(step.actionName) ? step.status : step.actionName + " → " + step.targetName)) +
                    (step.targetIds != null && step.targetIds.Length > 1 ? " · 覆盖" + step.targetIds.Length + "人" : string.Empty)));
            if (options.Count == 0) options.Add(new TMP_Dropdown.OptionData("尚无实际事件"));
            EventDropdown.ClearOptions();
            EventDropdown.AddOptions(options);
            EventDropdown.SetValueWithoutNotify(Math.Max(0, SelectedEventIndex));
            EventDropdown.RefreshShownValue();
            listedEventCount = history.Length;
        }

        private void RefreshReasons()
        {
            HighlightedActorId = string.Empty;
            HighlightedTargetId = string.Empty;
            highlightedTargetIds.Clear();
            SelectedEffectsSummary = string.Empty;
            if (SelectedEventIndex < 0 || SelectedEventIndex >= history.Length)
            {
                ReasonText.text = snapshot == null ? "尚无本场事件。\n\n应用编排后可单步，或以可控间隔自动播放。" :
                    "本场尚未推进。\n\n先预测第一位角色选择哪条指令，再单步观察。\n\n金边为行动者，红边为目标；点击角色卡可查看当前完整状态。";
                return;
            }
            CombatStepResult step = history[SelectedEventIndex];
            HighlightedActorId = step.actorId;
            HighlightedTargetId = step.targetId;
            // Native targetIds excludes auxiliary recipients such as drain_mp's caster refund.
            // The fallback is for records produced before the primary-coverage field existed.
            if (step.targetIds != null && step.targetIds.Length > 0)
            {
                foreach (string id in step.targetIds) AddHighlightedTarget(id);
            }
            else
            {
                foreach (CombatEffect effect in step.effects ?? Array.Empty<CombatEffect>()) AddHighlightedTarget(effect.targetId);
                if (highlightedTargetIds.Count == 0) AddHighlightedTarget(step.targetId);
            }
            SelectedEffectsSummary = FormatEffects(step);
            var text = new StringBuilder();
            text.Append("#").Append(step.index).Append(" · WAIT ").Append(Number(step.time)).Append(" · ").Append(step.status).Append('\n');
            text.Append(step.actorName).Append(" → ").Append(step.actionName).Append(" → 锚点 ").Append(step.targetName).Append('\n');
            text.Append(step.targetIds != null && step.targetIds.Length > 0 ? "主覆盖：" : "效果对象（兼容记录）：")
                .Append(highlightedTargetIds.Count == 0 ? "无" : TargetNames(highlightedTargetIds)).Append('\n');
            text.Append("实际结果：").Append(step.message).Append('\n');
            if (!string.IsNullOrEmpty(step.controlReason)) text.Append("控制原因：").Append(step.controlReason).Append('\n');
            if (!string.IsNullOrEmpty(step.tieBreakReason)) text.Append("同刻裁决：").Append(step.tieBreakReason).Append('\n');
            if (step.status == "Executed")
                text.Append("施放者 ").Append(step.actorName).Append(" MP ").Append(step.mpBefore).Append("→").Append(step.mpAfter)
                    .Append("；本次费用 ").Append(step.mpCost).Append("；WAIT ").Append(Number(step.baseWait)).Append('×').Append(Number(step.agilityFactor))
                    .Append('=').Append(Number(step.actualWait)).Append("（基础敏捷 ").Append(Number(step.baseAgilityFactor))
                    .Append("，状态倍率 ").Append(Number(step.statusWaitFactor)).Append("）；下次 ").Append(Number(step.nextActionTime)).Append('\n');
            if (!string.IsNullOrEmpty(SelectedEffectsSummary)) text.Append("\n实际效果（逐项真实前后值）：\n").Append(SelectedEffectsSummary).Append('\n');
            CombatInstructionAttempt chosen = (step.attempts ?? Array.Empty<CombatInstructionAttempt>()).FirstOrDefault(attempt => attempt.outcome == "Selected");
            CombatInstructionAttempt previous = chosen == null ? null : step.attempts.LastOrDefault(attempt => attempt.instructionIndex < chosen.instructionIndex);
            if (previous != null)
                text.Append("前一条为什么跳过：第 ").Append(previous.instructionIndex + 1).Append(" 条，")
                    .Append(string.IsNullOrEmpty(previous.reasonCode) ? "" : "[" + previous.reasonCode + "] ").Append(previous.reason).Append('\n');
            else if (chosen != null) text.Append("本次第一条已可执行，没有更高优先级指令被跳过。\n");
            foreach (CombatInstructionAttempt attempt in step.attempts ?? Array.Empty<CombatInstructionAttempt>())
            {
                text.Append("\n第 ").Append(attempt.instructionIndex + 1).Append(" 条 · ").Append(attempt.outcome).Append('\n')
                    .Append(attempt.display).Append('\n').Append("原因：")
                    .Append(string.IsNullOrEmpty(attempt.reasonCode) ? "" : "[" + attempt.reasonCode + "] ").Append(attempt.reason).Append('\n');
                foreach (CombatCandidateTrace candidate in attempt.candidates ?? Array.Empty<CombatCandidateTrace>())
                {
                    text.Append("  ").Append(candidate.unitName).Append(" [条件").Append(candidate.passesCondition ? "满足" : "不满足")
                        .Append(" / ").Append(candidate.eligible ? "可执行" : "不可执行").Append("] ").Append(candidate.reason).Append('\n');
                    if (candidate.affectedTargetIds != null && candidate.affectedTargetIds.Length > 0)
                        text.Append("    实际候选覆盖：").Append(TargetNames(candidate.affectedTargetIds)).Append('\n');
                }
            }
            text.Append("\n\n棋盘显示当前真实状态；历史事件的 HP/MP/状态前后值以上述实际记录为准。");
            ReasonText.text = text.ToString();
        }

        private void AddHighlightedTarget(string id)
        {
            if (!string.IsNullOrEmpty(id) && !highlightedTargetIds.Contains(id)) highlightedTargetIds.Add(id);
        }

        private string TargetNames(IEnumerable<string> ids)
        {
            return string.Join("、", ids.Select(id =>
            {
                CombatUnitSnapshot unit = snapshot == null ? null : snapshot.units.FirstOrDefault(value => value.id == id);
                return unit == null ? id : unit.name + " [" + id + "]";
            }).ToArray());
        }

        private string FormatEffects(CombatStepResult step)
        {
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(step.controlReason))
                text.Append("控制原因 ").Append(step.controlReason).Append("；实际事件 ").Append(step.status).Append('\n');
            bool nativeCoverage = step.targetIds != null && step.targetIds.Length > 0;
            foreach (CombatEffect effect in step.effects ?? Array.Empty<CombatEffect>())
            {
                if (text.Length > 0) text.Append('\n');
                if (nativeCoverage && !highlightedTargetIds.Contains(effect.targetId)) text.Append("附加效果（非主覆盖） ");
                text.Append(effect.targetName).Append(" [").Append(effect.targetId).Append("] ").Append(effect.kind)
                    .Append(" · HP ").Append(effect.hpBefore).Append("→").Append(effect.hpAfter).Append(" (Δ").Append(Signed(effect.hpDelta))
                    .Append(") · MP ").Append(effect.mpBefore).Append("→").Append(effect.mpAfter).Append(" (Δ").Append(Signed(effect.mpDelta)).Append(")\n");
                if (!string.IsNullOrEmpty(effect.statusId))
                    text.Append("  状态 ").Append(effect.statusId).Append("；施加 ").Append(Number(effect.statusAppliedAt))
                        .Append("；到期 ").Append(Number(effect.statusExpiresAt)).Append('\n');
                if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0)
                    text.Append("  移除状态：").Append(string.Join("、", effect.removedStatusIds)).Append('\n');
                text.Append("  永久易伤标记 ").Append(effect.baseVulnerableBefore).Append("→").Append(effect.baseVulnerableAfter)
                    .Append("；").Append(effect.description);
            }
            return text.ToString();
        }

        private Cell[] BuildBoard(string name, string title, Transform parent, bool friendly)
        {
            var column = Ui.Column(name, parent, 4);
            Ui.Flexible(column.gameObject);
            var heading = Ui.Text(name + "_Heading", column, font, title, 13, friendly ? Ui.Good : Ui.Bad);
            Ui.FixedHeight(heading.gameObject, 20);
            var board = Ui.Rect(name + "_Grid", column);
            Ui.Flexible(board.gameObject);
            var grid = board.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            var sizer = board.gameObject.AddComponent<PrototypeMapGridSizer>();
            sizer.columns = 3; sizer.rows = 3;
            var cells = new Cell[9];
            for (int i = 0; i < cells.Length; i++)
            {
                var image = Ui.Image(name + "_Cell_" + i, board, Ui.Background);
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var label = Ui.Text("Card", image.transform, font, "空位", 14, Ui.Muted);
                // Noto CJK at 14 pt needs about 60.3 px for these three lines.
                // A 62.7 px cell cannot lose another 4 px vertically: Ellipsis then removes the MP line.
                Ui.Stretch(label.rectTransform, 3, 0, 3, 0);
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                // Ellipsis is global to TMP text: a long name on line one can discard both resource lines.
                // Fit only the heading ourselves; the fixed-size HP and MP lines must always be generated.
                label.overflowMode = TextOverflowModes.Overflow;
                label.lineSpacing = -2;
                var outline = image.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(2, -2);
                outline.enabled = false;
                var cell = new Cell { image = image, button = button, label = label, outline = outline };
                button.onClick.AddListener(() => ShowUnit(cell.unitId));
                cells[i] = cell;
            }
            return cells;
        }

        private void RefreshCards()
        {
            foreach (Cell cell in friendlyCells.Concat(enemyCells))
            {
                cell.unitId = null; cell.label.text = "空位"; cell.label.color = Ui.Muted;
                cell.image.color = Ui.Background; cell.outline.enabled = false; cell.button.interactable = false;
            }
            if (snapshot == null) return;
            foreach (CombatUnitSnapshot unit in snapshot.units)
            {
                if (unit.row < 0 || unit.row > 2 || unit.column < 0 || unit.column > 2) continue;
                bool friendly = unit.team == FriendlyTeam;
                Cell cell = (friendly ? friendlyCells : enemyCells)[unit.row * 3 + unit.column];
                bool actor = unit.id == HighlightedActorId;
                bool target = highlightedTargetIds.Contains(unit.id);
                cell.unitId = unit.id;
                cell.button.interactable = true;
                string states = unit.hp <= 0 ? "倒下" : StatusSummary(unit);
                cell.label.text = FitFirstLine(cell, unit.name + " " + states) + "\nHP " + unit.hp + "/" + unit.maxHp +
                    "\nMP " + unit.mp + "/" + unit.maxMp;
                cell.label.color = unit.hp <= 0 ? Ui.Muted : Ui.Ink;
                cell.image.color = unit.hp <= 0 ? new Color32(34, 38, 47, 255) : friendly ? new Color32(31, 65, 61, 255) : new Color32(68, 42, 50, 255);
                if (target) cell.image.color = new Color32(98, 48, 51, 255);
                cell.outline.enabled = actor || target || (SelectedEventIndex < 0 && unit.id == snapshot.nextActorId);
                cell.outline.effectColor = actor ? Ui.Accent : target ? Ui.Bad : Ui.Good;
            }
        }

        private static string FitFirstLine(Cell cell, string source)
        {
            float width = cell.label.rectTransform.rect.width - Mathf.Max(0, cell.label.margin.x)
                - Mathf.Max(0, cell.label.margin.z) - 4;
            if (width <= 0) return source; // The settled canvas dimensions are applied in LateUpdate.
            if (cell.firstLineSource == source && Mathf.Abs(cell.firstLineWidth - width) < .25f) return cell.firstLineText;
            string result = source;
            if (cell.label.GetPreferredValues(source, float.PositiveInfinity, float.PositiveInfinity).x > width)
            {
                const string ellipsis = "…";
                int[] starts = StringInfo.ParseCombiningCharacters(source);
                int low = 0;
                int high = starts.Length;
                while (low < high)
                {
                    int count = (low + high + 1) / 2;
                    int end = count == starts.Length ? source.Length : starts[count];
                    string candidate = source.Substring(0, end) + ellipsis;
                    if (cell.label.GetPreferredValues(candidate, float.PositiveInfinity, float.PositiveInfinity).x <= width) low = count;
                    else high = count - 1;
                }
                int cut = low == starts.Length ? source.Length : starts[low];
                result = source.Substring(0, cut) + ellipsis;
            }
            cell.firstLineSource = source;
            cell.firstLineWidth = width;
            cell.firstLineText = result;
            return result;
        }

        private static string StatusSummary(CombatUnitSnapshot unit)
        {
            var names = new List<string>();
            foreach (CombatStatusConfig status in unit.statuses ?? Array.Empty<CombatStatusConfig>())
                names.Add(ShortStatus(status.kind));
            if (unit.baseVulnerable && !names.Contains("易")) names.Add("易");
            if (unit.dormant) names.Add("眠");
            if (unit.equipmentDamageBonus != 0) names.Add("装+" + unit.equipmentDamageBonus);
            if (names.Count == 0) return "无状态";
            return string.Join("/", names.Take(2).ToArray()) + (names.Count > 2 ? " +" + (names.Count - 2) : string.Empty);
        }

        public void ShowUnit(string id)
        {
            if (snapshot == null || string.IsNullOrEmpty(id)) return;
            CombatUnitSnapshot unit = snapshot.units.FirstOrDefault(value => value.id == id);
            if (unit == null) return;
            var text = new StringBuilder(unit.name + " · 当前真实状态\n");
            text.Append("HP ").Append(unit.hp).Append('/').Append(unit.maxHp).Append(" MP ").Append(unit.mp).Append('/').Append(unit.maxMp)
                .Append("；下一次 WAIT ").Append(Number(unit.nextActionTime)).Append('\n');
            text.Append("智力 ").Append(unit.intelligence).Append("；指令容量 ").Append(unit.instructionCapacity)
                .Append("；每条条件项 ").Append(unit.conditionTermCapacity).Append('\n');
            text.Append("标签：").Append(unit.tags == null || unit.tags.Length == 0 ? "无" : string.Join("、", unit.tags))
                .Append("；当前有效敏捷系数 ").Append(Number(unit.effectiveAgilityFactor)).Append("（越小越快；已有预约按核心保留）\n");
            text.Append("装备来源：").Append(unit.equipmentSourceId).Append("；伤害加成 ").Append(unit.equipmentDamageBonus).Append('\n');
            foreach (CombatStatusConfig status in unit.statuses ?? Array.Empty<CombatStatusConfig>())
                text.Append("状态 ").Append(status.id).Append(" / ").Append(StatusLabel(status.kind)).Append(" (").Append(status.kind).Append(")；幅度 ").Append(status.magnitude)
                    .Append("；施加 ").Append(Number(status.appliedAt)).Append("；到期 ").Append(Number(status.expiresAt)).Append('\n');
            foreach (CombatInstructionSnapshot instruction in unit.instructions ?? Array.Empty<CombatInstructionSnapshot>())
                text.Append(instruction.capacityAllowed ? "可装载 " : "容量不允许 ").Append(instruction.display).Append('\n');
            text.Append("\n可从上方事件列表重新选择行动，查看逐条件与候选原因。");
            ReasonText.text = text.ToString();
            reasonsScroll.verticalNormalizedPosition = 1;
        }

        private void Feedback(string message, bool error)
        {
            if (FeedbackText == null) return;
            FeedbackText.text = message; FeedbackText.color = error ? Ui.Bad : Ui.Muted;
        }
        private void ShowFailure(Exception exception) { LastError = exception.ToString(); Pause(); Feedback("播放失败：" + exception.Message, true); }
        private static string Number(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string Signed(int value) { return (value >= 0 ? "+" : "") + value.ToString(CultureInfo.InvariantCulture); }
        private static string ShortStatus(string kind)
        {
            switch (kind)
            {
                case "guard": return "护";
                case "empowered": return "增";
                case "vulnerable": return "易";
                case "magic_guard": return "魔盾";
                case "haste": return "急";
                case "slow": return "缓";
                case "silenced": return "默";
                case "stunned": return "晕";
                case "weakened": return "弱";
                default: return "状";
            }
        }
        private static string StatusLabel(string kind)
        {
            CombatTacticsOption option = CombatTacticsCatalog.GetStatusKinds().FirstOrDefault(value => value.id == kind);
            return option == null ? kind : option.label;
        }

        private TMP_Dropdown BuildEventDropdown(Transform parent)
        {
            var image = Ui.Image("TacticsBattle_Event", parent, Ui.Background);
            image.gameObject.SetActive(false);
            var dropdown = image.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = image;
            var caption = Ui.Text("Caption", image.transform, font, "尚无实际事件", 14);
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Stretch(caption.rectTransform, 9, 3, 27, 3);
            var arrow = Ui.Text("Arrow", image.transform, font, "▼", 14, Ui.Muted);
            arrow.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Stretch(arrow.rectTransform, 0, 0, 8, 0);
            var template = Ui.Image("Template", image.transform, Ui.Card).rectTransform;
            template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(.5f, 1); template.sizeDelta = new Vector2(0, 210);
            template.anchoredPosition = new Vector2(0, -2);
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 28;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Ui.Rect("Viewport", template); Ui.Stretch(viewport, 3, 3, 3, 3);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 34);
            var item = Ui.Image("Item", content, Ui.Background);
            item.rectTransform.anchorMin = new Vector2(0, 1); item.rectTransform.anchorMax = new Vector2(1, 1);
            item.rectTransform.pivot = new Vector2(.5f, 1); item.rectTransform.sizeDelta = new Vector2(0, 34);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item;
            var check = Ui.Image("Checkmark", item.transform, Ui.Accent);
            check.rectTransform.anchorMin = new Vector2(0, .5f); check.rectTransform.anchorMax = new Vector2(0, .5f);
            check.rectTransform.sizeDelta = new Vector2(6, 12); check.rectTransform.anchoredPosition = new Vector2(7, 0);
            toggle.graphic = check;
            var label = Ui.Text("Label", item.transform, font, "事件", 13);
            Ui.Stretch(label.rectTransform, 18, 3, 5, 3); label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            scroll.viewport = viewport; scroll.content = content;
            dropdown.template = template; dropdown.captionText = caption; dropdown.itemText = label;
            dropdown.options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("尚无实际事件") };
            template.gameObject.SetActive(false);
            image.gameObject.SetActive(true);
            return dropdown;
        }
    }
}
