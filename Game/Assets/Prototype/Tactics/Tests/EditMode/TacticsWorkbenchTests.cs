using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.Expedition;
using UnityEngine;

namespace Prototype.Tactics.Tests
{
    public sealed class TacticsWorkbenchTests
    {
        private CombatScenario loaded;

        [SetUp]
        public void ReadActualRoundThreeScenario()
        {
            loaded = Read<CombatScenario>("Prototype/Combat/round3_tactics.json");
        }

        [Test]
        public void CatalogAndCapacityComeFromTheActualRoleAndKernel()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            var core = new CombatSession(loaded);
            var catalog = workbench.GetCatalog("mage");
            Assert.That(Signature(catalog.actions), Is.EqualTo(Signature(core.GetAvailableActions("mage"))));
            Assert.That(Signature(catalog.conditions), Is.EqualTo(Signature(CombatTacticsCatalog.GetConditions())));
            Assert.That(Signature(catalog.operations), Is.EqualTo(Signature(CombatTacticsCatalog.GetOperations())));
            Assert.That(Signature(catalog.groupings), Is.EqualTo(Signature(CombatTacticsCatalog.GetGroupings())));
            Assert.That(Signature(catalog.subjects), Is.EqualTo(Signature(CombatTacticsCatalog.GetSubjects())));
            var mage = core.GetSnapshot().units.Single(unit => unit.id == "mage");
            Assert.That(catalog.instructionCapacity, Is.EqualTo(mage.instructionCapacity));
            Assert.That(catalog.conditionCapacity, Is.EqualTo(mage.conditionTermCapacity));
            var enemyOnly = core.GetAvailableActions("enemy_caster").Select(action => action.id);
            Assert.That(catalog.actions.Select(action => action.id).Intersect(enemyOnly), Is.Empty);
            Assert.That(workbench.GetBattleSnapshot(), Is.Null);
            Assert.That(workbench.GetBattleHistory(), Is.Empty);
        }

        [Test]
        public void BlankFourPartRuleCannotBeInsertedUntilCompleteThenUsesRealInsert()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            var blank = workbench.CreateBlankInstruction();
            Assert.That(blank.scope, Is.Empty);
            Assert.That(blank.selector, Is.Empty);
            Assert.That(blank.actionId, Is.Empty);
            Assert.That(blank.condition.operation, Is.Empty);
            Assert.That(blank.condition.terms, Is.Empty);
            var before = WorkbenchState(workbench);
            Assert.That(workbench.InsertInstruction("mage", 0, blank).success, Is.False);
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
            FillCompleteRule(workbench, blank, "mage");
            var expected = new CombatSession(loaded);
            Assert.That(expected.InsertInstruction("mage", 0, blank).success, Is.True);
            Assert.That(workbench.InsertInstruction("mage", 0, blank).success, Is.True);
            Assert.That(Signature(workbench.GetDraftScenario()), Is.EqualTo(Signature(expected.GetScenario())));
            Assert.That(workbench.GetSnapshot().draftRevision, Is.EqualTo(1));
            Assert.That(workbench.GetBattleSnapshot(), Is.Null, "Editing is not an implicit battle start.");
            blank.condition.terms[0].kind = "caller-mutated";
            Assert.That(workbench.GetDraftScenario().units.Single(unit => unit.id == "mage").instructions[0].condition.terms[0].kind, Is.EqualTo("always"));
        }

        [Test]
        public void ReplaceRemoveAndMoveAreTheSameEditsAsTheRealKernel()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            var expected = new CombatSession(loaded);
            var replacement = CompleteRule(workbench, "mage");
            Assert.That(workbench.ReplaceInstruction("mage", 1, replacement).success, Is.True);
            expected.ReplaceInstruction("mage", 1, replacement);
            Assert.That(workbench.MoveInstruction("mage", 1, 0).success, Is.True);
            expected.MoveInstruction("mage", 1, 0);
            Assert.That(workbench.RemoveInstruction("mage", 2).success, Is.True);
            expected.RemoveInstruction("mage", 2);
            Assert.That(Signature(workbench.GetDraftScenario()), Is.EqualTo(Signature(expected.GetScenario())));
            Assert.That(Signature(workbench.GetDraftPreview()), Is.EqualTo(Signature(expected.GetSnapshot())));
            Assert.That(workbench.GetSnapshot().draftRevision, Is.EqualTo(3));
        }

        [Test]
        public void CapacityAndSkillFailuresAreAtomicAndExposeTheRealReason()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            int capacity = workbench.GetCatalog("mage").instructionCapacity;
            while (Mage(workbench.GetDraftScenario()).instructions.Length < capacity)
                Assert.That(workbench.InsertInstruction("mage", 0, CompleteRule(workbench, "mage")).success, Is.True);
            var before = WorkbenchState(workbench);
            var rejected = workbench.InsertInstruction("mage", 0, CompleteRule(workbench, "mage"));
            Assert.That(rejected.success, Is.False);
            Assert.That(rejected.code, Is.EqualTo("InstructionCapacityExceeded"));
            Assert.That(rejected.message, Is.Not.Empty);
            Assert.That(rejected.instructionCapacity, Is.EqualTo(capacity));
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
            var enemySkill = new CombatSession(loaded).GetAvailableActions("enemy_caster").First();
            var unavailable = CompleteRule(workbench, "mage");
            unavailable.actionId = enemySkill.id;
            var denied = workbench.ReplaceInstruction("mage", 0, unavailable);
            Assert.That(denied.code, Is.EqualTo("ActionUnavailable"));
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
        }

        [Test]
        public void ExplicitMixedGroupingAndActorSubjectArePassedToTheKernelWithoutOwnEvaluation()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            var rule = CompleteRule(workbench, "mage");
            rule.condition = new CombatCondition
            {
                operation = "mixed", operators = new[] { "and", "or" }, grouping = "",
                terms = new[]
                {
                    new CombatConditionTerm { kind = "mp_ratio_at_least", threshold = 0.75, subject = "actor" },
                    new CombatConditionTerm { kind = "vulnerable", subject = "target" },
                    new CombatConditionTerm { kind = "row_is", threshold = 2, subject = "target" }
                }
            };
            var before = WorkbenchState(workbench);
            Assert.That(workbench.InsertInstruction("mage", 0, rule).code, Is.EqualTo("InvalidCondition"));
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
            rule.condition.grouping = "left";
            Assert.That(workbench.InsertInstruction("mage", 0, rule).success, Is.True);
            var actual = workbench.GetDraftPreview().units.Single(unit => unit.id == "mage").instructions[0].display;
            Assert.That(actual, Does.Contain("施放者"));
            Assert.That(actual, Does.Contain("("));
            Assert.That(actual, Does.Contain(")"));
            Assert.That(actual, Does.Contain(" AND "));
            Assert.That(actual, Does.Contain(" OR "));
            var expected = new CombatSession(loaded);
            expected.InsertInstruction("mage", 0, rule);
            Assert.That(Signature(workbench.GetDraftPreview()), Is.EqualTo(Signature(expected.GetSnapshot())));
            var tooManyForAlly = CompleteRule(workbench, "ally");
            tooManyForAlly.condition = rule.condition;
            var failed = workbench.ReplaceInstruction("ally", 0, tooManyForAlly);
            Assert.That(failed.code, Is.EqualTo("ConditionCapacityExceeded"));
        }

        [Test]
        public void DraftEditingDoesNotTouchLiveBattleAndReplayUsesCommittedRatherThanDraft()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            Assert.That(workbench.StepBattle(), Is.Null);
            Assert.That(workbench.CommitBattle().success, Is.True);
            workbench.StepBattle();
            var beforeSnapshot = Signature(workbench.GetBattleSnapshot());
            var beforeHistory = Signature(workbench.GetBattleHistory());
            var changed = Mage(workbench.GetDraftScenario()).instructions[0];
            changed.condition.terms[0].threshold = 0.10;
            Assert.That(workbench.ReplaceInstruction("mage", 0, changed).success, Is.True);
            Assert.That(Signature(workbench.GetBattleSnapshot()), Is.EqualTo(beforeSnapshot));
            Assert.That(Signature(workbench.GetBattleHistory()), Is.EqualTo(beforeHistory));
            Assert.That(workbench.GetSnapshot().hasUncommittedChanges, Is.True);
            Assert.That(Mage(workbench.GetCommittedScenario()).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.35));
            workbench.ReplayCommitted();
            Assert.That(workbench.GetBattleHistory(), Is.Empty);
            Assert.That(workbench.GetSnapshot().hasUncommittedChanges, Is.True);
            var expected = new CombatSession(workbench.GetCommittedScenario());
            Assert.That(Signature(workbench.StepBattle()), Is.EqualTo(Signature(expected.Step())));
            Assert.That(workbench.CommitBattle().success, Is.True);
            Assert.That(workbench.GetSnapshot().hasUncommittedChanges, Is.False);
            Assert.That(Mage(workbench.GetCommittedScenario()).instructions[0].condition.terms[0].threshold, Is.EqualTo(0.10));
            Assert.That(workbench.GetBattleHistory(), Is.Empty);
        }

        [Test]
        public void EveryStepAndRunToEndProduceIdenticalRealHistoryAndFinalState()
        {
            var stepped = new TacticsWorkbenchSession(loaded);
            var automatic = new TacticsWorkbenchSession(loaded);
            stepped.CommitBattle();
            automatic.CommitBattle();
            while (!stepped.GetBattleSnapshot().finished) stepped.StepBattle();
            var result = automatic.RunBattleToEnd();
            Assert.That(Signature(stepped.GetBattleSnapshot()), Is.EqualTo(Signature(result.snapshot)));
            Assert.That(Signature(stepped.GetBattleHistory()), Is.EqualTo(Signature(result.steps)));
            var terminal = Signature(stepped.GetBattleHistory());
            stepped.StepBattle();
            Assert.That(Signature(stepped.GetBattleHistory()), Is.EqualTo(terminal));
        }

        [Test]
        public void SingleRuleComparisonRunsTheActualTwoInputsWithoutTouchingTheLiveBattle()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            workbench.CommitBattle();
            workbench.StepBattle();
            var changed = Mage(workbench.GetDraftScenario()).instructions[0];
            changed.condition.terms[0].threshold = 0.10;
            workbench.ReplaceInstruction("mage", 0, changed);
            var liveBefore = WorkbenchState(workbench);
            var result = workbench.CompareWithLoaded();
            Assert.That(result.success, Is.True, result.message);
            Assert.That(result.changeKind, Is.EqualTo("Replace"));
            Assert.That(result.comparison.onlyDeclaredSettingChanged, Is.True);
            Assert.That(result.comparison.baseline.time, Is.EqualTo(135));
            Assert.That(result.comparison.variant.time, Is.EqualTo(75));
            Assert.That(result.comparison.firstDivergence, Is.Not.Null);
            Assert.That(result.comparison.firstDivergence.baseline.status, Is.EqualTo("Executed").Or.EqualTo("StateExpired"));
            Assert.That(Signature(result.comparison.baseline), Is.EqualTo(Signature(new CombatSession(loaded).RunToCompletion())));
            Assert.That(Signature(result.comparison.variant), Is.EqualTo(Signature(new CombatSession(workbench.GetDraftScenario()).RunToCompletion())));
            Assert.That(WorkbenchState(workbench), Is.EqualTo(liveBefore));
        }

        [Test]
        public void LabelOrAttemptTextChangesAreNotReportedAsActualActionDivergence()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            var changed = Mage(workbench.GetDraftScenario()).instructions[0];
            changed.label = "说明文字变了，真实行动不变";
            Assert.That(workbench.ReplaceInstruction("mage", 0, changed).success, Is.True);
            var result = workbench.CompareWithLoaded();
            Assert.That(result.success, Is.True, result.message);
            Assert.That(result.comparison.firstDivergence, Is.Null);
            Assert.That(result.comparison.baseline.time, Is.EqualTo(result.comparison.variant.time));
        }

        [Test]
        public void CompareRejectsSeveralRuleChangesAndAcceptsOneMoveOrOneInsertion()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            Assert.That(workbench.CompareWithLoaded().success, Is.False);
            workbench.MoveInstruction("mage", 0, 3);
            Assert.That(workbench.CompareWithLoaded().changeKind, Is.EqualTo("Move"));
            workbench.Reset();
            workbench.InsertInstruction("mage", 0, CompleteRule(workbench, "mage"));
            Assert.That(workbench.CompareWithLoaded().changeKind, Is.EqualTo("Insert"));
            var changed = workbench.GetDraftScenario().units.Single(unit => unit.id == "ally").instructions[0];
            changed.selector = "highest_hp";
            workbench.ReplaceInstruction("ally", 0, changed);
            var failed = workbench.CompareWithLoaded();
            Assert.That(failed.success, Is.False);
            Assert.That(failed.code, Is.EqualTo("SingleInstructionChangeRequired"));
        }

        [Test]
        public void ReadOnlyValidationExplainsLegacyOverloadWithoutChangingAnySessionState()
        {
            Mage(loaded).intelligence = 10;
            var workbench = new TacticsWorkbenchSession(loaded);
            var before = WorkbenchState(workbench);
            var result = workbench.GetInstructionValidation("mage", 3);
            Assert.That(result.success, Is.False);
            Assert.That(result.code, Is.EqualTo("InstructionCapacityExceeded"));
            Assert.That(result.message, Is.Not.Empty);
            Assert.That(workbench.GetDraftPreview().units.Single(unit => unit.id == "mage").instructions[3].capacityAllowed, Is.False);
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
            Assert.That(workbench.RemoveInstruction("mage", 3).success, Is.True);
        }

        [Test]
        public void ReturnedCatalogConfigurationHistoryAndAcceptanceAreDetached()
        {
            var workbench = new TacticsWorkbenchSession(loaded);
            workbench.GetCatalog("mage").actions[0].magnitude = 9999;
            workbench.GetDraftScenario().units[0].hp = 1;
            workbench.CommitBattle();
            workbench.StepBattle();
            var before = WorkbenchState(workbench);
            var acceptance = workbench.GetAcceptance();
            acceptance.battleHistory[0].message = "changed";
            acceptance.draftScenario.units[0].hp = 1;
            acceptance.committedScenario.units[0].mp = 0;
            acceptance.battleSnapshot.units[0].hp = 1;
            Assert.That(WorkbenchState(workbench), Is.EqualTo(before));
            Assert.That(acceptance.reportText, Does.Contain("本场实际 CombatSession 日志"));
        }

        [Test]
        public void ApplyTacticsPreservesPurchasedCampMoneyAndEveryUnrelatedEncounterField()
        {
            var expedition = CreateExpedition();
            Assert.That(expedition.PurchaseAuxiliary("before-import", 2).success, Is.True);
            var moneyBefore = Signature(expedition.GetSnapshot().preparation);
            var before = expedition.GetCombatConfiguration();
            var source = new CombatSession(loaded);
            var applied = expedition.ApplyTactics(source.GetScenario());
            Assert.That(applied.success, Is.True, applied.reason);
            Assert.That(Signature(expedition.GetSnapshot().preparation), Is.EqualTo(moneyBefore));
            var after = expedition.GetCombatConfiguration();
            for (int index = 0; index < after.encounters.Length; index++)
            {
                var old = before.encounters[index].scenario;
                var current = after.encounters[index].scenario;
                foreach (var member in source.GetScenario().units.Where(unit => unit.team == "friendly"))
                {
                    var receiver = current.units.Single(unit => unit.id == member.id);
                    Assert.That(Signature(receiver.instructions), Is.EqualTo(Signature(member.instructions)));
                    Assert.That(receiver.availableActionIds, Is.EqualTo(source.GetAvailableActions(member.id).Select(action => action.id)));
                    foreach (var action in source.GetAvailableActions(member.id))
                        Assert.That(Signature(current.actions.Single(value => value.id == action.id)), Is.EqualTo(Signature(action)));
                    receiver.instructions = old.units.Single(unit => unit.id == member.id).instructions;
                    receiver.availableActionIds = old.units.Single(unit => unit.id == member.id).availableActionIds;
                    receiver.availableActionIdsExplicit = old.units.Single(unit => unit.id == member.id).availableActionIdsExplicit;
                }
                foreach (var action in old.actions)
                    Assert.That(Signature(current.actions.Single(value => value.id == action.id)), Is.EqualTo(Signature(action)), "Shared enemy actions must retain their actual definition.");
                Assert.That(current.actions.Any(action => action.id == "tactics_caster_strike"), Is.False);
                current.actions = old.actions;
            }
            Assert.That(Signature(after), Is.EqualTo(Signature(before)), "Only friendly tactics, role skills and missing action definitions may differ.");
        }

        [Test]
        public void ImportedTacticsRunThroughTheActualExpeditionAndStillPostEveryResultOnlyOnce()
        {
            var expedition = CreateExpedition();
            Assert.That(expedition.ApplyTactics(loaded).success, Is.True);
            Assert.That(expedition.StartExpedition().success, Is.True);
            var expected = new CombatExpeditionSession(expedition.GetAcceptance().combatConfiguration).RunToEnd();
            Assert.That(expedition.RunToEnd().success, Is.True);
            var actual = expedition.GetSnapshot();
            Assert.That(NormalizedReport(actual.report), Is.EqualTo(NormalizedReport(expected)));
            Assert.That(actual.report.encounters.SelectMany(value => value.steps).Any(step => step.status == "Executed" && step.actionId.StartsWith("tactics_", StringComparison.Ordinal)), Is.True);
            Assert.That(actual.preparation.treasuryCash, Is.EqualTo(100 + expected.publicCash));
            var before = ExpeditionState(expedition);
            foreach (var receipt in actual.settlements.Where(value => value.rewardsEligible))
            {
                Assert.That(expedition.RetrySettlement(receipt.resultId).isReplay, Is.True);
                Assert.That(expedition.GetAcceptance().ledger.Count(row => row.operationId == receipt.resultId), Is.EqualTo(1));
            }
            Assert.That(ExpeditionState(expedition), Is.EqualTo(before));
            Assert.That(Signature(expedition.GetSnapshot().experience), Is.EqualTo(Signature(expected.experience)));
        }

        [Test]
        public void SameActionIdWithDifferentDefinitionIsRejectedInsteadOfOverwritingEnemyDamage()
        {
            var conflict = JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(loaded).Replace("\"tactics_basic_attack\"", "\"basic_attack\""));
            var expedition = CreateExpedition();
            var before = ExpeditionState(expedition);
            var result = expedition.ApplyTactics(conflict);
            Assert.That(result.success, Is.False);
            Assert.That(result.code, Is.EqualTo("ActionDefinitionConflict"));
            Assert.That(result.reason, Does.Contain("basic_attack"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(before));
        }

        [Test]
        public void ExplicitEmptyRoleSkillsRemainEmptyAfterImportInsteadOfInferringOldExpeditionSkills()
        {
            var source = CombatSession.CopyScenario(loaded);
            Mage(source).instructions = new CombatInstruction[0];
            Mage(source).availableActionIds = new string[0];
            Mage(source).availableActionIdsExplicit = true;
            source = JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(source));
            var expedition = CreateExpedition();
            var beforeMoney = Signature(expedition.GetSnapshot().preparation);
            var result = expedition.ApplyTactics(source);
            Assert.That(result.success, Is.True, result.reason);
            foreach (var encounter in expedition.GetCombatConfiguration().encounters)
            {
                var member = Mage(encounter.scenario);
                Assert.That(member.instructions, Is.Empty);
                Assert.That(member.availableActionIds, Is.Empty);
                Assert.That(member.availableActionIdsExplicit, Is.True);
                Assert.That(new CombatSession(encounter.scenario).GetAvailableActions("mage"), Is.Empty,
                    "The old R2 heal/attack list must not be inferred after an explicit empty import.");
            }
            Assert.That(Signature(expedition.GetSnapshot().preparation), Is.EqualTo(beforeMoney));
        }

        [Test]
        public void TargetCapacityIsCheckedByItsOwnKernelRatherThanCopyingSourceIntelligence()
        {
            var source = CombatSession.CopyScenario(loaded);
            Mage(source).intelligence = 50;
            var workbench = new TacticsWorkbenchSession(source);
            while (Mage(workbench.GetDraftScenario()).instructions.Length < 6)
                Assert.That(workbench.InsertInstruction("mage", 0, CompleteRule(workbench, "mage")).success, Is.True);
            var expedition = CreateExpedition();
            var before = ExpeditionState(expedition);
            var result = expedition.ApplyTactics(workbench.GetDraftScenario());
            Assert.That(result.success, Is.False);
            Assert.That(result.code, Is.EqualTo("InstructionCapacityExceeded"));
            Assert.That(result.reason, Does.Contain("mage"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(before));
        }

        [Test]
        public void TargetConditionCapacityRejectsTheEntireImportWithoutPartialSkillPublication()
        {
            var source = CombatSession.CopyScenario(loaded);
            source.units.Single(unit => unit.id == "ally").intelligence = 20;
            var workbench = new TacticsWorkbenchSession(source);
            var rule = source.units.Single(unit => unit.id == "ally").instructions[0];
            rule.condition.terms = new[]
            {
                new CombatConditionTerm { kind = "always", subject = "target" },
                new CombatConditionTerm { kind = "hp_ratio_at_least", threshold = 0, subject = "target" }
            };
            Assert.That(workbench.ReplaceInstruction("ally", 0, rule).success, Is.True);
            var expedition = CreateExpedition();
            var before = ExpeditionState(expedition);
            var result = expedition.ApplyTactics(workbench.GetDraftScenario());
            Assert.That(result.success, Is.False);
            Assert.That(result.code, Is.EqualTo("ConditionCapacityExceeded"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(before));
        }

        [Test]
        public void MismatchedFriendlyIdAndAlreadyStartedCampRejectImportWithoutResettingAnything()
        {
            var expedition = CreateExpedition();
            var wrong = CombatSession.CopyScenario(loaded);
            wrong.units.Single(unit => unit.id == "ally").id = "different-person";
            var before = ExpeditionState(expedition);
            Assert.That(expedition.ApplyTactics(wrong).code, Is.EqualTo("FriendlyRosterMismatch"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(before));
            expedition.StartExpedition();
            var started = ExpeditionState(expedition);
            Assert.That(expedition.ApplyTactics(loaded).code, Is.EqualTo("CampRequired"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(started));
            expedition.RunToEnd();
            var completed = ExpeditionState(expedition);
            Assert.That(expedition.ApplyTactics(loaded).code, Is.EqualTo("CampRequired"));
            Assert.That(ExpeditionState(expedition), Is.EqualTo(completed));
        }

        [Test]
        public void ResetRestoresTheOriginalExpeditionAfterAnExplicitImport()
        {
            var expedition = CreateExpedition();
            var original = Signature(expedition.GetCombatConfiguration());
            expedition.ApplyTactics(loaded);
            expedition.Reset();
            Assert.That(Signature(expedition.GetCombatConfiguration()), Is.EqualTo(original));
            Assert.That(expedition.GetSnapshot().preparation.treasuryCash, Is.EqualTo(100));
            Assert.That(expedition.GetSnapshot().preparation.ledgerCount, Is.EqualTo(1));
            Assert.That(expedition.GetSnapshot().experience, Is.Empty);
        }

        private static CombatInstruction CompleteRule(TacticsWorkbenchSession workbench, string unitId)
        {
            var rule = workbench.CreateBlankInstruction();
            FillCompleteRule(workbench, rule, unitId);
            return rule;
        }

        private static void FillCompleteRule(TacticsWorkbenchSession workbench, CombatInstruction rule, string unitId)
        {
            rule.label = "完整四段新规则";
            rule.condition = new CombatCondition { operation = "all", terms = new[] { new CombatConditionTerm { kind = "always", subject = "target" } } };
            rule.scope = "enemies";
            rule.selector = "highest_hp";
            rule.actionId = workbench.GetCatalog(unitId).actions.First(action => action.effectKind == "damage" && action.mpCost == 0).id;
        }

        private static CombatUnitConfig Mage(CombatScenario scenario) { return scenario.units.Single(unit => unit.id == "mage"); }

        private static string WorkbenchState(TacticsWorkbenchSession workbench)
        {
            var snapshot = workbench.GetSnapshot();
            return Signature(snapshot.draftScenario) + Signature(snapshot.draftPreview) + Signature(snapshot.committedScenario)
                + Signature(snapshot.battleSnapshot) + Signature(snapshot.history) + snapshot.draftRevision + ":" + snapshot.committedRevision;
        }

        private static string ExpeditionState(ExpeditionSliceSession session)
        {
            var snapshot = session.GetSnapshot();
            return Signature(session.GetCombatConfiguration()) + Signature(snapshot.preparation) + Signature(snapshot.partyState)
                + Signature(snapshot.experience) + Signature(snapshot.report) + session.ExportLedgerCsv();
        }

        private static string NormalizedReport(CombatExpeditionReport report)
        {
            return JsonUtility.ToJson(report).Replace(report.expeditionId, "<dispatch>");
        }

        private static ExpeditionSliceSession CreateExpedition()
        {
            var configuration = Read<ExpeditionSliceConfig>("Prototype/Expedition/default.json");
            return new ExpeditionSliceSession(configuration, Read<CombatExpeditionConfig>(configuration.combatConfigPath),
                Read<PreparationScenario>(configuration.preparationConfigPath));
        }

        private static T Read<T>(string relativePath)
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

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
