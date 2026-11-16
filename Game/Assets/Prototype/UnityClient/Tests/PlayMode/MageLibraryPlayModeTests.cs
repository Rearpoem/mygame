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
    public sealed class MageLibraryPlayModeTests
    {
        private PrototypeHubController hub;
        private TacticsPanelController panel;
        private TacticsInstructionEditor editor;
        private TacticsBattleView battle;

        [UnitySetUp]
        public IEnumerator CreateRealDefaultMageLibrary()
        {
            hub = PrototypeHubFactory.Create(TMP_Settings.defaultFontAsset);
            Assert.That(hub.TacticsCandidates.SelectCandidate("legacy"), Is.True);
            panel = hub.TacticsView;
            Assert.That(panel.IsConfigured, Is.True, panel.LastError);
            Assert.That(hub.CurrentModule, Is.EqualTo("Tactics"));
            Assert.That(panel.Workbench.GetDraftScenario().id, Is.EqualTo("round4-mage-library"));
            editor = panel.InstructionEditor;
            battle = panel.BattleView;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyHub()
        {
            if (battle != null) battle.Pause();
            if (hub != null) UnityEngine.Object.Destroy(hub.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SearchBuildMeditationRestoreVisibleMpAndExportTheExactActualRecord()
        {
            SelectRule(0);
            Assert.That(editor.SelectedActionId, Is.EqualTo("mage_meditate"));
            var beforeSearch = Signature(panel.Workbench.GetDraftScenario());
            editor.ActionSearchInput.text = "不匹配任何技能的检索";
            Assert.That(editor.SelectedActionId, Is.EqualTo("mage_meditate"));
            Assert.That(editor.VisibleActions.Any(action => action.id == "mage_meditate"), Is.True);
            Assert.That(editor.HasFormChanges, Is.False, "Search navigates the catalog, not the rule.");
            Assert.That(Signature(panel.Workbench.GetDraftScenario()), Is.EqualTo(beforeSearch));
            Click(panel.NewBlankButton);
            Choose(editor.OperationDropdown, editor.Catalog.operations, "all");
            Click(editor.AddConditionButton);
            Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, "mp_ratio_at_most");
            Choose(editor.Terms[0].SubjectDropdown, editor.Catalog.subjects, "actor");
            editor.Terms[0].ThresholdInput.text = "0.5";
            Choose(editor.ScopeDropdown, editor.Catalog.scopes, "self");
            Choose(editor.SelectorDropdown, editor.Catalog.selectors, "configured_order");
            SearchAndSelect("冥想", "mage_meditate");
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            Assert.That(editor.ActionDetailsText.text, Does.Contain("MP 费用 0"));
            Assert.That(editor.ActionDetailsText.text, Does.Contain("基础 WAIT 60"));
            Assert.That(editor.ActionDetailsText.text, Does.Contain("restore_mp"));
            Assert.That(editor.ActionDetailsText.text, Does.Contain("isMagic：否"));
            RevealText(editor.ActionDetailsText, editor.FormScroll, "MP 费用 0");
            CommitPauseStep();
            var step = panel.Workbench.GetBattleHistory().Single();
            Assert.That(step.actionId, Is.EqualTo("mage_meditate"));
            Assert.That(step.effects.Single().mpBefore, Is.EqualTo(4));
            Assert.That(step.effects.Single().mpAfter, Is.EqualTo(16));
            Assert.That(step.effects.Single().mpDelta, Is.EqualTo(12));
            Assert.That(battle.HighlightedTargetIds, Is.EqualTo(step.targetIds));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain("MP 4→16"));
            var mage = panel.Workbench.GetBattleSnapshot().units.Single(unit => unit.id == "mage");
            var mageCard = battle.FriendlyCellTexts[mage.row * 3 + mage.column];
            AssertVisibleToken(mageCard, mageCard.rectTransform, "MP 16/30");
            var enemy = panel.Workbench.GetBattleSnapshot().units.Single(unit => unit.id == "enemy_silencer");
            var enemyCard = battle.EnemyCellTexts[enemy.row * 3 + enemy.column];
            AssertVisibleToken(enemyCard, enemyCard.rectTransform, "HP " + enemy.hp + "/" + enemy.maxHp);
            AssertVisibleToken(enemyCard, enemyCard.rectTransform, "MP " + enemy.mp + "/" + enemy.maxMp);
            Click(battle.EnemyCellButtons[enemy.row * 3 + enemy.column]);
            Assert.That(battle.ReasonText.text, Does.Contain(enemy.name), "Clicking a shortened card reveals its full real name.");
            string reportRoot = Path.Combine(Path.GetTempPath(), "Prototype-Mage-PlayMode-" + Guid.NewGuid().ToString("N"));
            panel.ReportDirectoryOverride = reportRoot;
            var accepted = panel.Workbench.GetAcceptance();
            Click(panel.ExportButton);
            Assert.That(panel.LastExportError, Is.Null, panel.LastExportError);
            string serialized = File.ReadAllText(Path.Combine(panel.LastExportDirectory, "session_result.json"));
            Assert.That(serialized, Is.EqualTo(JsonUtility.ToJson(accepted, true)));
            var decoded = JsonUtility.FromJson<TacticsWorkbenchAcceptance>(serialized);
            Assert.That(decoded.battleHistory.Single().effects.Single().mpDelta, Is.EqualTo(12));
            Assert.That(decoded.battleHistory.Single().targetIds, Is.EqualTo(new[] { "mage" }));
            var catalog = JsonUtility.FromJson<TacticsCatalogSnapshot>(File.ReadAllText(Path.Combine(panel.LastExportDirectory, "catalog_snapshot.json")));
            Assert.That(catalog.statusKinds, Is.Not.Empty);
            Assert.That(catalog.targetModes.Select(option => option.id), Does.Contain("all"));
            Assert.That(catalog.effectKinds.Select(option => option.id), Does.Contain("restore_mp"));
            Assert.That(catalog.conditions.Single(option => option.id == "mp_at_most").thresholdUnit, Is.EqualTo("points"));
            Assert.That(catalog.conditions.Single(option => option.id == "has_status_kind").defaultValue, Is.Not.Empty);
            TestContext.Progress.WriteLine("Actual mage UI export: " + panel.LastExportDirectory);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AreaAndDrainViewsShowAllEffectsButHighlightOnlyActualPrimaryCoverage()
        {
            LoadCase("round4_area.json");
            SelectRule(0);
            SearchAndSelect("全体火雨", "mage_fire_rain");
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            Assert.That(editor.ActionDetailsText.text, Does.Contain("MP 费用与 WAIT 各结算一次"));
            Click(panel.CompareButton);
            Assert.That(panel.LastComparison.success, Is.True, panel.LastComparison.message);
            Assert.That(panel.LastComparison.comparison.firstDivergence.variant.targetIds.Length, Is.EqualTo(3));
            Click(panel.BackToBattleButton);
            CommitPauseStep();
            var area = panel.Workbench.GetBattleHistory().Single();
            Assert.That(area.targetIds.Length, Is.EqualTo(3));
            Assert.That(area.effects.Length, Is.EqualTo(3));
            Assert.That(battle.HighlightedTargetIds, Is.EqualTo(area.targetIds));
            foreach (var effect in area.effects)
            {
                Assert.That(battle.SelectedEffectsSummary, Does.Contain("[" + effect.targetId + "]"));
                Assert.That(battle.SelectedEffectsSummary, Does.Contain("HP " + effect.hpBefore + "→" + effect.hpAfter));
                Assert.That(battle.SelectedEffectsSummary, Does.Contain("MP " + effect.mpBefore + "→" + effect.mpAfter));
            }
            Assert.That(panel.Workbench.GetBattleSnapshot().units.Single(unit => unit.id == "mage").mp, Is.EqualTo(26));
            LoadCase("round4_mage_library.json");
            SelectRule(0);
            Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, "always");
            Choose(editor.ScopeDropdown, editor.Catalog.scopes, "enemies");
            Choose(editor.SelectorDropdown, editor.Catalog.selectors, "highest_mp");
            SearchAndSelect("汲取魔力", "mage_mana_drain");
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            CommitPauseStep();
            var drain = panel.Workbench.GetBattleHistory().Single();
            Assert.That(drain.effects.Length, Is.EqualTo(2));
            Assert.That(drain.targetIds, Is.EqualTo(new[] { "enemy_silencer" }));
            Assert.That(battle.HighlightedTargetIds, Is.EqualTo(drain.targetIds));
            Assert.That(battle.HighlightedTargetIds, Does.Not.Contain("mage"));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain("附加效果（非主覆盖）"));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain("[mage]"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CleanseAndControlHistoryDisplaysStructuredRemovalTimingAndControlCodes()
        {
            LoadCase("round4_cleanse.json");
            SelectRule(0);
            editor.Terms[0].ThresholdInput.text = "1";
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            CommitPauseStep();
            var cleanse = panel.Workbench.GetBattleHistory().Single();
            Assert.That(cleanse.actionId, Is.EqualTo("mage_cleanse"));
            Assert.That(cleanse.effects[0].removedStatusIds, Is.Not.Empty);
            foreach (string status in cleanse.effects[0].removedStatusIds)
                Assert.That(battle.SelectedEffectsSummary, Does.Contain(status));
            Assert.That(battle.HighlightedTargetIds, Is.EqualTo(cleanse.targetIds));
            LoadCase("round4_control.json");
            SelectRule(0);
            editor.Terms[0].ThresholdInput.text = "1";
            Click(panel.ReplaceButton);
            Assert.That(panel.LastEditResult.success, Is.True, panel.LastEditResult.message);
            CommitPauseStep();
            var applied = panel.Workbench.GetBattleHistory().Single();
            Assert.That(applied.actionId, Is.EqualTo("mage_freeze"));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain(applied.effects[0].statusId));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain("到期 " + Number(applied.effects[0].statusExpiresAt)));
            for (int index = 0; index < 10 && panel.Workbench.GetBattleHistory().Last().controlReason != "Stunned"; index++)
                Click(battle.StepButton);
            var skipped = panel.Workbench.GetBattleHistory().Last();
            Assert.That(skipped.status, Is.EqualTo("Skipped"));
            Assert.That(skipped.controlReason, Is.EqualTo("Stunned"));
            Assert.That(battle.SelectedEffectsSummary, Does.Contain("Stunned"));
            string before = Signature(panel.Workbench.GetBattleSnapshot()) + Signature(panel.Workbench.GetBattleHistory());
            EnsureVisible(battle.EventDropdown);
            battle.EventDropdown.value = 0;
            Assert.That(battle.SelectedEffectsSummary, Does.Contain(applied.effects[0].statusId));
            Assert.That(Signature(panel.Workbench.GetBattleSnapshot()) + Signature(panel.Workbench.GetBattleHistory()), Is.EqualTo(before));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExplicitConditionUnitsAndImportedMageSkillsKeepRealValuesAndPurchasedCampState()
        {
            SelectRule(0);
            var representatives = new[]
            {
                new[] { "mp_at_most", "points", "7", "点数" },
                new[] { "team_alive_at_least", "count", "2", "人数" },
                new[] { "wait_remaining_at_most", "wait", "30", "WAIT" }
            };
            foreach (var sample in representatives)
            {
                Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, sample[0]);
                editor.Terms[0].ThresholdInput.text = sample[2];
                Assert.That(editor.Catalog.conditions.Single(option => option.id == sample[0]).thresholdUnit, Is.EqualTo(sample[1]));
                Assert.That(editor.Terms[0].ThresholdLabel.text, Does.Contain(sample[3]));
                Assert.That(editor.Terms[0].ThresholdLabel.text, Does.Not.Contain("%"));
                CombatInstruction rule;
                string error;
                Assert.That(editor.TryReadInstruction(out rule, out error), Is.True, error);
                Assert.That(rule.condition.terms[0].threshold, Is.EqualTo(double.Parse(sample[2], CultureInfo.InvariantCulture)));
            }
            Choose(editor.Terms[0].KindDropdown, editor.Catalog.conditions, "has_status_kind");
            Assert.That(editor.Terms[0].ValueInput.text, Is.EqualTo(editor.Catalog.conditions.Single(option => option.id == "has_status_kind").defaultValue));
            Click(editor.LoadSelectedButton);
            Assert.That(editor.HasFormChanges, Is.False);
            var source = new CombatSession(panel.Workbench.GetDraftScenario());
            Click(Nav("Nav_Expedition"));
            Click(hub.ExpeditionView.ExpeditionPurchaseButton);
            string money = Signature(hub.ExpeditionView.Snapshot.preparation);
            Click(hub.TacticsNavButton);
            Click(panel.SendExpeditionButton);
            Assert.That(panel.LastSendResult.success, Is.True, panel.LastSendResult.message);
            Assert.That(hub.CurrentModule, Is.EqualTo("Expedition"));
            Assert.That(Signature(hub.ExpeditionView.Snapshot.preparation), Is.EqualTo(money));
            foreach (var encounter in hub.ExpeditionSession.GetCombatConfiguration().encounters)
                foreach (var definition in source.GetAvailableActions("mage"))
                    Assert.That(Signature(encounter.scenario.actions.Single(action => action.id == definition.id)), Is.EqualTo(Signature(definition)));
            Click(hub.ExpeditionView.ExpeditionNextButton);
            Assert.That(hub.ExpeditionView.LastAdvance.success, Is.True, hub.ExpeditionView.LastAdvance.reason);
            var actual = hub.ExpeditionView.Snapshot;
            var first = actual.report.encounters.Single();
            Assert.That(first.battleStart.units.Single(unit => unit.id == "mage").mp, Is.EqualTo(30));
            Assert.That(first.battleStart.units.Single(unit => unit.id == "ally").hp, Is.EqualTo(20));
            Assert.That(first.steps.Any(step => step.status == "Executed" && step.actionId.StartsWith("mage_", StringComparison.Ordinal)), Is.True);
            Assert.That(actual.preparation.treasuryCash, Is.EqualTo(88 + first.publicCash));
            Assert.That(actual.preparation.residentCash, Is.EqualTo(32));
            yield return null;
        }

        private void LoadCase(string file)
        {
            var scenario = JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", file)));
            int index = panel.ScenarioDropdown.options.FindIndex(option => option.text == scenario.title);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), file);
            EnsureVisible(panel.ScenarioDropdown);
            panel.ScenarioDropdown.value = index;
            Assert.That(panel.Workbench.GetDraftScenario().id, Is.EqualTo(scenario.id));
        }

        private void SelectRule(int index)
        {
            EnsureVisible(editor.InstructionDropdown);
            editor.InstructionDropdown.value = index + 1;
        }

        private void SearchAndSelect(string query, string actionId)
        {
            EnsureVisible(editor.ActionSearchInput);
            editor.ActionSearchInput.text = query;
            int index = editor.VisibleActions.ToList().FindIndex(action => action.id == actionId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), actionId);
            EnsureVisible(editor.ActionDropdown);
            editor.ActionDropdown.value = index + 1;
            Assert.That(editor.SelectedActionId, Is.EqualTo(actionId));
        }

        private void Choose(TMP_Dropdown dropdown, CombatTacticsOption[] options, string id)
        {
            int index = Array.FindIndex(options, option => option.id == id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
            EnsureVisible(dropdown);
            dropdown.value = index + 1;
        }

        private void CommitPauseStep()
        {
            Click(panel.ApplyBattleButton);
            Assert.That(panel.LastApplyResult.success, Is.True, panel.LastApplyResult.message);
            Click(battle.PauseButton);
            Click(battle.StepButton);
        }

        private void Click(Button button)
        {
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name);
            Assert.That(button.interactable, Is.True, button.name);
            EnsureVisible(button);
            button.onClick.Invoke();
        }

        private static void EnsureVisible(Selectable control)
        {
            Canvas.ForceUpdateCanvases();
            var scroll = control.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || !control.transform.IsChildOf(scroll.content)) return;
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

        private static void RevealText(TMP_Text text, ScrollRect scroll, string token)
        {
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            int start = text.text.IndexOf(token, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), token);
            var first = text.textInfo.characterInfo.First(glyph => glyph.isVisible && glyph.index >= start && glyph.index < start + token.Length);
            float top = scroll.viewport.InverseTransformPoint(text.transform.TransformPoint(first.topRight)).y;
            scroll.content.anchoredPosition += new Vector2(0, scroll.viewport.rect.yMax - 6 - top);
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(text, scroll.viewport, token);
        }

        private static void AssertVisibleToken(TMP_Text text, RectTransform clip, string token)
        {
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            int start = text.text.IndexOf(token, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), token);
            int count = 0;
            for (int index = 0; index < text.textInfo.characterCount; index++)
            {
                var glyph = text.textInfo.characterInfo[index];
                if (!glyph.isVisible || glyph.index < start || glyph.index >= start + token.Length) continue;
                count++;
                var bottom = clip.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                var top = clip.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                Assert.That(bottom.x, Is.GreaterThanOrEqualTo(clip.rect.xMin - 2), token);
                Assert.That(bottom.y, Is.GreaterThanOrEqualTo(clip.rect.yMin - 2), token);
                Assert.That(top.x, Is.LessThanOrEqualTo(clip.rect.xMax + 2), token);
                Assert.That(top.y, Is.LessThanOrEqualTo(clip.rect.yMax + 2), token);
            }
            Assert.That(count, Is.EqualTo(token.Count(character => !char.IsWhiteSpace(character))), token);
        }

        private Button Nav(string name) { return hub.GetComponentsInChildren<Button>(true).Single(button => button.name == name); }
        private static string Number(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string Signature(object value)
        {
            if (value == null) return "null";
            if (value is string) return "string:" + ((string)value).Length + ":" + value;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal) return type.Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
            Array array = value as Array;
            if (array != null) return "[" + string.Join("|", array.Cast<object>().Select(Signature)) + "]";
            return type.FullName + "{" + string.Join("|", type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => field.Name + "=" + Signature(field.GetValue(value)))) + "}";
        }
    }
}
