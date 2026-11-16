using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    // Each test starts from the same real StreamingAssets JSON used by the UI.
    // Variants change only the field(s) named in the test; there is no second rules implementation.
    public sealed class CombatSessionTests
    {
        private static CombatScenario Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "default.json");
            return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(path));
        }

        private static CombatUnitConfig Unit(CombatScenario config, string id)
        {
            return config.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatSnapshot snapshot, string id)
        {
            return snapshot.units.Single(unit => unit.id == id);
        }

        [Test]
        public void RealJson_HasRequiredOpening_AndRoundTripsThroughJsonUtility()
        {
            CombatScenario config = Load();
            var session = new CombatSession(config);
            CombatSnapshot snapshot = session.GetSnapshot();
            Assert.That(config.units.Count(unit => unit.team == "friendly"), Is.EqualTo(2));
            Assert.That(config.units.Count(unit => unit.team == "hostile"), Is.EqualTo(2));
            Assert.That(snapshot.nextActorId, Is.EqualTo("mage"));
            Assert.That(Unit(snapshot, "mage").hp, Is.EqualTo(Unit(snapshot, "mage").maxHp));
            Assert.That((double)Unit(snapshot, "ally").hp / Unit(snapshot, "ally").maxHp, Is.EqualTo(0.2));
            Assert.That((double)Unit(snapshot, "enemy_low").hp / Unit(snapshot, "enemy_low").maxHp,
                Is.LessThan((double)Unit(snapshot, "enemy_high").hp / Unit(snapshot, "enemy_high").maxHp));
            Assert.That(config.parameterNotice, Does.Contain("测试参数"));
            Assert.That(config.randomUsed, Is.False);
            var roundTrip = new CombatSession(JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(config)));
            Assert.That(JsonUtility.ToJson(roundTrip.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(snapshot)));
        }

        [Test]
        public void HealingFirst_SelectsInjuredAlly_NotFullHealthCaster()
        {
            var session = new CombatSession(Load());
            CombatStepResult step = session.Step();
            CombatSnapshot snapshot = session.GetSnapshot();
            Assert.That(step.status, Is.EqualTo("Executed"));
            Assert.That(step.actorId, Is.EqualTo("mage"));
            Assert.That(step.actionId, Is.EqualTo("first_aid"));
            Assert.That(step.targetId, Is.EqualTo("ally"));
            Assert.That(step.attempts.Length, Is.EqualTo(1));
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == "mage").passesCondition, Is.False);
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == "ally").eligible, Is.True);
            Assert.That(Unit(snapshot, "ally").hp, Is.EqualTo(65));
            Assert.That(Unit(snapshot, "mage").hp, Is.EqualTo(100));
            Assert.That(Unit(snapshot, "mage").mp, Is.EqualTo(24));
            Assert.That(step.effects[0].hpDelta, Is.EqualTo(45));
        }

        [Test]
        public void ChangingOnlyInstructionOrder_AttacksLowestHpRatioEnemyFirst()
        {
            var session = new CombatSession(Load());
            Assert.That(session.MoveInstruction("mage", 1, 0).success, Is.True);
            session.Reset();
            CombatStepResult step = session.Step();
            CombatSnapshot snapshot = session.GetSnapshot();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.targetId, Is.EqualTo("enemy_low"));
            Assert.That(step.attempts[0].instructionId, Is.EqualTo("fallback_attack"));
            Assert.That(Unit(snapshot, "enemy_low").hp, Is.EqualTo(17));
            Assert.That(Unit(snapshot, "enemy_high").hp, Is.EqualTo(80));
            Assert.That(Unit(snapshot, "ally").hp, Is.EqualTo(20));
            Assert.That(Unit(snapshot, "mage").mp, Is.EqualTo(30));
        }

        [Test]
        public void ChangingOnlyAllyInitialHpToFull_FallsBackFromHealingToAttack()
        {
            var session = new CombatSession(Load());
            Assert.That(session.SetInitialHp("ally", 100).success, Is.True);
            Assert.That(Unit(session.GetSnapshot(), "ally").hp, Is.EqualTo(20), "Changing the baseline must wait for Reset.");
            session.Reset();
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.targetId, Is.EqualTo("enemy_low"));
            Assert.That(step.attempts.Length, Is.EqualTo(2));
            Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(step.attempts[0].candidates.All(candidate => !candidate.passesCondition), Is.True);
            Assert.That(Unit(session.GetSnapshot(), "ally").hp, Is.EqualTo(100));
        }

        [Test]
        public void Replay_SameOpeningAndRules_ProducesIdenticalEventsAndText()
        {
            var session = new CombatSession(Load());
            var recorded = new string[6];
            for (int i = 0; i < recorded.Length; i++) recorded[i] = JsonUtility.ToJson(session.Step());
            string firstLog = session.ExportLogText();
            string firstSnapshot = JsonUtility.ToJson(session.GetSnapshot());
            session.Reset();
            for (int i = 0; i < recorded.Length; i++) Assert.That(JsonUtility.ToJson(session.Step()), Is.EqualTo(recorded[i]));
            Assert.That(session.GetHistory().All(step => step.status == "Executed"), Is.True);
            Assert.That(session.ExportLogText(), Is.EqualTo(firstLog));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot()), Is.EqualTo(firstSnapshot));
            Assert.That(firstLog, Does.Contain("randomUsed=false"));
        }

        [Test]
        public void AndConditions_MustBeTrueOnTheSameCandidate()
        {
            CombatScenario config = Load();
            CombatUnitConfig mage = Unit(config, "mage");
            mage.vulnerable = true; // mage satisfies only vulnerable; ally satisfies only low HP.
            mage.instructions[0].condition.terms = new[]
            {
                new CombatConditionTerm { kind = "hp_ratio_at_most", threshold = 0.35 },
                new CombatConditionTerm { kind = "vulnerable", threshold = 0 }
            };
            CombatStepResult before = new CombatSession(config).Step();
            Assert.That(before.actionId, Is.EqualTo("basic_attack"));
            Assert.That(before.attempts[0].candidates.All(candidate => !candidate.eligible), Is.True);
            Unit(config, "ally").vulnerable = true; // This single change now makes one candidate satisfy both.
            CombatStepResult after = new CombatSession(config).Step();
            Assert.That(after.actionId, Is.EqualTo("first_aid"));
            Assert.That(after.targetId, Is.EqualTo("ally"));
        }

        [Test]
        public void InsufficientMp_SkipsWithoutCostOrHealing_ThenExecutesFallback()
        {
            CombatScenario config = Load();
            Unit(config, "mage").mp = config.actions.Single(action => action.id == "first_aid").mpCost - 1;
            var session = new CombatSession(config);
            CombatSnapshot before = session.GetSnapshot();
            CombatStepResult step = session.Step();
            CombatSnapshot after = session.GetSnapshot();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.attempts[0].reason, Does.Contain("MP 不足"));
            Assert.That(Unit(after, "mage").mp, Is.EqualTo(Unit(before, "mage").mp));
            Assert.That(Unit(after, "ally").hp, Is.EqualTo(Unit(before, "ally").hp));
            Assert.That(Unit(after, "mage").row, Is.EqualTo(Unit(before, "mage").row));
            Assert.That(Unit(after, "mage").column, Is.EqualTo(Unit(before, "mage").column));
            Assert.That(step.actualWait, Is.EqualTo(60), "Only the executed attack schedules WAIT.");
        }

        [TestCase(1.0, 160.0)]
        [TestCase(0.5, 130.0)]
        public void ActualWait_UsesExplicitAgilityFactor(double agilityFactor, double expectedNextTime)
        {
            CombatScenario config = Load();
            foreach (CombatUnitConfig unit in config.units) unit.initialReadyTime += 100;
            Unit(config, "mage").agilityFactor = agilityFactor;
            var session = new CombatSession(config);
            session.MoveInstruction("mage", 1, 0);
            CombatStepResult step = session.Step();
            Assert.That(step.time, Is.EqualTo(100));
            Assert.That(step.baseWait, Is.EqualTo(60));
            Assert.That(step.actualWait, Is.EqualTo(60 * agilityFactor));
            Assert.That(step.nextActionTime, Is.EqualTo(expectedNextTime));
            Assert.That(Unit(session.GetSnapshot(), "mage").nextActionTime, Is.EqualTo(expectedNextTime));
        }

        [Test]
        public void IntelligenceCapacity_UsesExplicitLinearTestParametersAndUnlocksConditions()
        {
            var session = new CombatSession(Load());
            CombatSnapshot snapshot = session.GetSnapshot();
            Assert.That(Unit(snapshot, "mage").instructionCapacity, Is.EqualTo(5));
            Assert.That(Unit(snapshot, "ally").instructionCapacity, Is.EqualTo(2));
            Assert.That(Unit(snapshot, "mage").conditionTermCapacity, Is.EqualTo(3));
            Assert.That(Unit(snapshot, "ally").conditionTermCapacity, Is.EqualTo(1));
            CombatScenario edited = session.GetScenario();
            Unit(edited, "mage").intelligence = 20;
            CombatUnitSnapshot changed = Unit(new CombatSession(edited).GetSnapshot(), "mage");
            Assert.That(changed.instructionCapacity, Is.EqualTo(3));
            Assert.That(changed.conditionTermCapacity, Is.EqualTo(2));
        }

        [Test]
        public void LockedConditionCount_SkipsInsteadOfTreatingConditionsAsFree()
        {
            CombatScenario config = Load();
            CombatUnitConfig mage = Unit(config, "mage");
            mage.intelligence = Unit(config, "ally").intelligence;
            mage.instructions[0].condition.terms = new[]
            {
                new CombatConditionTerm { kind = "hp_ratio_at_most", threshold = 0.35 },
                new CombatConditionTerm { kind = "always", threshold = 0 }
            };
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(Unit(session.GetSnapshot(), "mage").instructions[0].capacityAllowed, Is.False);
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.attempts[0].reason, Does.Contain("条件项数"));
        }

        [Test]
        public void NoExecutableRule_SuspendsOnlyThisActorAndLetsOtherActorsContinue()
        {
            CombatScenario config = Load();
            CombatUnitConfig mage = Unit(config, "mage");
            mage.instructions = new[] { mage.instructions[0] };
            Unit(config, "ally").hp = Unit(config, "ally").maxHp;
            var session = new CombatSession(config);
            CombatSnapshot before = session.GetSnapshot();
            CombatStepResult step = session.Step();
            CombatSnapshot after = session.GetSnapshot();
            Assert.That(step.status, Is.EqualTo("Skipped"));
            Assert.That(after.blocked, Is.False);
            Assert.That(after.time, Is.EqualTo(before.time));
            Assert.That(Unit(after, "mage").mp, Is.EqualTo(Unit(before, "mage").mp));
            Assert.That(Unit(after, "ally").hp, Is.EqualTo(Unit(before, "ally").hp));
            Assert.That(after.completedStepCount, Is.EqualTo(0));
            CombatStepResult next = session.Step();
            Assert.That(next.status, Is.EqualTo("Executed"));
            Assert.That(next.actorId, Is.EqualTo("ally"));
            Assert.That(next.time, Is.EqualTo(10));
            Assert.That(session.GetHistory().Length, Is.EqualTo(2));
        }

        [Test]
        public void SimultaneousReadiness_UsesConfigurationOrderAndIsReplayable()
        {
            CombatScenario config = Load();
            Unit(config, "ally").initialReadyTime = Unit(config, "mage").initialReadyTime;
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.status, Is.EqualTo("Executed"));
            Assert.That(step.actorId, Is.EqualTo("mage"));
            Assert.That(step.time, Is.EqualTo(0));
            CombatStepResult second = session.Step();
            Assert.That(second.status, Is.EqualTo("Executed"));
            Assert.That(second.actorId, Is.EqualTo("ally"));
            Assert.That(second.time, Is.EqualTo(0));
            string expected = session.ExportLogText();
            session.Reset();
            session.Step();
            session.Step();
            Assert.That(session.ExportLogText(), Is.EqualTo(expected));
        }

        [Test]
        public void UnknownAction_SkipsWithTraceAndNoConsumption()
        {
            CombatScenario config = Load();
            Unit(config, "mage").instructions[0].actionId = "not_implemented_action";
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.attempts[0].reason, Does.Contain("找不到行动配置"));
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(30));
        }

        [Test]
        public void TargetSelector_ComparesRatiosRatherThanRawHp()
        {
            CombatScenario config = Load();
            Unit(config, "enemy_high").maxHp = 1000; // 80/1000 < 35/100 even though raw HP is higher.
            var session = new CombatSession(config);
            session.MoveInstruction("mage", 1, 0);
            Assert.That(session.Step().targetId, Is.EqualTo("enemy_high"));
        }

        [Test]
        public void EqualTargetRatios_UseDeclaredConfigurationOrder()
        {
            CombatScenario config = Load();
            Unit(config, "enemy_high").hp = Unit(config, "enemy_low").hp;
            var session = new CombatSession(config);
            session.MoveInstruction("mage", 1, 0);
            Assert.That(session.Step().targetId, Is.EqualTo("enemy_low"));
        }

        [Test]
        public void ActionsMayTargetAcrossAllegiance_HealingEnemyIsNotAutomaticallyForbidden()
        {
            CombatScenario config = Load();
            Unit(config, "mage").instructions[0].scope = "enemies";
            CombatStepResult step = new CombatSession(config).Step();
            Assert.That(step.actionId, Is.EqualTo("first_aid"));
            Assert.That(step.targetId, Is.EqualTo("enemy_low"));
            Assert.That(step.effects[0].hpDelta, Is.EqualTo(45));
        }

        [Test]
        public void ThresholdEditing_PersistsAcrossReset_AndRejectsInvalidInput()
        {
            var session = new CombatSession(Load());
            Assert.That(session.SetConditionThreshold("mage", 0, 0, 0.1).success, Is.True);
            Assert.That(session.SetConditionThreshold("mage", 0, 0, 2).success, Is.False);
            session.Reset();
            Assert.That(session.GetScenario().units[0].instructions[0].condition.terms[0].threshold, Is.EqualTo(0.1));
            Assert.That(Unit(session.GetSnapshot(), "mage").instructions[0].display, Does.StartWith("HP≤10% | 己方 | HP比例最低 | 急救"));
            Assert.That(session.Step().actionId, Is.EqualTo("basic_attack"));
        }

        [Test]
        public void PublicDataCopies_CannotChangeSessionStateOrRecordedEvents()
        {
            var session = new CombatSession(Load());
            session.GetScenario().units[0].hp = 1;
            session.GetSnapshot().units[0].hp = 2;
            Assert.That(Unit(session.GetSnapshot(), "mage").hp, Is.EqualTo(100));
            CombatStepResult step = session.Step();
            string log = session.ExportLogText();
            step.targetId = "changed";
            step.effects[0].hpAfter = 0;
            session.GetHistory()[0].attempts[0].reason = "changed";
            Assert.That(session.ExportLogText(), Is.EqualTo(log));
        }

        [Test]
        public void LogExport_IsIndependentOfCurrentCulture_AndUsesLfNewlines()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var first = new CombatSession(Load());
                first.MoveInstruction("mage", 1, 0);
                first.Step();
                string expected = first.ExportLogText();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
                var second = new CombatSession(Load());
                second.MoveInstruction("mage", 1, 0);
                second.Step();
                Assert.That(second.ExportLogText(), Is.EqualTo(expected));
                Assert.That(second.ExportLogText(), Does.Not.Contain("\r"));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Test]
        public void TeamLimitAndGridOccupancy_AreValidated()
        {
            CombatScenario config = Load();
            config.maxUnitsPerTeam = 7;
            Assert.Throws<ArgumentException>(() => new CombatSession(config));
            config = Load();
            Unit(config, "enemy_low").row = Unit(config, "ally").row;
            Unit(config, "enemy_low").column = Unit(config, "ally").column;
            Assert.DoesNotThrow(() => new CombatSession(config), "Separate teams use separate grids in this test interpretation.");
            Unit(config, "enemy_high").row = Unit(config, "enemy_low").row;
            Unit(config, "enemy_high").column = Unit(config, "enemy_low").column;
            Assert.Throws<ArgumentException>(() => new CombatSession(config));
        }
    }
}
