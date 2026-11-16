using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatTacticsTests
    {
        private static CombatScenario Load(string filename = "default.json")
        {
            return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", filename)));
        }

        private static CombatUnitConfig Unit(CombatScenario config, string id)
        {
            return config.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatSnapshot snapshot, string id)
        {
            return snapshot.units.Single(unit => unit.id == id);
        }

        private static CombatConditionTerm Term(string kind, double threshold = 0, string value = null, string subject = null)
        {
            return new CombatConditionTerm { kind = kind, threshold = threshold, value = value, subject = subject };
        }

        private static CombatInstruction Attack(string id, params CombatConditionTerm[] terms)
        {
            return new CombatInstruction { id = id, label = id, scope = "enemies", selector = "configured_order",
                actionId = "basic_attack", condition = new CombatCondition { operation = "all", terms = terms } };
        }

        private static CombatScenario Targeting(CombatInstruction rule)
        {
            CombatScenario scenario = Load();
            Unit(scenario, "mage").instructions = new[] { rule, Unit(scenario, "mage").instructions[1] };
            return scenario;
        }

        [Test]
        public void InsertedInstruction_ChangesTheNextChoiceAndResetPreservesAnIsolatedCopy()
        {
            var session = new CombatSession(Load());
            CombatInstruction incoming = Attack("inserted_attack", Term("always"));
            Assert.That(session.InsertInstruction("mage", 0, incoming).success, Is.True);
            incoming.actionId = "caller-mutated";
            incoming.condition.terms[0].kind = "caller-mutated";
            CombatStepResult first = session.Step();
            Assert.That(first.actionId, Is.EqualTo("basic_attack"));
            Assert.That(first.attempts[0].instructionId, Is.EqualTo("inserted_attack"));
            Assert.That(first.effects[0].hpAfter, Is.EqualTo(17));
            session.Reset();
            Assert.That(session.GetScenario().units[0].instructions[0].actionId, Is.EqualTo("basic_attack"));
            Assert.That(session.Step().attempts[0].instructionId, Is.EqualTo("inserted_attack"));
        }

        [Test]
        public void ReplaceDuringBattle_ChangesLaterChoiceWithoutResettingTimeHpOrMp()
        {
            var session = new CombatSession(Load());
            session.Step(); // Real first aid spends MP and changes a teammate's HP.
            Assert.That(session.ReplaceInstruction("mage", 0, Attack("live_attack", Term("always"))).success, Is.True);
            CombatSnapshot edited = session.GetSnapshot();
            Assert.That(edited.time, Is.EqualTo(0));
            Assert.That(Unit(edited, "mage").mp, Is.EqualTo(24));
            Assert.That(Unit(edited, "ally").hp, Is.EqualTo(65));
            Assert.That(session.GetHistory().Length, Is.EqualTo(1));
            CombatStepResult mage = null;
            for (int i = 0; i < 10 && !session.GetSnapshot().finished; i++)
            {
                CombatStepResult step = session.Step();
                if (step.actorId == "mage") { mage = step; break; }
            }
            Assert.That(mage, Is.Not.Null);
            Assert.That(mage.time, Is.EqualTo(40));
            Assert.That(mage.actionId, Is.EqualTo("basic_attack"));
            Assert.That(mage.attempts[0].instructionId, Is.EqualTo("live_attack"));
            session.Reset();
            Assert.That(session.Step().actionId, Is.EqualTo("basic_attack"));
        }

        [Test]
        public void RemoveAndMove_EditTheStoredPriorityRatherThanOnlyTheDisplay()
        {
            var removed = new CombatSession(Load());
            Assert.That(removed.RemoveInstruction("mage", 0).success, Is.True);
            Assert.That(removed.Step().actionId, Is.EqualTo("basic_attack"));
            removed.Reset();
            Assert.That(Unit(removed.GetScenario(), "mage").instructions.Length, Is.EqualTo(1));
            Assert.That(removed.Step().actionId, Is.EqualTo("basic_attack"));
            var moved = new CombatSession(Load());
            Assert.That(moved.MoveInstruction("mage", 1, 0).success, Is.True);
            Assert.That(moved.Step().actionId, Is.EqualTo("basic_attack"));
            moved.Reset();
            Assert.That(moved.Step().attempts[0].instructionId, Is.EqualTo("fallback_attack"));
        }

        [Test]
        public void RemovingAllRules_DoesNotForgetLearnedSkillsAndAllowsAddingOneBack()
        {
            var session = new CombatSession(Load());
            string[] learned = session.GetAvailableActions("mage").Select(action => action.id).ToArray();
            Assert.That(session.RemoveInstruction("mage", 0).success, Is.True);
            Assert.That(session.RemoveInstruction("mage", 0).success, Is.True);
            Assert.That(session.GetAvailableActions("mage").Select(action => action.id), Is.EqualTo(learned));
            Assert.That(session.InsertInstruction("mage", 0, Attack("new_fallback", Term("always"))).success, Is.True);
            Assert.That(session.Step().actionId, Is.EqualTo("basic_attack"));
        }

        [TestCase("unknown_kind")]
        [TestCase("bad_hp")]
        [TestCase("bad_mp")]
        [TestCase("bad_subject")]
        [TestCase("bad_row")]
        [TestCase("empty_status_id")]
        [TestCase("bad_operation")]
        [TestCase("missing_grouping")]
        [TestCase("bad_selector")]
        [TestCase("bad_scope")]
        [TestCase("duplicate_id")]
        [TestCase("unknown_action")]
        [TestCase("unavailable_action")]
        public void InvalidReplacement_HasNoSideEffectsOnConfigStateOrHistory(string defect)
        {
            var session = new CombatSession(Load("round2_ambush.json"));
            session.Step();
            CombatInstruction rule = Attack("invalid_probe", Term("always"));
            switch (defect)
            {
                case "unknown_kind": rule.condition.terms[0].kind = "unknown"; break;
                case "bad_hp": rule.condition.terms[0] = Term("hp_ratio_at_most", 1.1); break;
                case "bad_mp": rule.condition.terms[0] = Term("mp_ratio_at_least", double.NaN); break;
                case "bad_subject": rule.condition.terms[0].subject = "some_other_actor"; break;
                case "bad_row": rule.condition.terms[0] = Term("row_is", 1.5); break;
                case "empty_status_id": rule.condition.terms[0] = Term("has_status"); break;
                case "bad_operation": rule.condition.operation = "xor"; break;
                case "missing_grouping":
                    rule.condition = new CombatCondition { operation = "mixed", operators = new[] { "or", "and" },
                        terms = new[] { Term("always"), Term("always"), Term("always") } }; break;
                case "bad_selector": rule.selector = "some_selector"; break;
                case "bad_scope": rule.scope = "some_scope"; break;
                case "duplicate_id": rule.id = Unit(session.GetScenario(), "mage").instructions[1].id; break;
                case "unknown_action": rule.actionId = "not_in_scenario"; break;
                case "unavailable_action": rule.actionId = "ambush_strike"; break;
            }
            string configBefore = JsonUtility.ToJson(session.GetScenario());
            string stateBefore = JsonUtility.ToJson(session.GetSnapshot());
            string logBefore = session.ExportLogText();
            CombatEditResult edit = session.ReplaceInstruction("mage", 0, rule);
            Assert.That(edit.success, Is.False, defect);
            Assert.That(edit.code, Is.Not.Empty);
            Assert.That(edit.message, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(session.GetScenario()), Is.EqualTo(configBefore));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot()), Is.EqualTo(stateBefore));
            Assert.That(session.ExportLogText(), Is.EqualTo(logBefore));
        }

        [Test]
        public void InvalidIndicesAndNullRules_DoNotChangeAValidConfiguration()
        {
            var session = new CombatSession(Load());
            string before = JsonUtility.ToJson(session.GetScenario());
            Assert.That(session.InsertInstruction("missing", 0, Attack("a", Term("always"))).code, Is.EqualTo("UnknownUnit"));
            Assert.That(session.InsertInstruction("mage", -1, Attack("a", Term("always"))).code, Is.EqualTo("InvalidIndex"));
            Assert.That(session.ReplaceInstruction("mage", 99, Attack("a", Term("always"))).code, Is.EqualTo("InvalidIndex"));
            Assert.That(session.RemoveInstruction("mage", 99).code, Is.EqualTo("InvalidIndex"));
            Assert.That(session.MoveInstruction("mage", 0, 99).code, Is.EqualTo("InvalidIndex"));
            Assert.That(session.ReplaceInstruction("mage", 0, null).success, Is.False);
            Assert.That(JsonUtility.ToJson(session.GetScenario()), Is.EqualTo(before));
        }

        [TestCase(10, 1, true)]
        [TestCase(10, 2, false)]
        [TestCase(20, 2, true)]
        [TestCase(20, 3, false)]
        [TestCase(40, 3, true)]
        public void ConditionCapacity_IsEnforcedByEditingAndSuccessfulRulesActuallyExecute(int intelligence, int terms, bool allowed)
        {
            CombatScenario config = Load();
            Unit(config, "mage").intelligence = intelligence;
            var session = new CombatSession(config);
            CombatInstruction rule = Attack("capacity_probe", Enumerable.Range(0, terms).Select(i => Term("always")).ToArray());
            CombatEditResult edit = session.ReplaceInstruction("mage", 0, rule);
            Assert.That(edit.success, Is.EqualTo(allowed));
            if (allowed) Assert.That(session.Step().attempts[0].instructionId, Is.EqualTo("capacity_probe"));
            else
            {
                Assert.That(edit.code, Is.EqualTo("ConditionCapacityExceeded"));
                Assert.That(session.Step().actionId, Is.EqualTo("first_aid"));
            }
        }

        [Test]
        public void CapacityLimit_RejectsInsertionButLegacyOverloadCanBeReorderedOrRemoved()
        {
            CombatScenario config = Load();
            Unit(config, "mage").intelligence = 10;
            var session = new CombatSession(config);
            Assert.That(session.InsertInstruction("mage", 2, Attack("extra", Term("always"))).code,
                Is.EqualTo("InstructionCapacityExceeded"));
            Assert.That(Unit(session.GetScenario(), "mage").instructions.Length, Is.EqualTo(2));
            var legacy = new CombatSession(Load("round2_capacity.json"));
            Assert.That(Unit(legacy.GetSnapshot(), "mage").instructions[2].capacityAllowed, Is.False);
            Assert.That(legacy.MoveInstruction("mage", 2, 0).success, Is.True);
            Assert.That(legacy.Step().actionId, Is.EqualTo("first_aid"));
            Assert.That(legacy.RemoveInstruction("mage", 2).success, Is.True);
        }

        [Test]
        public void AvailableActions_ArePerUnitPersistentIndependentAndNotLimitedByCurrentMp()
        {
            CombatScenario config = Load("round2_ambush.json");
            Unit(config, "mage").mp = 0;
            var session = new CombatSession(config);
            CombatActionConfig[] actions = session.GetAvailableActions("mage");
            Assert.That(actions.Select(action => action.id), Is.EquivalentTo(new[] { "first_aid", "basic_attack" }));
            Assert.That(actions.Any(action => action.id == "ambush_strike"), Is.False);
            actions[0].id = "changed";
            actions[1].magnitude = 999;
            Assert.That(session.GetAvailableActions("mage").Select(action => action.id), Is.EquivalentTo(new[] { "first_aid", "basic_attack" }));
            Assert.That(session.GetAvailableActions("mage").Single(action => action.id == "basic_attack").magnitude, Is.EqualTo(18));
            Assert.That(session.GetAvailableActions("missing"), Is.Empty);
            Assert.That(session.InsertInstruction("mage", 0, new CombatInstruction { id = "enemy_skill", scope = "enemies",
                selector = "configured_order", actionId = "ambush_strike", condition = new CombatCondition { operation = "all", terms = new[] { Term("always") } } }).code,
                Is.EqualTo("ActionUnavailable"));
        }

        [Test]
        public void ExplicitEmptySkillWhitelist_DoesNotFallBackToInferringConfiguredSkills()
        {
            CombatScenario config = Load();
            Unit(config, "mage").availableActionIds = new string[0];
            Unit(config, "mage").availableActionIdsExplicit = true;
            var session = new CombatSession(config);
            Assert.That(session.GetAvailableActions("mage"), Is.Empty);
            CombatInstructionSnapshot[] rules = Unit(session.GetSnapshot(), "mage").instructions;
            Assert.That(rules.All(rule => rule.capacityAllowed), Is.True);
            Assert.That(rules.All(rule => !rule.actionAvailable), Is.True);
            Assert.That(session.InsertInstruction("mage", 0, Attack("not_learned", Term("always"))).code,
                Is.EqualTo("ActionUnavailable"));
            CombatStepResult step = session.Step();
            Assert.That(step.status, Is.EqualTo("Skipped"));
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(30));
        }

        [TestCase("legacy_raw")]
        [TestCase("canonical")]
        [TestCase("explicit_empty")]
        [TestCase("explicit_null")]
        public void UnityJsonRoundTrip_PreservesLegacyCanonicalAndExplicitEmptySkillIntent(string mode)
        {
            CombatScenario config = Load();
            if (mode == "explicit_empty" || mode == "explicit_null")
            {
                Unit(config, "mage").availableActionIdsExplicit = true;
                Unit(config, "mage").availableActionIds = mode == "explicit_null" ? null : new string[0];
            }
            var expected = new CombatSession(config);
            CombatScenario transfer = mode == "canonical" ? expected.GetScenario() : config;
            var restored = new CombatSession(JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(transfer)));
            Assert.That(JsonUtility.ToJson(restored.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(expected.GetSnapshot())));
            Assert.That(restored.GetScenario().units.All(unit => unit.availableActionIdsExplicit), Is.True);
            Assert.That(restored.GetAvailableActions("mage").Select(action => action.id),
                Is.EquivalentTo(expected.GetAvailableActions("mage").Select(action => action.id)));
            if (mode.StartsWith("explicit_")) Assert.That(restored.GetAvailableActions("mage"), Is.Empty);
            else Assert.That(restored.GetAvailableActions("mage").Select(action => action.id),
                Is.EquivalentTo(new[] { "first_aid", "basic_attack" }));
        }

        [Test]
        public void TargetAndActorConditions_DoNotReadTheOtherSubjectsResources()
        {
            CombatInstruction rule = Attack("mp_guard", Term("hp_ratio_at_most", 0.35),
                Term("mp_ratio_at_least", 0.5, subject: "actor"));
            CombatScenario config = Targeting(rule);
            Unit(config, "enemy_low").mp = 0;
            CombatStepResult actorGuard = new CombatSession(config).Step();
            Assert.That(actorGuard.attempts[0].outcome, Is.EqualTo("Selected"));
            Assert.That(actorGuard.targetId, Is.EqualTo("enemy_low"));
            rule.condition.terms[1].subject = null;
            CombatStepResult targetGuard = new CombatSession(config).Step();
            Assert.That(targetGuard.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(targetGuard.attempts[0].candidates.All(candidate => !candidate.passesCondition), Is.True);
        }

        [Test]
        public void AndAndOr_AreEvaluatedForEachCandidateWithoutCombiningDifferentEnemies()
        {
            CombatInstruction rule = Attack("same_target", Term("hp_ratio_at_most", 0.35), Term("vulnerable"));
            CombatScenario config = Targeting(rule);
            Unit(config, "enemy_high").vulnerable = true;
            CombatStepResult and = new CombatSession(config).Step();
            Assert.That(and.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(and.attempts[0].candidates.All(candidate => !candidate.passesCondition), Is.True);
            rule.condition.operation = "any";
            CombatStepResult or = new CombatSession(config).Step();
            Assert.That(or.attempts[0].outcome, Is.EqualTo("Selected"));
            Assert.That(or.attempts[0].candidates.Where(candidate => candidate.unitId.StartsWith("enemy_")).All(candidate => candidate.passesCondition), Is.True);
        }

        [Test]
        public void ThreeConditionGrouping_ChangesTheActualSelectedEnemy()
        {
            CombatInstruction rule = Attack("grouped", Term("hp_ratio_at_most", 0.35), Term("vulnerable"), Term("mp_ratio_at_least", 0.5));
            rule.condition.operation = "mixed";
            rule.condition.operators = new[] { "or", "and" };
            rule.condition.grouping = "left";
            CombatScenario config = Targeting(rule);
            Unit(config, "enemy_low").mp = 0;
            Unit(config, "enemy_high").vulnerable = true;
            CombatStepResult left = new CombatSession(config).Step();
            rule.condition.grouping = "right";
            CombatStepResult right = new CombatSession(config).Step();
            Assert.That(left.targetId, Is.EqualTo("enemy_high"), "(true OR false) AND false excludes enemy_low.");
            Assert.That(right.targetId, Is.EqualTo("enemy_low"), "true OR (false AND false) includes enemy_low.");
            Assert.That(left.attempts[0].display, Is.Not.EqualTo(right.attempts[0].display));
            Assert.That(left.effects[0].targetId, Is.Not.EqualTo(right.effects[0].targetId));
        }

        [TestCase("has_status", "enemy_high")]
        [TestCase("lacks_status", "enemy_low")]
        public void StatusIdConditions_ReadTheActualActiveStatus(string kind, string expectedTarget)
        {
            CombatScenario config = Targeting(Attack("status_probe", Term(kind, value: "test_mark")));
            Unit(config, "enemy_high").statuses = new[] { new CombatStatusConfig { id = "test_mark", kind = "guard", magnitude = 1, appliedAt = 0, expiresAt = 50 } };
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo(expectedTarget));
        }

        [Test]
        public void StatusPredicate_StopsMatchingAtItsRealExpiry()
        {
            CombatScenario config = Targeting(Attack("expired_probe", Term("has_status", value: "test_mark")));
            foreach (CombatUnitConfig unit in config.units) unit.initialReadyTime += 20;
            Unit(config, "enemy_high").statuses = new[] { new CombatStatusConfig { id = "test_mark", kind = "guard", magnitude = 1, appliedAt = 0, expiresAt = 10 } };
            var session = new CombatSession(config);
            Assert.That(session.Step().status, Is.EqualTo("StateExpired"));
            CombatStepResult action = session.Step();
            Assert.That(action.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(action.attempts[1].instructionId, Is.EqualTo("fallback_attack"));
        }

        [TestCase("row_is", 1, "enemy_high")]
        [TestCase("row_at_most", 0, "enemy_low")]
        [TestCase("row_at_least", 1, "enemy_high")]
        public void RowConditions_FilterByCandidatePosition(string kind, double row, string expectedTarget)
        {
            CombatScenario config = Targeting(Attack("row_probe", Term(kind, row)));
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo(expectedTarget));
        }

        [TestCase("configured_order", "enemy_low")]
        [TestCase("lowest_hp_ratio", "enemy_low")]
        [TestCase("highest_hp_ratio", "enemy_high")]
        [TestCase("lowest_hp", "enemy_low")]
        [TestCase("highest_hp", "enemy_high")]
        [TestCase("lowest_mp_ratio", "enemy_high")]
        [TestCase("highest_mp_ratio", "enemy_low")]
        [TestCase("front_first", "enemy_low")]
        [TestCase("back_first", "enemy_high")]
        [TestCase("vulnerable_first", "enemy_high")]
        public void PublishedSelectors_ChangeTheRealTarget(string selector, string expectedTarget)
        {
            CombatInstruction rule = Attack("selector_probe", Term("always"));
            rule.selector = selector;
            CombatScenario config = Targeting(rule);
            Unit(config, "enemy_high").mp = 1;
            Unit(config, "enemy_high").vulnerable = true;
            Assert.That(CombatTacticsCatalog.GetSelectors().Any(option => option.id == selector), Is.True);
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo(expectedTarget));
        }

        [Test]
        public void SoftPreference_DoesNotBypassHardConditionsAndFallsBackWhenNoMarkedTargetExists()
        {
            CombatInstruction rule = Attack("hard_filter", Term("hp_ratio_at_most", 0.5));
            rule.selector = "vulnerable_first";
            CombatScenario config = Targeting(rule);
            Unit(config, "enemy_high").vulnerable = true;
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo("enemy_low"));
            rule.condition.terms[0] = Term("always");
            Unit(config, "enemy_high").vulnerable = false;
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo("enemy_low"));
        }

        [Test]
        public void CatalogEntries_AreIndependentAndEveryConditionCanBeEditedAndEvaluated()
        {
            CombatTacticsOption[] options = CombatTacticsCatalog.GetConditions();
            Assert.That(options.Select(option => option.id).Distinct().Count(), Is.EqualTo(options.Length));
            foreach (CombatTacticsOption option in options)
            {
                Assert.That(option.label, Is.Not.Empty);
                var session = new CombatSession(Load());
                CombatInstruction rule = Attack("catalog_probe", Term(option.id, option.defaultThreshold,
                    option.usesValue ? (option.defaultValue ?? "test_mark") : null));
                Assert.That(session.ReplaceInstruction("mage", 0, rule).success, Is.True, option.id);
                CombatStepResult step = session.Step();
                Assert.That(step.attempts[0].candidates.Length, Is.GreaterThan(0), option.id);
                Assert.That(step.attempts[0].candidates.All(candidate => !candidate.reason.Contains("未实现")), Is.True, option.id);
            }
            options[0].id = "mutated";
            Assert.That(CombatTacticsCatalog.GetConditions()[0].id, Is.EqualTo("always"));
            CombatTacticsOption[] selectors = CombatTacticsCatalog.GetSelectors();
            selectors[0].label = "mutated";
            Assert.That(CombatTacticsCatalog.GetSelectors()[0].label, Is.Not.EqualTo("mutated"));
            Assert.That(CombatTacticsCatalog.GetScopes().Select(option => option.id), Is.EquivalentTo(new[] { "self", "allies", "enemies" }));
            Assert.That(CombatTacticsCatalog.GetOperations().Select(option => option.id), Is.EquivalentTo(new[] { "all", "any", "mixed" }));
            Assert.That(CombatTacticsCatalog.GetGroupings().Select(option => option.id), Is.EquivalentTo(new[] { "left", "right" }));
            Assert.That(CombatTacticsCatalog.GetSubjects().Select(option => option.id), Is.EquivalentTo(new[] { "target", "actor" }));
        }

        [Test]
        public void EditedConfiguration_RoundTripsAndReplaysWithTheSameGroupingAndWhitelist()
        {
            var session = new CombatSession(Load());
            CombatInstruction rule = Attack("replay_probe", Term("hp_ratio_at_most", 0.35), Term("vulnerable"), Term("mp_ratio_at_least", 0.5, subject: "actor"));
            rule.condition.operation = "mixed";
            rule.condition.operators = new[] { "or", "and" };
            rule.condition.grouping = "right";
            Assert.That(session.ReplaceInstruction("mage", 0, rule).success, Is.True);
            CombatScenario saved = JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(session.GetScenario()));
            session.RunToCompletion();
            string expected = session.ExportLogText();
            var restored = new CombatSession(saved);
            restored.RunToCompletion();
            Assert.That(restored.ExportLogText(), Is.EqualTo(expected));
            session.Reset();
            session.RunToCompletion();
            Assert.That(session.ExportLogText(), Is.EqualTo(expected));
        }
    }
}
