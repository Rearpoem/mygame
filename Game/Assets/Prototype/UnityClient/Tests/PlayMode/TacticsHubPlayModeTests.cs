using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Prototype.Combat;
using Prototype.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Prototype.UnityClient.Tests
{
    public sealed class TacticsHubPlayModeTests
    {
        private PrototypeHubController hub;
        private TacticsPanelController panel;
        private TacticsInstructionEditor editor;
        private TacticsBattleView battle;

        [UnitySetUp]
        public IEnumerator CreateActualPlayerHub()
        {
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "GenerateHub must run first.");
            hub = PrototypeHubFactory.Create(TMP_Settings.defaultFontAsset);
            Assert.That(hub.CurrentModule, Is.EqualTo("Tactics"));
            // These original regressions exercise the real, visible legacy fallback.
            // Candidate button paths and shared-state switching are covered separately.
            Assert.That(hub.TacticsCandidates.SelectCandidate("legacy"), Is.True);
            panel = hub.TacticsView;
            Assert.That(panel, Is.Not.Null);
            panel.LoadConfiguration(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "round3_tactics.json")));
            Assert.That(panel.IsConfigured, Is.True, panel.LastError);
            editor = panel.InstructionEditor;
            battle = panel.BattleView;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyActualHub()
        {
            if (battle != null) battle.Pause();
            if (hub != null) UnityEngine.Object.Destroy(hub.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DefaultWorkbenchShowsCapacityAndNoBattleRunsBeforeExplicitCommit()
        {
            Assert.That(panel.Workbench.GetBattleSnapshot(), Is.Null);
            Assert.That(panel.Workbench.GetBattleHistory(), Is.Empty);
            Assert.That(battle.StepButton.interactable, Is.False);
            Assert.That(battle.AutoButton.interactable, Is.False);
            Assert.That(panel.RevisionText.text, Does.Contain("尚未开战"));
            AssertVisibleToken(panel.RevisionText, panel.RevisionText.rectTransform, "尚未开战");
            AssertVisibleToken(editor.CapacityText, editor.CapacityText.rectTransform, "指令 4/5");
            AssertVisibleToken(editor.CapacityText, editor.CapacityText.rectTransform, "每条条件项容量 3");
            AssertFixedControlsInsideCanvas();
            Assert.That(panel.LastError, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BlankFourPartRuleIsBuiltThroughDropdownsThenInsertedReplacedMovedAndDeleted()
        {
            int originalCount = Mage().instructions.Length;
            Click(panel.NewBlankButton);
            Assert.That(editor.Terms, Is.Empty);
            Assert.That(editor.OperationDropdown.value, Is.Zero);
            Assert.That(editor.ScopeDropdown.value, Is.Zero);
            Assert.That(editor.SelectorDropdown.value, Is.Zero);
            Assert.That(editor.ActionDropdown.value, Is.Zero);
            string original = Signature(panel.Workbench.GetDraftScenario());
            Click(panel.InsertButton);
            Assert.That(panel.LastEditResult.success, Is.False);
            Assert.That(panel.LastEditResult.message, Is.Not.Empty);
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(original));
            FillBlankFourParts();
            editor.LabelInput.text = "按钮新建完整规则";
            Click(panel.InsertButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            Assert.That(Mage().instructions.Length, Is.EqualTo(originalCount + 1));
            var inserted = Mage().instructions.Last();
            Assert.That(inserted.label, Is.EqualTo("按钮新建完整规则"));
            Assert.That(inserted.condition.operation, Is.EqualTo("all"));
            Assert.That(inserted.condition.terms.Single().kind, Is.EqualTo("always"));
            Assert.That(inserted.scope, Is.EqualTo("enemies"));
            Assert.That(inserted.selector, Is.EqualTo("highest_hp"));
            Assert.That(editor.Catalog.actions.Any(action => action.id == inserted.actionId), Is.True);
            Click(panel.MoveUpButton);
            Assert.That(panel.SelectedInstructionIndex, Is.EqualTo(originalCount - 1));
            Click(panel.MoveDownButton);
            Assert.That(panel.SelectedInstructionIndex, Is.EqualTo(originalCount));
            Choose(editor.SelectorDropdown, editor.Catalog.selectors, "front_first");
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            Assert.That(Mage().instructions.Last().selector, Is.EqualTo("front_first"));
            Click(panel.DeleteButton);
            Assert.That(panel.LastEditResult.success, Is.True);
            Assert.That(Mage().instructions.Length, Is.EqualTo(originalCount));
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(original));
            Assert.That(panel.Workbench.GetBattleSnapshot(), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExplicitGroupingAndCapacityErrorsUseRealButtonsAndPreserveFailedDrafts()
        {
            Click(panel.NewBlankButton);
            FillBlankFourParts();
            Choose(editor.OperationDropdown, editor.Catalog.operations, "mixed");
            Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, "mp_ratio_at_least");
            Choose(editor.Terms[0].SubjectDropdown, editor.Catalog.subjects, "actor");
            editor.Terms[0].ThresholdInput.text = "0.75";
            Click(editor.AddConditionButton);
            Choose(editor.Terms[1].KindDropdown, editor.Catalog.conditions, "vulnerable");
            Click(editor.AddConditionButton);
            Choose(editor.Terms[2].KindDropdown, editor.Catalog.conditions, "row_is");
            editor.Terms[2].ThresholdInput.text = "2";
            var binary = editor.Catalog.operations.Where(option => option.id != "mixed").ToArray();
            Choose(editor.LeftOperatorDropdown, binary, "all");
            Choose(editor.RightOperatorDropdown, binary, "any");
            string before = Signature(panel.Workbench.GetDraftScenario());
            Click(panel.InsertButton);
            Assert.That(panel.LastEditResult.code, Is.EqualTo("InvalidCondition"));
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(before));
            Assert.That(editor.Terms.Count, Is.EqualTo(3), "A failed submit keeps the user's fields.");
            Choose(editor.GroupingDropdown, editor.Catalog.groupings, "left");
            Assert.That(editor.ExpressionText.text, Does.Contain("("));
            Assert.That(editor.ExpressionText.text, Does.Contain(")"));
            Assert.That(editor.ExpressionText.text, Does.Contain("施放者"));
            Click(panel.InsertButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            var mixed = Mage().instructions.Last().condition;
            Assert.That(mixed.grouping, Is.EqualTo("left"));
            Assert.That(mixed.operators, Is.EqualTo(new[] { "and", "or" }));
            Assert.That(mixed.terms[0].subject, Is.EqualTo("actor"));
            Click(panel.NewBlankButton);
            FillBlankFourParts();
            before = Signature(panel.Workbench.GetDraftScenario());
            Click(panel.InsertButton);
            Assert.That(panel.LastEditResult.code, Is.EqualTo("InstructionCapacityExceeded"));
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(before));
            Assert.That(panel.FeedbackText.text, Does.Contain(panel.LastEditResult.message));

            SelectUnit("ally");
            SelectRule(0);
            Click(editor.AddConditionButton);
            Choose(editor.Terms.Last().KindDropdown, editor.Catalog.conditions, "always");
            before = Signature(panel.Workbench.GetDraftScenario());
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.code, Is.EqualTo("ConditionCapacityExceeded"));
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(before));
            Click(editor.Terms.Last().RemoveButton);
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FormSubmissionDraftApplicationAndCommittedReplayRemainThreeExplicitStates()
        {
            CommitAndPause();
            Assert.That(panel.Workbench.GetBattleHistory(), Is.Empty);
            var committed = panel.Workbench.GetCommittedScenario();
            SelectRule(0);
            editor.Terms[0].ThresholdInput.text = "0.10";
            Click(panel.ApplyBattleButton);
            Assert.That(panel.LastActionMessage, Does.Contain("尚未提交"));
            Assert.That(Signature(panel.Workbench.GetCommittedScenario()), Is.EqualTo(Signature(committed)));
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True);
            Assert.That(panel.Workbench.GetSnapshot().hasUncommittedChanges, Is.True);
            Assert.That(panel.Workbench.GetBattleHistory(), Is.Empty);
            var expected = new CombatSession(committed);
            Click(battle.StepButton);
            Assert.That(Signature(panel.Workbench.GetBattleHistory().Single()), Is.EqualTo(Signature(expected.Step())));
            Click(panel.ReplayButton);
            Assert.That(panel.Workbench.GetBattleHistory(), Is.Empty);
            Assert.That(panel.Workbench.GetSnapshot().hasUncommittedChanges, Is.True);
            Assert.That(Mage(panel.Workbench.GetCommittedScenario()).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.35));
            CommitAndPause();
            Assert.That(Mage(panel.Workbench.GetCommittedScenario()).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.10));
            Assert.That(panel.Workbench.GetSnapshot().hasUncommittedChanges, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoPauseAndHistorySelectionUseTheSameLiveStepAndKeepReasonsVisible()
        {
            CommitAndPause();
            Click(battle.StepButton);
            battle.IntervalInput.text = "0.05";
            Click(battle.AutoButton);
            float deadline = Time.realtimeSinceStartup + 5;
            while (panel.Workbench.GetBattleHistory().Length < 3 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(panel.Workbench.GetBattleHistory().Length, Is.GreaterThanOrEqualTo(3));
            Click(battle.PauseButton);
            string paused = BattleState();
            int prefixCount = panel.Workbench.GetBattleHistory().Length;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(BattleState(), Is.EqualTo(paused), "Pause must stop actual settlement.");
            var expected = new CombatSession(panel.Workbench.GetCommittedScenario());
            for (int index = 0; index < prefixCount; index++) expected.Step();
            Assert.That(Signature(panel.Workbench.GetBattleSnapshot()), Is.EqualTo(Signature(expected.GetSnapshot())));
            EnsureVisible(battle.EventDropdown);
            battle.EventDropdown.value = 0;
            var first = panel.Workbench.GetBattleHistory()[0];
            Assert.That(battle.SelectedEventIndex, Is.Zero);
            Assert.That(battle.HighlightedActorId, Is.EqualTo(first.actorId));
            Assert.That(battle.HighlightedTargetId, Is.EqualTo(first.targetId));
            Assert.That(battle.ReasonText.text, Does.Contain(first.attempts[0].reason));
            var candidate = first.attempts.SelectMany(attempt => attempt.candidates).First(value => !string.IsNullOrEmpty(value.reason));
            Assert.That(battle.ReasonText.text, Does.Contain(candidate.reason));
            Assert.That(BattleState(), Is.EqualTo(paused), "Selecting historical reasons is read only.");
            AssertCardsShowActualNumbers();
            Click(battle.AutoButton);
            deadline = Time.realtimeSinceStartup + 5;
            while (!panel.Workbench.GetBattleSnapshot().finished && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(panel.Workbench.GetBattleSnapshot().finished, Is.True);
            var full = new CombatSession(panel.Workbench.GetCommittedScenario()).RunToCompletion();
            Assert.That(Signature(panel.Workbench.GetBattleSnapshot()), Is.EqualTo(Signature(full.snapshot)));
            Assert.That(Signature(panel.Workbench.GetBattleHistory()), Is.EqualTo(Signature(full.steps)));
            Click(panel.ReplayButton);
            Click(battle.AutoButton);
            Click(Nav("Nav_Expedition"));
            Assert.That(battle.IsAutoPlaying, Is.False, "Leaving the page pauses the live scheduler.");
            string away = BattleState();
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(BattleState(), Is.EqualTo(away));
        }

        [UnityTest]
        public IEnumerator OneRuleComparisonAndExportPreserveLoadedDraftCommittedAndRawFormInputs()
        {
            CommitAndPause();
            Click(battle.StepButton);
            SelectRule(0);
            editor.Terms[0].ThresholdInput.text = "0.10";
            Click(panel.ReplaceButton);
            var before = BattleState();
            Click(panel.CompareButton);
            Assert.That(panel.LastComparison.success, Is.True, panel.LastComparison.message);
            Assert.That(panel.LastComparison.comparison.baseline.time, Is.EqualTo(135));
            Assert.That(panel.LastComparison.comparison.variant.time, Is.EqualTo(75));
            Assert.That(panel.LastComparison.comparison.firstDivergence, Is.Not.Null);
            Assert.That(panel.IsComparisonVisible, Is.True);
            Assert.That(BattleState(), Is.EqualTo(before));
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(panel.ComparisonText, panel.ComparisonScroll.viewport, "加载配置结束 WAIT 135");
            AssertVisibleToken(panel.ComparisonText, panel.ComparisonScroll.viewport, "当前战术结束 WAIT 75");
            Click(panel.BackToBattleButton);
            editor.Terms[0].ThresholdInput.text = "0.20";
            Assert.That(editor.HasFormChanges, Is.True);
            var destination = Path.Combine(Path.GetTempPath(), "Prototype-Tactics-PlayMode-" + Guid.NewGuid().ToString("N"));
            panel.ReportDirectoryOverride = destination;
            Assert.That(Directory.Exists(destination), Is.False);
            var accepted = panel.Workbench.GetAcceptance();
            Click(panel.ExportButton);
            Assert.That(panel.LastExportError, Is.Null, panel.LastExportError);
            string directory = panel.LastExportDirectory;
            Assert.That(Path.GetFullPath(directory).StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            string serialized = File.ReadAllText(Path.Combine(directory, "session_result.json"));
            Assert.That(serialized, Is.EqualTo(JsonUtility.ToJson(accepted, true)),
                "The complete file must equal the actual paused session serialized by the exporter; optional null strings may normalize on decode.");
            var actual = JsonUtility.FromJson<TacticsWorkbenchAcceptance>(serialized);
            Assert.That(actual.hasUncommittedChanges, Is.True);
            Assert.That(actual.draftRevision, Is.EqualTo(accepted.draftRevision));
            Assert.That(actual.committedRevision, Is.EqualTo(accepted.committedRevision));
            Assert.That(Mage(actual.loadedScenario).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.35));
            Assert.That(Mage(actual.draftScenario).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.10));
            Assert.That(Mage(actual.committedScenario).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.35));
            Assert.That(Mage(actual.loadedScenario).availableActionIdsExplicit, Is.EqualTo(Mage(accepted.loadedScenario).availableActionIdsExplicit));
            Assert.That(Mage(actual.draftScenario).availableActionIdsExplicit, Is.EqualTo(Mage(accepted.draftScenario).availableActionIdsExplicit));
            Assert.That(Mage(actual.committedScenario).availableActionIdsExplicit, Is.EqualTo(Mage(accepted.committedScenario).availableActionIdsExplicit));
            Assert.That(actual.battleHistory.Length, Is.EqualTo(accepted.battleHistory.Length));
            var exportedEvent = actual.battleHistory.Single();
            var sourceEvent = accepted.battleHistory.Single();
            Assert.That(exportedEvent.time, Is.EqualTo(sourceEvent.time));
            Assert.That(exportedEvent.actionId, Is.EqualTo(sourceEvent.actionId));
            Assert.That(exportedEvent.targetId, Is.EqualTo(sourceEvent.targetId));
            Assert.That(exportedEvent.attempts.Select(attempt => attempt.reason), Is.EqualTo(sourceEvent.attempts.Select(attempt => attempt.reason)));
            Assert.That(exportedEvent.attempts.SelectMany(attempt => attempt.candidates).Select(candidate => candidate.reason),
                Is.EqualTo(sourceEvent.attempts.SelectMany(attempt => attempt.candidates).Select(candidate => candidate.reason)));
            var form = JsonUtility.FromJson<TacticsFormInput>(File.ReadAllText(Path.Combine(directory, "instruction_form.json")));
            Assert.That(form.hasUnsubmittedChanges, Is.True);
            Assert.That(form.terms[0].thresholdText, Is.EqualTo("0.20"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "tactics_report.txt")), Is.EqualTo(accepted.reportText));
            foreach (string name in new[] { "loaded_scenario.json", "tactics_input.json", "committed_scenario.json", "catalog_snapshot.json", "battle_report.txt", "export_manifest.json" })
                Assert.That(File.Exists(Path.Combine(directory, name)), Is.True, name);
            Assert.That(BattleState(), Is.EqualTo(before));
            TestContext.Progress.WriteLine("Actual tactics button export: " + directory);
        }

        [UnityTest]
        public IEnumerator SendButtonUsesExistingPurchasedCampThenRealDispatchAndRejectsLateImport()
        {
            Click(Nav("Nav_Expedition"));
            Click(hub.ExpeditionView.ExpeditionPurchaseButton);
            Assert.That(hub.ExpeditionView.Snapshot.preparation.treasuryCash, Is.EqualTo(88));
            Click(hub.TacticsNavButton);
            string money = Signature(hub.ExpeditionView.Snapshot.preparation);
            Click(panel.SendExpeditionButton);
            Assert.That(panel.LastSendResult.success, Is.True, panel.LastSendResult.message);
            Assert.That(hub.CurrentModule, Is.EqualTo("Expedition"));
            Assert.That(Signature(hub.ExpeditionView.Snapshot.preparation), Is.EqualTo(money));
            foreach (var encounter in hub.ExpeditionSession.GetCombatConfiguration().encounters)
                Assert.That(Signature(Mage(encounter.scenario).instructions), Is.EqualTo(Signature(Mage().instructions)));
            Click(hub.ExpeditionView.ExpeditionDispatchButton);
            Assert.That(hub.ExpeditionView.LastAdvance.success, Is.True, hub.ExpeditionView.LastAdvance.reason);
            var dispatched = hub.ExpeditionView.Snapshot;
            Assert.That(dispatched.report.encounters.SelectMany(value => value.steps).Any(value => value.status == "Executed" && value.actionId.StartsWith("tactics_", StringComparison.Ordinal)), Is.True);
            Assert.That(dispatched.preparation.treasuryCash, Is.EqualTo(88 + dispatched.report.publicCash));
            var ledger = hub.ExpeditionSession.ExportLedgerCsv();
            Click(hub.ExpeditionView.ExpeditionReplaySettlementButton);
            Assert.That(hub.ExpeditionView.LastCommand.isReplay, Is.True);
            Assert.That(hub.ExpeditionSession.ExportLedgerCsv(), Is.EqualTo(ledger));
            Click(hub.TacticsNavButton);
            var state = Signature(hub.ExpeditionView.Snapshot.preparation) + hub.ExpeditionSession.ExportLedgerCsv();
            Click(panel.SendExpeditionButton);
            Assert.That(panel.LastSendResult.success, Is.False);
            Assert.That(panel.LastSendResult.code, Is.EqualTo("CampRequired"));
            Assert.That(hub.CurrentModule, Is.EqualTo("Tactics"));
            Assert.That(Signature(hub.ExpeditionView.Snapshot.preparation) + hub.ExpeditionSession.ExportLedgerCsv(), Is.EqualTo(state));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScenarioDropdownLoadsActualIndependentExamplesWithoutHiddenCombat()
        {
            Assert.That(panel.ScenarioDropdown.options.Count, Is.GreaterThanOrEqualTo(3));
            foreach (string path in Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat"), "round3_*.json"))
            {
                var example = JsonUtility.FromJson<CombatScenario>(File.ReadAllText(path));
                int index = panel.ScenarioDropdown.options.FindIndex(option => option.text == example.title);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), example.id);
                EnsureVisible(panel.ScenarioDropdown);
                panel.ScenarioDropdown.value = index;
                Assert.That(panel.LastError, Is.Null, panel.LastError);
                Assert.That(panel.Workbench.GetBattleSnapshot(), Is.Null);
                Assert.That(panel.Workbench.GetBattleHistory(), Is.Empty);
                Assert.That(panel.Workbench.GetDraftScenario().seed, Is.EqualTo(20261114));
                Assert.That(panel.Workbench.GetDraftScenario().id, Is.EqualTo(example.id));
                var core = new CombatSession(panel.Workbench.GetDraftScenario());
                Assert.That(Signature(editor.Catalog.actions), Is.EqualTo(Signature(core.GetAvailableActions(panel.SelectedUnitId))));
            }
            yield return null;
        }

        private void CommitAndPause()
        {
            Click(panel.ApplyBattleButton);
            Assert.That(panel.LastApplyResult.success, Is.True, panel.LastApplyResult.message);
            Assert.That(battle.IsAutoPlaying, Is.True);
            Click(battle.PauseButton);
        }

        private void FillBlankFourParts()
        {
            Choose(editor.OperationDropdown, editor.Catalog.operations, "all");
            Click(editor.AddConditionButton);
            Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, "always");
            Choose(editor.ScopeDropdown, editor.Catalog.scopes, "enemies");
            Choose(editor.SelectorDropdown, editor.Catalog.selectors, "highest_hp");
            var free = editor.Catalog.actions.Select((action, index) => new { action, index }).First(value => value.action.effectKind == "damage" && value.action.mpCost == 0);
            EnsureVisible(editor.ActionDropdown);
            editor.ActionDropdown.value = free.index + 1;
        }

        private void SelectUnit(string id)
        {
            var scenario = panel.Workbench.GetDraftScenario();
            var friends = scenario.units.Where(unit => unit.team == scenario.playerTeam).ToArray();
            EnsureVisible(editor.UnitDropdown);
            editor.UnitDropdown.value = Array.FindIndex(friends, unit => unit.id == id);
        }

        private void SelectRule(int index)
        {
            EnsureVisible(editor.InstructionDropdown);
            editor.InstructionDropdown.value = index + 1;
        }

        private void Choose(TMP_Dropdown dropdown, CombatTacticsOption[] options, string id)
        {
            int index = Array.FindIndex(options, option => option.id == id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
            EnsureVisible(dropdown);
            dropdown.value = index + 1;
        }

        private void Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name);
            Assert.That(button.interactable, Is.True, button.name);
            EnsureVisible(button);
            button.onClick.Invoke();
        }

        private void EnsureVisible(Selectable control)
        {
            Canvas.ForceUpdateCanvases();
            var scroll = control.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content != null && control.transform.IsChildOf(scroll.content))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4];
                ((RectTransform)control.transform).GetWorldCorners(corners);
                float bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
                float top = scroll.viewport.InverseTransformPoint(corners[2]).y;
                float shift = top > scroll.viewport.rect.yMax - 2 ? scroll.viewport.rect.yMax - 2 - top
                    : bottom < scroll.viewport.rect.yMin + 2 ? scroll.viewport.rect.yMin + 2 - bottom : 0;
                scroll.content.anchoredPosition += new Vector2(0, shift);
                Canvas.ForceUpdateCanvases();
            }
            AssertRectInsideCanvas((RectTransform)control.transform, control.name);
        }

        private void AssertFixedControlsInsideCanvas()
        {
            Canvas.ForceUpdateCanvases();
            foreach (var control in hub.GetComponentsInChildren<Selectable>(false))
            {
                var scroll = control.GetComponentInParent<ScrollRect>();
                if (scroll == null || scroll.content == null || !control.transform.IsChildOf(scroll.content))
                    AssertRectInsideCanvas((RectTransform)control.transform, control.name);
            }
        }

        private void AssertRectInsideCanvas(RectTransform rect, string name)
        {
            var bounds = new Vector3[4];
            ((RectTransform)hub.GetComponentInChildren<Canvas>().transform).GetWorldCorners(bounds);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(bounds[0].x - 2), name);
            Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(bounds[0].y - 2), name);
            Assert.That(corners[2].x, Is.LessThanOrEqualTo(bounds[2].x + 2), name);
            Assert.That(corners[2].y, Is.LessThanOrEqualTo(bounds[2].y + 2), name);
        }

        private void AssertCardsShowActualNumbers()
        {
            var snapshot = panel.Workbench.GetBattleSnapshot();
            foreach (var unit in snapshot.units)
            {
                var cards = unit.team == battle.FriendlyTeam ? battle.FriendlyCellTexts : battle.EnemyCellTexts;
                var card = cards[unit.row * 3 + unit.column];
                AssertVisibleToken(card, card.rectTransform, unit.name);
                AssertVisibleToken(card, card.rectTransform, "HP " + unit.hp + "/" + unit.maxHp);
                AssertVisibleToken(card, card.rectTransform, "MP " + unit.mp + "/" + unit.maxMp);
            }
        }

        private static void AssertVisibleToken(TMP_Text text, RectTransform clip, string token)
        {
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            int start = text.text.IndexOf(token, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), token);
            int visible = 0;
            for (int index = 0; index < text.textInfo.characterCount; index++)
            {
                var glyph = text.textInfo.characterInfo[index];
                if (!glyph.isVisible || glyph.index < start || glyph.index >= start + token.Length) continue;
                visible++;
                var bottom = clip.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                var top = clip.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                Assert.That(bottom.x, Is.GreaterThanOrEqualTo(clip.rect.xMin - 2), token);
                Assert.That(bottom.y, Is.GreaterThanOrEqualTo(clip.rect.yMin - 2), token);
                Assert.That(top.x, Is.LessThanOrEqualTo(clip.rect.xMax + 2), token);
                Assert.That(top.y, Is.LessThanOrEqualTo(clip.rect.yMax + 2), token);
            }
            Assert.That(visible, Is.EqualTo(token.Count(character => !char.IsWhiteSpace(character))), "Every critical glyph must render: " + token);
        }

        private CombatUnitConfig Mage() { return Mage(panel.Workbench.GetDraftScenario()); }
        private static CombatUnitConfig Mage(CombatScenario scenario) { return scenario.units.Single(unit => unit.id == "mage"); }
        private Button Nav(string name) { return hub.GetComponentsInChildren<Button>(true).Single(button => button.name == name); }
        private string BattleState() { return Signature(panel.Workbench.GetBattleSnapshot()) + Signature(panel.Workbench.GetBattleHistory()); }
        private static string Signature(object value)
        {
            if (value == null) return "null";
            if (value is string) return "string:" + ((string)value).Length + ":" + value;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal) return type.Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
            var array = value as Array;
            if (array != null) return "[" + string.Join("|", array.Cast<object>().Select(Signature)) + "]";
            return type.FullName + "{" + string.Join("|", type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => field.Name + "=" + Signature(field.GetValue(value)))) + "}";
        }
    }
}
