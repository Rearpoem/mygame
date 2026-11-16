using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;

namespace Prototype.UnityClient.Candidates.Common
{
    /// <summary>One command owner and one playback clock for legacy and every candidate.
    /// The existing editor remains the sole raw-form adapter; views never drive hidden buttons.</summary>
    public sealed class TacticsSharedController : MonoBehaviour
    {
        private TacticsPanelController source;
        private bool notifying;
        private string inspectedUnitId;
        private readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        public event Action Changed;
        public TacticsPanelController Source { get { return source; } }
        public string[] CaseTitles { get { return source.CaseTitles; } }
        public int SelectedCaseIndex { get { return source.SelectedCaseIndex; } }

        public void Initialize(TacticsPanelController controller)
        {
            if (source != null) throw new InvalidOperationException("共享控制器只能绑定一个真实工作台。");
            source = controller ?? throw new ArgumentNullException(nameof(controller));
            source.BattleView.ExternalPlaybackClock = true;
            source.Changed += RaiseChanged;
        }

        public TacticsCandidateState GetState()
        {
            var snapshot = source.Workbench.GetSnapshot();
            var editor = source.InstructionEditor;
            var playback = source.BattleView;
            return new TacticsCandidateState
            {
                workbench = snapshot, form = editor.GetFormInputSnapshot(), catalog = editor.Catalog,
                battle = snapshot.battleSnapshot, history = snapshot.history ?? Array.Empty<CombatStepResult>(),
                visibleActions = editor.VisibleActions.ToArray(), matchingActionCount = editor.MatchingActionCount,
                selectedActionRetained = editor.SelectedActionRetained, isAutoPlaying = playback.IsAutoPlaying,
                intervalSeconds = playback.PlaybackIntervalSeconds, selectedEventIndex = playback.SelectedEventIndex,
                followLatest = playback.FollowLatest,
                inspectedUnitId = inspectedUnitId, highlightedActorId = playback.HighlightedActorId,
                highlightedTargetIds = playback.HighlightedTargetIds.ToArray(), selectedEffectsSummary = playback.SelectedEffectsSummary,
                revisionText = source.RevisionText.text, feedback = source.FeedbackText.text,
                reasonText = playback.ReasonText.text, queueText = playback.QueueText.text,
                actionMatchText = editor.ActionMatchText.text, actionDetailsText = editor.ActionDetailsText.text,
                comparisonText = source.ComparisonText.text, isComparisonVisible = source.IsComparisonVisible,
                lastComparison = source.LastComparison, lastEdit = source.LastEditResult,
                lastApply = source.LastApplyResult, lastSend = source.LastSendResult,
                exportDirectory = source.LastExportDirectory, exportError = source.LastExportError, lastError = source.LastError
            };
        }

        public void SelectCase(int index) { inspectedUnitId = null; Execute(() => source.SelectCase(index)); }
        public void SelectUnit(string unitId) { Execute(() => source.SelectCharacter(unitId)); }
        public void SelectInstruction(int index) { Execute(() => source.SelectInstruction(index)); }
        public void LoadSelected() { Execute(source.InstructionEditor.LoadSelectedInstruction); }
        public void SetForm(TacticsFormInput form, bool markDirty = true) { Execute(() => source.InstructionEditor.ApplyFormInput(form, markDirty)); }
        public void SetActionSearch(string query) { Execute(() => source.InstructionEditor.ActionSearchInput.text = query ?? ""); }
        public void NewBlank() { Execute(source.NewBlank); }
        public void Insert() { Execute(source.Insert); }
        public void Replace() { Execute(source.Replace); }
        public void Delete() { Execute(source.Delete); }
        public void Move(int delta) { Execute(() => source.Move(delta)); }

        public bool CommitBattle()
        {
            var previous = source.LastApplyResult;
            inspectedUnitId = null;
            Execute(source.ApplyBattle);
            return source.LastApplyResult != null && !ReferenceEquals(previous, source.LastApplyResult) && source.LastApplyResult.success;
        }
        public void Replay() { inspectedUnitId = null; Execute(source.Replay); }
        public void Step() { inspectedUnitId = null; Execute(() => { source.BattleView.Pause(); source.BattleView.StepOnce(); }); }
        public void StartAuto() { Execute(source.BattleView.StartAuto); }
        public void Pause() { if (source != null) Execute(source.BattleView.Pause); }
        public void SetPlaybackInterval(double seconds) { Execute(() => source.BattleView.PlaybackIntervalSeconds = seconds); }
        public void SelectEvent(int index) { inspectedUnitId = null; Execute(() => source.BattleView.SelectEvent(index)); }
        public void SetFollowLatest(bool follow) { Execute(() => source.BattleView.SetFollowLatest(follow)); }
        public void InspectUnit(string unitId) { inspectedUnitId = unitId; Execute(() => source.BattleView.ShowUnit(unitId)); }
        public bool Compare()
        {
            var previous = source.LastComparison;
            Execute(source.Compare);
            return source.LastComparison != null && !ReferenceEquals(previous, source.LastComparison) && source.LastComparison.success;
        }
        public string Export() { Execute(source.Export); return source.LastExportError == null ? source.LastExportDirectory : null; }
        public bool SendToExpedition()
        {
            var previous = source.LastSendResult;
            Execute(source.SendToExpedition);
            return source.LastSendResult != null && !ReferenceEquals(previous, source.LastSendResult) && source.LastSendResult.success;
        }
        public void Reset() { inspectedUnitId = null; Execute(source.ResetWorkbench); }
        public void ShowBattle() { Execute(source.ShowBattle); }
        public void NotifyUser(string message, bool error = false) { Execute(() => source.NotifyUser(message, error)); }

        public Sprite GetPortrait(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            if (!portraits.TryGetValue(unitId, out var sprite))
            {
                sprite = Resources.Load<Sprite>("PrototypeUI/Portraits/unit_" + unitId + "_256");
                portraits.Add(unitId, sprite);
            }
            return sprite;
        }

        private void Execute(Action command)
        {
            try { command(); }
            catch (Exception exception) { source.NotifyUser("操作未完成：" + exception.Message, true); }
            RaiseChanged();
        }
        private void RaiseChanged()
        {
            if (notifying) return;
            notifying = true;
            try { Changed?.Invoke(); }
            finally { notifying = false; }
        }
        private void Update()
        {
            if (source == null || !source.BattleView.IsAutoPlaying) return;
            source.BattleView.TickPlayback(Time.unscaledDeltaTime);
        }
        private void OnDisable() { if (source != null) source.BattleView.Pause(); }
        private void OnDestroy() { if (source != null) source.Changed -= RaiseChanged; }
    }
}
