using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    // Integration tests execute the real runtime and real first-round JSON.
    // Numeric variants isolate a single timing/resource invariant; no alternate resolver exists here.
    public sealed class CombatRoundTwoTests
    {
        private static CombatScenario Load()
        {
            return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", "default.json")));
        }

        private static CombatUnitConfig Unit(CombatScenario config, string id)
        {
            return config.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatSnapshot snapshot, string id)
        {
            return snapshot.units.Single(unit => unit.id == id);
        }

        private static CombatScenario Duel(int friendlyHp = 100, int enemyHp = 100)
        {
            CombatScenario scenario = Load();
            CombatUnitConfig friendly = Unit(scenario, "mage");
            CombatUnitConfig enemy = Unit(scenario, "enemy_low");
            friendly.instructions = new[] { friendly.instructions[1] };
            friendly.hp = friendlyHp;
            friendly.initialReadyTime = 0;
            enemy.hp = enemyHp;
            enemy.initialReadyTime = 10;
            scenario.units = new[] { friendly, enemy };
            return scenario;
        }

        [Test]
        public void AutomaticAndManualStepping_ProduceIdenticalHistoryAndFinalSnapshot()
        {
            CombatScenario scenario = Load();
            var automatic = new CombatSession(scenario);
            CombatRunResult result = automatic.RunToCompletion();
            var manual = new CombatSession(scenario);
            for (int i = 0; i < 1000 && !manual.GetSnapshot().finished; i++) manual.Step();
            Assert.That(manual.GetSnapshot().finished, Is.True);
            Assert.That(JsonUtility.ToJson(automatic.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(manual.GetSnapshot())));
            Assert.That(automatic.ExportLogText(), Is.EqualTo(manual.ExportLogText()));
            Assert.That(result.steps.Length, Is.EqualTo(automatic.GetHistory().Length));
            Assert.That(result.stopReason, Is.Not.Empty);
        }

        [Test]
        public void AutomaticReplay_UsesTheSameOpeningConfigurationAndSeed()
        {
            var session = new CombatSession(Load());
            session.RunToCompletion();
            string expectedLog = session.ExportLogText();
            string expectedSnapshot = JsonUtility.ToJson(session.GetSnapshot());
            session.Reset();
            session.RunToCompletion();
            Assert.That(session.ExportLogText(), Is.EqualTo(expectedLog));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot()), Is.EqualTo(expectedSnapshot));
        }

        [Test]
        public void SimultaneousLethalAction_FinishesBeforeTheDefeatedActorCanAct()
        {
            CombatScenario scenario = Duel(100, 18);
            Unit(scenario, "enemy_low").initialReadyTime = 0;
            var session = new CombatSession(scenario);
            CombatStepResult first = session.Step();
            Assert.That(first.actorId, Is.EqualTo("mage"));
            Assert.That(Unit(session.GetSnapshot(), "enemy_low").hp, Is.Zero);
            Assert.That(session.GetSnapshot().finished, Is.True);
            Assert.That(session.GetSnapshot().winnerTeam, Is.EqualTo("friendly"));
            Assert.That(session.GetHistory().Count(step => step.status == "Executed"), Is.EqualTo(1));
            Assert.That(Unit(session.GetSnapshot(), "mage").hp, Is.EqualTo(100));
        }

        [Test]
        public void TerminalStepAndRun_AreReadOnlyAndDoNotAppendPseudoEvents()
        {
            var session = new CombatSession(Duel(100, 18));
            session.RunToCompletion();
            string expectedSnapshot = JsonUtility.ToJson(session.GetSnapshot());
            string expectedLog = session.ExportLogText();
            int count = session.GetHistory().Length;
            for (int i = 0; i < 3; i++)
            {
                session.Step();
                session.RunToCompletion();
            }
            Assert.That(session.GetHistory().Length, Is.EqualTo(count));
            Assert.That(session.ExportLogText(), Is.EqualTo(expectedLog));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot()), Is.EqualTo(expectedSnapshot));
        }

        [Test]
        public void SleepingHealer_WakesOnAChangedBattleStateWithoutTimeGoingBackwards()
        {
            CombatScenario scenario = Load();
            Unit(scenario, "mage").instructions = new[] { Unit(scenario, "mage").instructions[0] };
            Unit(scenario, "ally").instructions = new CombatInstruction[0];
            Unit(scenario, "ally").hp = 100;
            Unit(scenario, "enemy_low").hp = 100;
            Unit(scenario, "enemy_high").instructions = new CombatInstruction[0];
            scenario.actions.Single(action => action.id == "basic_attack").magnitude = 70;
            var session = new CombatSession(scenario);
            double lastTime = -1;
            bool healedAfterEnemy = false;
            bool enemyActed = false;
            for (int i = 0; i < 30 && !session.GetSnapshot().finished; i++)
            {
                CombatStepResult step = session.Step();
                Assert.That(step.time, Is.GreaterThanOrEqualTo(lastTime), "A reawakened actor cannot use an old timestamp.");
                lastTime = step.time;
                if (step.status == "Executed" && step.actorId == "enemy_low") enemyActed = true;
                if (step.status == "Executed" && step.actorId == "mage" && step.actionId == "first_aid")
                {
                    healedAfterEnemy = enemyActed;
                    break;
                }
            }
            Assert.That(healedAfterEnemy, Is.True);
            Assert.That(session.GetHistory().Any(step => step.actorId == "mage" && step.status == "Skipped"), Is.True);
        }

        [Test]
        public void EveryoneWithoutInstructions_StopsAsStalemateAndPreservesResources()
        {
            CombatScenario scenario = Duel();
            foreach (CombatUnitConfig unit in scenario.units) unit.instructions = new CombatInstruction[0];
            var session = new CombatSession(scenario);
            CombatRunResult result = session.RunToCompletion(100, 1000);
            Assert.That(result.outcome, Is.EqualTo("Stalemate"));
            Assert.That(result.winnerTeam, Is.Empty);
            Assert.That(result.stopReason, Is.Not.Empty);
            Assert.That(Unit(result.snapshot, "mage").hp, Is.EqualTo(100));
            Assert.That(Unit(result.snapshot, "mage").mp, Is.EqualTo(30));
            Assert.That(result.steps.All(step => step.status != "Executed"), Is.True);
        }

        [Test]
        public void IneffectiveActions_StopAsStalemateInsteadOfSpinningUntilTheEventBudget()
        {
            CombatScenario scenario = Duel();
            scenario.actions.Single(action => action.id == "basic_attack").magnitude = 0;
            CombatRunResult result = new CombatSession(scenario).RunToCompletion(100, 10000);
            Assert.That(result.outcome, Is.EqualTo("Stalemate"));
            Assert.That(result.steps.Length, Is.LessThan(100));
            Assert.That(Unit(result.snapshot, "mage").hp, Is.EqualTo(100));
            Assert.That(Unit(result.snapshot, "enemy_low").hp, Is.EqualTo(100));
        }

        [Test]
        public void EventBudget_IsSharedByStepAndAutomaticExecution()
        {
            CombatScenario scenario = Duel();
            scenario.maxEvents = 2;
            var automatic = new CombatSession(scenario);
            CombatRunResult result = automatic.RunToCompletion();
            var manual = new CombatSession(scenario);
            for (int i = 0; i < 10 && !manual.GetSnapshot().finished; i++) manual.Step();
            Assert.That(result.outcome, Is.EqualTo("Stopped"));
            Assert.That(result.steps.Length, Is.EqualTo(2));
            Assert.That(manual.ExportLogText(), Is.EqualTo(automatic.ExportLogText()));
            Assert.That(JsonUtility.ToJson(manual.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(result.snapshot)));
        }

        [Test]
        public void WaitBudget_DoesNotExecuteOrConsumeAnActionBeyondTheBoundary()
        {
            CombatScenario scenario = Duel();
            Unit(scenario, "mage").initialReadyTime = 100;
            Unit(scenario, "enemy_low").initialReadyTime = 200;
            var session = new CombatSession(scenario);
            CombatRunResult result = session.RunToCompletion(100, 50);
            Assert.That(result.outcome, Is.EqualTo("Stopped"));
            Assert.That(result.steps.Count(step => step.status == "Executed"), Is.Zero);
            Assert.That(Unit(result.snapshot, "mage").hp, Is.EqualTo(100));
            Assert.That(Unit(result.snapshot, "mage").mp, Is.EqualTo(30));
            Assert.That(result.stopReason, Is.Not.Empty);
        }

        [Test]
        public void ExpiryAtAnActionTimestamp_HappensBeforeDamage()
        {
            CombatScenario scenario = Duel();
            Unit(scenario, "mage").initialReadyTime = 10;
            Unit(scenario, "enemy_low").initialReadyTime = 20;
            Unit(scenario, "enemy_low").statuses = new[]
            {
                new CombatStatusConfig { id = "guard-test", kind = "guard", magnitude = 100, appliedAt = 0, expiresAt = 10 }
            };
            var session = new CombatSession(scenario);
            CombatStepResult expiry = session.Step();
            CombatStepResult action = session.Step();
            Assert.That(expiry.status, Is.EqualTo("StateExpired"));
            Assert.That(expiry.time, Is.EqualTo(10));
            Assert.That(action.actorId, Is.EqualTo("mage"));
            Assert.That(action.time, Is.EqualTo(10));
            Assert.That(action.effects[0].hpDelta, Is.EqualTo(-18));
            Assert.That(Unit(session.GetSnapshot(), "enemy_low").statuses, Is.Empty);
        }

        [Test]
        public void FutureExpiry_IsResolvedBeforeDeclaringAWaitingBattleStalemate()
        {
            CombatScenario scenario = Duel();
            foreach (CombatUnitConfig unit in scenario.units) unit.instructions = new CombatInstruction[0];
            Unit(scenario, "mage").statuses = new[]
            {
                new CombatStatusConfig { id = "temporary", kind = "guard", magnitude = 1, appliedAt = 0, expiresAt = 25 }
            };
            CombatRunResult result = new CombatSession(scenario).RunToCompletion();
            Assert.That(result.outcome, Is.EqualTo("Stalemate"));
            Assert.That(result.steps.Any(step => step.status == "StateExpired" && step.time == 25), Is.True);
            Assert.That(Unit(result.snapshot, "mage").statuses, Is.Empty);
        }

        private static CombatSession CompletedFirstEncounter()
        {
            CombatScenario scenario = Duel(70, 18);
            CombatInstruction aid = Load().units.Single(unit => unit.id == "mage").instructions[0];
            aid.condition.terms[0].threshold = 0.8;
            Unit(scenario, "mage").instructions = new[] { aid, Unit(scenario, "mage").instructions[0] };
            Unit(scenario, "mage").mp = 6;
            Unit(scenario, "mage").initialReadyTime = 20;
            Unit(scenario, "enemy_low").initialReadyTime = 30;
            Unit(scenario, "mage").statuses = new[]
            {
                new CombatStatusConfig { id = "expired-in-first", kind = "guard", magnitude = 0, appliedAt = 0, expiresAt = 25 },
                new CombatStatusConfig { id = "carry-buff", kind = "empowered", magnitude = 0, appliedAt = 0, expiresAt = 200 }
            };
            var session = new CombatSession(scenario);
            session.RunToCompletion();
            return session;
        }

        [Test]
        public void ConsecutiveEncounter_CarriesActualHpMpStatusesAndRemainingWait()
        {
            CombatSession first = CompletedFirstEncounter();
            CombatSnapshot previous = first.GetSnapshot();
            Assert.That(previous.winnerTeam, Is.EqualTo("friendly"));
            Assert.That(previous.time, Is.EqualTo(60));
            Assert.That(Unit(previous, "mage").hp, Is.EqualTo(82));
            Assert.That(Unit(previous, "mage").mp, Is.Zero);
            var next = new CombatSession(Duel(), previous, "friendly", 15);
            CombatSnapshot opening = next.GetSnapshot();
            Assert.That(opening.time, Is.EqualTo(75));
            Assert.That(Unit(opening, "mage").hp, Is.EqualTo(82));
            Assert.That(Unit(opening, "mage").mp, Is.Zero);
            Assert.That(Unit(opening, "mage").nextActionTime, Is.EqualTo(120));
            Assert.That(Unit(opening, "mage").statuses.Select(status => status.id), Is.EqualTo(new[] { "carry-buff" }));
            Assert.That(Unit(opening, "mage").statuses[0].expiresAt, Is.EqualTo(200));
            CombatStepResult nextAction = next.Step();
            Assert.That(nextAction.actorId, Is.EqualTo("enemy_low"));
            Assert.That(nextAction.time, Is.GreaterThanOrEqualTo(75));
            Assert.That(nextAction.effects[0].hpBefore, Is.EqualTo(82));
        }

        [Test]
        public void BetweenEncounterWait_ExpiresOldStatusesWithoutReapplyingTemplateBuffs()
        {
            CombatSnapshot previous = CompletedFirstEncounter().GetSnapshot();
            CombatScenario second = Duel();
            Unit(second, "mage").statuses = new[]
            {
                new CombatStatusConfig { id = "carry-buff", kind = "empowered", magnitude = 99, appliedAt = 0, expiresAt = 9999 }
            };
            CombatSnapshot opening = new CombatSession(second, previous, "friendly", 200).GetSnapshot();
            Assert.That(opening.time, Is.EqualTo(260));
            Assert.That(Unit(opening, "mage").statuses, Is.Empty, "The next template must not resurrect an expired carried buff.");
            Assert.That(Unit(opening, "mage").hp, Is.EqualTo(Unit(previous, "mage").hp));
            Assert.That(Unit(opening, "mage").mp, Is.EqualTo(Unit(previous, "mage").mp));
        }

        [Test]
        public void CarryInputAndReturnedSnapshots_AreDeeplyIsolated()
        {
            CombatSnapshot previous = CompletedFirstEncounter().GetSnapshot();
            var next = new CombatSession(Duel(), previous, "friendly", 15);
            previous.units.Single(unit => unit.id == "mage").hp = 1;
            previous.units.Single(unit => unit.id == "mage").statuses[0].magnitude = 999;
            CombatSnapshot external = next.GetSnapshot();
            Unit(external, "mage").hp = 2;
            Unit(external, "mage").statuses[0].expiresAt = 100000;
            Assert.That(Unit(next.GetSnapshot(), "mage").hp, Is.EqualTo(82));
            Assert.That(Unit(next.GetSnapshot(), "mage").statuses[0].magnitude, Is.Zero);
            Assert.That(Unit(next.GetSnapshot(), "mage").statuses[0].expiresAt, Is.EqualTo(200));
        }

        [Test]
        public void CarryRejectsRosterAndMaximumResourceMismatches()
        {
            CombatSnapshot previous = CompletedFirstEncounter().GetSnapshot();
            CombatScenario second = Duel();
            Unit(second, "mage").maxMp++;
            Assert.Throws<ArgumentException>(() => new CombatSession(second, previous, "friendly", 15));
            second = Duel();
            Unit(second, "mage").id = "different-caster";
            Assert.Throws<ArgumentException>(() => new CombatSession(second, previous, "friendly", 15));
        }

        [Test]
        public void FutureStatusApplication_IsRejectedInsteadOfBecomingActiveEarly()
        {
            CombatScenario opening = Duel();
            Unit(opening, "mage").statuses = new[]
            {
                new CombatStatusConfig { id = "future", kind = "guard", magnitude = 1, appliedAt = 5, expiresAt = 25 }
            };
            Assert.Throws<ArgumentException>(() => new CombatSession(opening));
            CombatSnapshot previous = CompletedFirstEncounter().GetSnapshot();
            Unit(previous, "mage").statuses[0].appliedAt = previous.time + 1;
            Unit(previous, "mage").statuses[0].expiresAt = previous.time + 100;
            Assert.Throws<ArgumentException>(() => new CombatSession(Duel(), previous, "friendly", 15));
        }

        [Test]
        public void TimedVulnerability_ExpiresAcrossTheGapWithoutBecomingAPermanentFlag()
        {
            CombatScenario firstConfig = Duel(100, 18);
            Unit(firstConfig, "mage").statuses = new[]
            {
                new CombatStatusConfig { id = "brief-vulnerability", kind = "vulnerable", magnitude = 0, appliedAt = 0, expiresAt = 5 }
            };
            var first = new CombatSession(firstConfig);
            first.RunToCompletion();
            CombatSnapshot previous = first.GetSnapshot();
            Assert.That(Unit(previous, "mage").vulnerable, Is.True);
            Assert.That(Unit(previous, "mage").baseVulnerable, Is.False);
            CombatScenario template = Duel();
            Unit(template, "mage").vulnerable = true;
            CombatSnapshot next = new CombatSession(template, previous, "friendly", 10).GetSnapshot();
            Assert.That(Unit(next, "mage").vulnerable, Is.False);
            Assert.That(Unit(next, "mage").baseVulnerable, Is.False);
            Assert.That(Unit(next, "mage").statuses, Is.Empty);
        }

        [Test]
        public void EquipmentDamageBonus_IsReadFromTheInputWithoutChangingBaseSkillData()
        {
            CombatScenario scenario = Duel();
            Unit(scenario, "mage").equipmentDamageBonus = 7;
            Unit(scenario, "mage").equipmentSourceId = "test-sword-enhanced";
            var session = new CombatSession(scenario);
            CombatStepResult step = session.Step();
            Assert.That(step.effects[0].hpDelta, Is.EqualTo(-25));
            Assert.That(scenario.actions.Single(action => action.id == "basic_attack").magnitude, Is.EqualTo(18));
            Assert.That(session.GetScenario().units.Single(unit => unit.id == "mage").equipmentDamageBonus, Is.EqualTo(7));
            scenario.units.Single(unit => unit.id == "mage").equipmentDamageBonus = 999;
            Assert.That(session.GetScenario().units.Single(unit => unit.id == "mage").equipmentDamageBonus, Is.EqualTo(7));
        }
    }
}
