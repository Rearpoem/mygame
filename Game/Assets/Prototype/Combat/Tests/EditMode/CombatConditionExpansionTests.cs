using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static Prototype.Combat.Tests.CombatExpansionFixture;

namespace Prototype.Combat.Tests
{
    public sealed class CombatConditionExpansionTests
    {
        [TestCase("hp_at_most", 50, null, "b")]
        [TestCase("hp_at_least", 60, null, "a")]
        [TestCase("mp_at_most", 10, null, "a")]
        [TestCase("mp_at_least", 15, null, "b")]
        [TestCase("hp_missing_at_least", 50, null, "b")]
        [TestCase("mp_missing_at_least", 20, null, "a")]
        [TestCase("has_buff", 0, null, "b")]
        [TestCase("has_debuff", 0, null, "b")]
        [TestCase("has_control", 0, null, "b")]
        [TestCase("has_status_kind", 0, "guard", "b")]
        [TestCase("lacks_status_kind", 0, "guard", "a")]
        [TestCase("has_tag", 0, "caster", "b")]
        [TestCase("lacks_tag", 0, "caster", "a")]
        [TestCase("team_alive_at_least", 2, null, "a")]
        [TestCase("team_alive_at_most", 1, null, "b")]
        [TestCase("opponents_alive_at_least", 3, null, "b")]
        [TestCase("opponents_alive_at_most", 2, null, "a")]
        [TestCase("team_injured_at_least", 2, null, "a")]
        [TestCase("row_alive_at_least", 2, null, "a")]
        [TestCase("row_injured_at_least", 2, null, "a")]
        [TestCase("status_remaining_at_most", 20, "mark", "b")]
        [TestCase("status_remaining_at_least", 30, "mark", "a")]
        [TestCase("wait_remaining_at_most", 30, null, "a")]
        [TestCase("wait_remaining_at_least", 30, null, "b")]
        [TestCase("is_actor", 0, null, "actor")]
        [TestCase("is_not_actor", 0, null, "a")]
        public void NewConditions_FilterRealCandidatesBeforeDamage(string kind, double threshold, string value, string expected)
        {
            CombatScenario config = Pair();
            string scope = "enemies";
            if (kind.StartsWith("team_") || kind.StartsWith("opponents_") || kind.StartsWith("row_"))
            {
                Get(config, "b").team = "violet";
                CombatUnitConfig peer = Unit("peer", "red", 0, 1); peer.hp = 70;
                config.units = config.units.Concat(new[] { peer }).ToArray();
            }
            else if (kind.StartsWith("status_remaining"))
            {
                Get(config, "a").statuses = new[] { Status("mark", "guard", 0, 50) };
                Get(config, "b").statuses = new[] { Status("mark", "guard", 0, 15) };
            }
            else if (kind == "is_actor" || kind == "is_not_actor")
            {
                Get(config, "a").team = "blue"; Get(config, "a").column = 1;
                Get(config, "b").team = "blue";
                // Keep an opposing team so the live battle does not end before its first action.
                config.units = config.units.Concat(new[] { Unit("foe", "red") }).ToArray();
                scope = "allies";
            }
            else
            {
                Get(config, "b").statuses = new[] { Status("buff", "guard", 0), Status("control", "slow", 0) };
                Get(config, "b").tags = new[] { "caster" };
            }
            Configure(config, new[] { Rule(terms: new[] { Term(kind, threshold, value) }, scope: scope) });
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.status, Is.EqualTo("Executed"), kind);
            Assert.That(step.targetId, Is.EqualTo(expected), kind);
            Assert.That(Get(session.GetSnapshot(), expected).hp, Is.EqualTo(Get(config, expected).hp - 1));
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == expected).passesCondition, Is.True);
            string rejected = kind == "is_not_actor" ? "actor" : expected == "a" ? "b" : "a";
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == rejected).passesCondition, Is.False,
                "The predicate must filter a different real object, not merely be listed in a catalog.");
        }

        [TestCase("status_remaining_at_most")]
        [TestCase("status_remaining_at_least")]
        public void MissingStatus_DoesNotPretendToHaveZeroRemainingTime(string kind)
        {
            CombatScenario config = Pair();
            Configure(config, new[] { Rule("missing", terms: new[] { Term(kind, 0, "absent") }), Rule("fallback") });
            CombatStepResult step = new CombatSession(config).Step();
            Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(step.attempts[0].candidates.All(candidate => !candidate.passesCondition), Is.True);
            Assert.That(step.attempts.Last().instructionId, Is.EqualTo("fallback"));
        }

        [TestCase("status_remaining_at_most", 15)]
        [TestCase("wait_remaining_at_most", 25)]
        public void RemainingWaitPredicates_SubtractTheActualCurrentBattleTime(string kind, double threshold)
        {
            CombatScenario config = Pair(); Get(config, "actor").initialReadyTime = 30;
            Get(config, "a").initialReadyTime = 50; Get(config, "b").initialReadyTime = 90;
            Get(config, "a").statuses = new[] { Status("mark", "guard", 0, 40) };
            Get(config, "b").statuses = new[] { Status("mark", "guard", 0, 90) };
            Configure(config, new[] { Rule(terms: new[] { Term(kind, threshold, "mark") }) });
            CombatStepResult step = new CombatSession(config).Step();
            Assert.That(step.time, Is.EqualTo(30)); Assert.That(step.targetId, Is.EqualTo("a"));
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == "b").passesCondition, Is.False);
        }

        [Test]
        public void TeamCount_UsesTheSelectedSubjectAndExcludesDeadMembers()
        {
            CombatScenario config = Pair();
            CombatUnitConfig dead = Unit("dead", "blue", 1); dead.hp = 0;
            config.units = config.units.Concat(new[] { dead }).ToArray();
            CombatInstruction rule = Rule("count", terms: new[] { Term("team_alive_at_least", 2) });
            Configure(config, new[] { rule, Rule("fallback") });
            Assert.That(new CombatSession(config).Step().attempts[0].outcome, Is.EqualTo("Selected"));
            rule.condition.terms[0].subject = "actor";
            CombatStepResult actor = new CombatSession(config).Step();
            Assert.That(actor.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(actor.attempts.Last().instructionId, Is.EqualTo("fallback"));
        }

        [TestCase("mp_at_least", -1, null)]
        [TestCase("hp_at_most", 1.5, null)]
        [TestCase("team_alive_at_least", 1.5, null)]
        [TestCase("status_remaining_at_most", -1, "mark")]
        [TestCase("has_status_kind", 0, "invented_kind")]
        [TestCase("has_tag", 0, " ")]
        public void InvalidNewCondition_EditIsAtomic(string kind, double threshold, string value)
        {
            CombatScenario config = Pair(); Configure(config, new[] { Rule() });
            var session = new CombatSession(config);
            string before = JsonUtility.ToJson(session.GetScenario());
            string snapshot = JsonUtility.ToJson(session.GetSnapshot());
            CombatEditResult edit = session.ReplaceInstruction("actor", 0,
                Rule("invalid", terms: new[] { Term(kind, threshold, value) }));
            Assert.That(edit.success, Is.False);
            Assert.That(edit.code, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(session.GetScenario()), Is.EqualTo(before));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot()), Is.EqualTo(snapshot));
            Assert.That(session.GetHistory(), Is.Empty);
        }

        [TestCase("lowest_mp", "a")]
        [TestCase("highest_mp", "b")]
        [TestCase("most_hp_missing", "b")]
        [TestCase("most_mp_missing", "a")]
        [TestCase("buffed_first", "b")]
        [TestCase("debuffed_first", "b")]
        [TestCase("controlled_first", "b")]
        [TestCase("unprotected_first", "a")]
        [TestCase("highest_intelligence", "b")]
        [TestCase("lowest_intelligence", "a")]
        [TestCase("soonest_action", "a")]
        [TestCase("latest_action", "b")]
        [TestCase("fastest_speed", "b")]
        [TestCase("slowest_speed", "a")]
        public void NewSelectors_ChooseDifferentRealTargets(string selector, string expected)
        {
            CombatScenario config = Pair();
            Get(config, "b").statuses = new[] { Status("buff", "guard", 0), Status("debuff", "slow", 0) };
            Get(config, "b").agilityFactor = .5;
            Configure(config, new[] { Rule(selector: selector) });
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetId, Is.EqualTo(expected));
            Assert.That(Get(session.GetSnapshot(), expected).hp, Is.EqualTo(Get(config, expected).hp - 1));
        }

        [TestCase("fastest_speed", "b")]
        [TestCase("slowest_speed", "a")]
        public void SpeedSelectors_UseCurrentStatesRatherThanOnlyBaseAgility(string selector, string expected)
        {
            CombatScenario config = Pair(); Get(config, "a").agilityFactor = .5;
            Get(config, "a").statuses = new[] { Status("slow", "slow", 200) };
            Get(config, "b").statuses = new[] { Status("haste", "haste", 100) };
            Configure(config, new[] { Rule(selector: selector) });
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo(expected));
        }

        [TestCase("buffed_first")]
        [TestCase("debuffed_first")]
        [TestCase("controlled_first")]
        public void NewSoftPreferences_RespectHardConditionsAndHaveAFallback(string selector)
        {
            CombatScenario config = Pair();
            Get(config, "b").statuses = new[] { Status("buff", "guard", 0), Status("debuff", "slow", 0) };
            Configure(config, new[] { Rule(selector: selector, terms: new[] { Term("hp_at_least", 70) }) });
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo("a"));
            Get(config, "b").statuses = new CombatStatusConfig[0];
            Configure(config, new[] { Rule(selector: selector) });
            Assert.That(new CombatSession(config).Step().targetId, Is.EqualTo("b"), "With no preferred state, HP ratio remains the tie-break.");
        }

        [Test]
        public void CatalogMetadata_SeparatesRatiosPointsCountsRowsAndWaitAndReturnsCopies()
        {
            CombatTacticsOption[] conditions = CombatTacticsCatalog.GetConditions();
            Assert.That(conditions.Single(option => option.id == "hp_ratio_at_most").thresholdUnit, Is.EqualTo("ratio"));
            Assert.That(conditions.Single(option => option.id == "mp_at_least").thresholdUnit, Is.EqualTo("points"));
            Assert.That(conditions.Single(option => option.id == "team_alive_at_least").thresholdUnit, Is.EqualTo("count"));
            Assert.That(conditions.Single(option => option.id == "row_is").thresholdUnit, Is.EqualTo("row"));
            Assert.That(conditions.Single(option => option.id == "status_remaining_at_most").thresholdUnit, Is.EqualTo("wait"));
            foreach (CombatTacticsOption option in conditions.Where(option => option.usesValue))
                Assert.That(option.defaultValue, Is.Not.Empty, option.id);
            CombatTacticsOption[] kinds = CombatTacticsCatalog.GetStatusKinds(); kinds[0].id = "mutated";
            Assert.That(CombatTacticsCatalog.GetStatusKinds()[0].id, Is.EqualTo("guard"));
            CombatTacticsOption[] modes = CombatTacticsCatalog.GetTargetModes(); modes[0].label = "mutated";
            Assert.That(CombatTacticsCatalog.GetTargetModes()[0].label, Is.Not.EqualTo("mutated"));
        }
    }
}
