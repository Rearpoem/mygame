using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatExpeditionTests
    {
        private static CombatScenario Load()
        {
            return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", "default.json")));
        }

        private static CombatUnitConfig Unit(CombatScenario scenario, string id)
        {
            return scenario.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatSnapshot snapshot, string id)
        {
            return snapshot.units.Single(unit => unit.id == id);
        }

        private static CombatScenario Encounter(bool first)
        {
            CombatScenario scenario = Load();
            scenario.id = first ? "test-consumption-first" : "test-consumption-second";
            CombatUnitConfig mage = Unit(scenario, "mage");
            CombatUnitConfig enemy = Unit(scenario, "enemy_low");
            mage.hp = first ? 70 : 100;
            mage.mp = first ? 6 : 30;
            mage.initialReadyTime = first ? 20 : 0;
            mage.instructions[0].condition.terms[0].threshold = 0.8;
            if (!first) mage.instructions = new[] { mage.instructions[1] };
            enemy.hp = 18;
            enemy.initialReadyTime = first ? 30 : 0;
            scenario.units = new[] { mage, enemy };
            if (first) mage.statuses = new[]
            {
                new CombatStatusConfig { id = "short-guard", kind = "guard", magnitude = 0, appliedAt = 0, expiresAt = 25 },
                new CombatStatusConfig { id = "carried", kind = "empowered", magnitude = 0, appliedAt = 0, expiresAt = 200 }
            };
            return scenario;
        }

        private static CombatExpeditionConfig Config()
        {
            return new CombatExpeditionConfig
            {
                id = "test-dispatch-unique-id",
                title = "Consecutive encounter resource test",
                parameterNotice = "仅测试参数",
                carryTeam = "friendly",
                encounters = new[]
                {
                    new CombatEncounterConfig
                    {
                        id = "first", waitBefore = 0, scenario = Encounter(true),
                        experiencePool = 3, publicCash = 4,
                        publicDrops = new[] { new CombatLootStack { itemId = "ore", quantity = 2 } }
                    },
                    new CombatEncounterConfig
                    {
                        id = "second", waitBefore = 15, scenario = Encounter(false),
                        experiencePool = 5, publicCash = 7,
                        publicDrops = new[] { new CombatLootStack { itemId = "ore", quantity = 3 } }
                    }
                }
            };
        }

        [Test]
        public void PerEncounterAndBatchDispatch_UseIdenticalBattlesAndRewardRecords()
        {
            var watched = new CombatExpeditionSession(Config());
            watched.RunNextEncounter();
            watched.RunNextEncounter();
            CombatExpeditionReport batch = new CombatExpeditionSession(Config()).RunToEnd();
            Assert.That(JsonUtility.ToJson(watched.GetReport()), Is.EqualTo(JsonUtility.ToJson(batch)));
            Assert.That(batch.encounters.Length, Is.EqualTo(2));
            Assert.That(batch.finished, Is.True);
            Assert.That(batch.stopReason, Is.EqualTo("CompletedAllEncounters"));
            Assert.That(batch.publicCash, Is.EqualTo(11));
            Assert.That(batch.publicDrops.Single(drop => drop.itemId == "ore").quantity, Is.EqualTo(5));
            Assert.That(batch.experience.Sum(grant => grant.amount), Is.EqualTo(8));
        }

        [Test]
        public void TwoEncounterReport_ShowsActualConsumptionAndExplicitIntermissionWait()
        {
            CombatExpeditionReport report = new CombatExpeditionSession(Config()).RunToEnd();
            CombatEncounterReport first = report.encounters[0];
            CombatEncounterReport second = report.encounters[1];
            Assert.That(first.outcome, Is.EqualTo("Victory"));
            Assert.That(Unit(first.battleEnd, "mage").hp, Is.EqualTo(82));
            Assert.That(Unit(first.battleEnd, "mage").mp, Is.Zero);
            Assert.That(second.betweenBattleWait, Is.EqualTo(15));
            Assert.That(second.battleStart.time - first.battleEnd.time, Is.EqualTo(15));
            Assert.That(Unit(second.battleStart, "mage").hp, Is.EqualTo(82));
            Assert.That(Unit(second.battleStart, "mage").mp, Is.Zero);
            Assert.That(Unit(second.afterIntermission, "mage").statuses.Select(status => status.id), Is.EqualTo(new[] { "carried" }));
            Assert.That(Unit(second.battleStart, "mage").statuses[0].expiresAt, Is.EqualTo(200));
            Assert.That(Unit(second.battleEnd, "mage").hp, Is.LessThan(82));
            Assert.That(Unit(second.battleEnd, "mage").mp, Is.Zero);
            Assert.That(second.resultId, Is.Not.EqualTo(first.resultId));
            Assert.That(second.expeditionId, Is.EqualTo(report.expeditionId));
            Assert.That(first.stopReason, Is.Not.Empty);
            Assert.That(second.stopReason, Is.Not.Empty);
        }

        [Test]
        public void ReportReadsAndCompletedCalls_DoNotAwardAgainOrMutateSettledResults()
        {
            var session = new CombatExpeditionSession(Config());
            session.RunToEnd();
            string settled = JsonUtility.ToJson(session.GetReport());
            CombatExpeditionReport external = session.GetReport();
            external.publicCash = 9999;
            external.publicDrops[0].quantity = 9999;
            external.experience[0].amount = 9999;
            external.encounters[0].publicDrops[0].quantity = 8888;
            external.encounters[0].battleEnd.units[0].hp = 1;
            external.encounters[0].steps[0].message = "changed by caller";
            Assert.That(session.RunNextEncounter(), Is.Null);
            Assert.That(JsonUtility.ToJson(session.RunToEnd()), Is.EqualTo(settled));
            Assert.That(JsonUtility.ToJson(session.GetReport()), Is.EqualTo(settled));
        }

        [Test]
        public void Defeat_StopsBeforeTheNextEncounterAndHasNoVictoryRewards()
        {
            CombatExpeditionConfig config = Config();
            CombatScenario opening = config.encounters[0].scenario;
            Unit(opening, "mage").hp = 1;
            Unit(opening, "mage").initialReadyTime = 20;
            Unit(opening, "enemy_low").initialReadyTime = 0;
            var session = new CombatExpeditionSession(config);
            CombatExpeditionReport report = session.RunToEnd();
            Assert.That(report.encounters.Length, Is.EqualTo(1));
            Assert.That(report.finished, Is.True);
            Assert.That(report.encounters[0].winnerTeam, Is.EqualTo("hostile"));
            Assert.That(report.encounters[0].rewardsEligible, Is.False);
            Assert.That(report.publicCash, Is.Zero);
            Assert.That(report.publicDrops, Is.Empty);
            Assert.That(report.experience, Is.Empty);
            Assert.That(report.stopReason, Does.Contain("Battle"));
        }

        [Test]
        public void Stalemate_StopsBeforeTheNextEncounterAndHasNoVictoryRewards()
        {
            CombatExpeditionConfig config = Config();
            foreach (CombatUnitConfig unit in config.encounters[0].scenario.units)
            {
                unit.instructions = new CombatInstruction[0];
                unit.statuses = new CombatStatusConfig[0];
            }
            CombatExpeditionReport report = new CombatExpeditionSession(config).RunToEnd();
            Assert.That(report.encounters.Length, Is.EqualTo(1));
            Assert.That(report.encounters[0].outcome, Is.EqualTo("Stalemate"));
            Assert.That(report.encounters[0].rewardsEligible, Is.False);
            Assert.That(report.publicCash, Is.Zero);
            Assert.That(report.publicDrops, Is.Empty);
            Assert.That(report.experience, Is.Empty);
        }

        [Test]
        public void MissingPlayerTeam_DefaultsToCarriedTeamEvenWhenAnEnemyIsConfiguredFirst()
        {
            CombatExpeditionConfig config = Config();
            foreach (CombatEncounterConfig encounter in config.encounters)
            {
                encounter.scenario.playerTeam = null;
                encounter.scenario.units = encounter.scenario.units.Reverse().ToArray();
            }
            CombatExpeditionReport report = new CombatExpeditionSession(config).RunToEnd();
            Assert.That(report.encounters.Length, Is.EqualTo(2));
            Assert.That(report.encounters.All(encounter => encounter.outcome == "Victory" && encounter.winnerTeam == "friendly"), Is.True);
            Assert.That(report.publicCash, Is.EqualTo(11));
            Assert.That(config.encounters[0].scenario.playerTeam, Is.Null, "Normalization must not edit the caller's input.");
        }

        [Test]
        public void CarryRosterAndMaximumResources_AreValidatedBeforeAnyEncounterStarts()
        {
            CombatExpeditionConfig config = Config();
            Unit(config.encounters[1].scenario, "mage").maxMp++;
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
            config = Config();
            Unit(config.encounters[1].scenario, "mage").maxHp++;
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
            config = Config();
            Unit(config.encounters[1].scenario, "mage").id = "unexpected-replacement";
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
        }

        [Test]
        public void CarryGridMismatch_IsRejectedBeforeTheFirstBattleCanSettleRewards()
        {
            CombatExpeditionConfig config = Config();
            Unit(config.encounters[1].scenario, "mage").column = 0;
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
        }

        [Test]
        public void ExpeditionInput_IsCopiedAndInvalidFirstWaitIsRejected()
        {
            CombatExpeditionConfig config = Config();
            var session = new CombatExpeditionSession(config);
            config.encounters[0].publicCash = 999;
            config.encounters[0].scenario.units[0].hp = 1;
            CombatExpeditionReport report = session.RunToEnd();
            Assert.That(report.encounters[0].publicCash, Is.EqualTo(4));
            Assert.That(report.encounters[0].battleStart.units[0].hp, Is.EqualTo(70));
            config = Config();
            config.encounters[0].waitBefore = 1;
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
            config = Config();
            config.encounters[1].id = config.encounters[0].id;
            Assert.Throws<ArgumentException>(() => new CombatExpeditionSession(config));
        }
    }
}
