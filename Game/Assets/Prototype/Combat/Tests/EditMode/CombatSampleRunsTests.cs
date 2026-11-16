using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatSampleRunsTests
    {
        private static T Load<T>(string name)
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", name)));
        }

        private static CombatUnitConfig Unit(CombatScenario scenario, string id)
        {
            return scenario.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatRunResult run, string id)
        {
            return run.snapshot.units.Single(unit => unit.id == id);
        }

        private static void AssertOnlyPriorityChanged(CombatComparisonReport report)
        {
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            Unit(normalized, "mage").instructions = CombatSession.CopyScenario(report.baselineConfiguration)
                .units.Single(unit => unit.id == "mage").instructions;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(Unit(report.variantConfiguration, "mage").instructions.Select(rule => rule.id),
                Is.EqualTo(Unit(report.baselineConfiguration, "mage").instructions.Reverse().Select(rule => rule.id)));
        }

        [Test]
        public void Ambush_OnlyPriorityChangesAndEarlyKillPreventsTheLethalEnemyAction()
        {
            CombatComparisonReport report = CombatSampleRuns.ComparePriority(Load<CombatScenario>("round2_ambush.json"));
            AssertOnlyPriorityChanged(report);
            Assert.That(report.firstDivergence.recordIndex, Is.EqualTo(1));
            Assert.That(report.firstDivergence.baseline.actionId, Is.EqualTo("first_aid"));
            Assert.That(report.firstDivergence.variant.actionId, Is.EqualTo("basic_attack"));
            Assert.That(report.baseline.steps.Any(step => step.actorId == "enemy_low" && step.status == "Executed"), Is.True);
            Assert.That(report.variant.steps.Any(step => step.actorId == "enemy_low" && step.status == "Executed"), Is.False);
            Assert.That(Unit(report.baseline, "ally").hp, Is.Zero);
            Assert.That(Unit(report.variant, "ally").hp, Is.GreaterThan(0));
            Assert.That(report.observableImpact, Does.Contain("enemy_low"));
        }

        [Test]
        public void Pressure_OnlyPriorityChangesAndConditionalHealingPreservesTheTeammate()
        {
            CombatComparisonReport report = CombatSampleRuns.ComparePriority(Load<CombatScenario>("round2_pressure.json"));
            AssertOnlyPriorityChanged(report);
            Assert.That(report.firstDivergence.recordIndex, Is.EqualTo(1));
            Assert.That(Unit(report.baseline, "ally").hp, Is.GreaterThan(0));
            Assert.That(Unit(report.variant, "ally").hp, Is.Zero);
            Assert.That(Unit(report.baseline, "mage").mp, Is.LessThan(Unit(report.variant, "mage").mp));
            Assert.That(CombatSampleRuns.FormatComparisonMarkdown(report), Does.Contain("第一次行动分歧"));
        }

        [Test]
        public void IntelligenceComparison_ChangesOnlyIntelligenceAndUnlocksOneUsefulEmergencyRule()
        {
            CombatComparisonReport report = CombatSampleRuns.CompareIntelligenceCapacity(
                Load<CombatScenario>("round2_capacity.json"));
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            Unit(normalized, "mage").intelligence = Unit(report.baselineConfiguration, "mage").intelligence;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(Unit(report.baseline, "mage").instructionCapacity, Is.EqualTo(2));
            Assert.That(Unit(report.variant, "mage").instructionCapacity, Is.EqualTo(3));
            Assert.That(Unit(report.baselineConfiguration, "mage").instructions.All(rule => rule.condition.terms.Length == 1), Is.True,
                "The capacity experiment must not be confounded by newly unlocked condition complexity.");
            Assert.That(Unit(report.baseline, "mage").instructions[2].capacityAllowed, Is.False);
            Assert.That(Unit(report.variant, "mage").instructions[2].capacityAllowed, Is.True);
            Assert.That(report.firstDivergence.baseline.status, Is.EqualTo("Skipped"));
            Assert.That(report.firstDivergence.variant.actionId, Is.EqualTo("first_aid"));
            Assert.That(report.variant.steps.Any(step => step.attempts.Any(attempt =>
                attempt.instructionId == "extra_emergency" && attempt.outcome == "Selected")), Is.True);
            Assert.That(Unit(report.variant, "ally").hp, Is.GreaterThan(Unit(report.baseline, "ally").hp));
        }

        [Test]
        public void EquipmentComparison_OnlyDamageBonusChangesAndSavesOneAttack()
        {
            CombatComparisonReport report = CombatSampleRuns.CompareEquipment(Load<CombatScenario>("round2_equipment.json"));
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            Unit(normalized, "mage").equipmentDamageBonus = Unit(report.baselineConfiguration, "mage").equipmentDamageBonus;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(report.baseline.steps.Count(step => step.actorId == "mage" && step.status == "Executed"), Is.EqualTo(3));
            Assert.That(report.variant.steps.Count(step => step.actorId == "mage" && step.status == "Executed"), Is.EqualTo(2));
            Assert.That(report.firstDivergence.baseline.effects[0].hpDelta,
                Is.GreaterThan(report.firstDivergence.variant.effects[0].hpDelta));
        }

        [Test]
        public void ActualExpeditionJson_MatchesPerEncounterDispatchAndBatchDispatch()
        {
            CombatExpeditionConfig config = Load<CombatExpeditionConfig>("round2_expedition.json");
            var watched = new CombatExpeditionSession(config);
            while (!watched.GetReport().finished) watched.RunNextEncounter();
            CombatExpeditionReport batch = new CombatExpeditionSession(config).RunToEnd();
            Assert.That(JsonUtility.ToJson(watched.GetReport()), Is.EqualTo(JsonUtility.ToJson(batch)));
            Assert.That(batch.encounters.Length, Is.EqualTo(2));
            CombatEncounterReport first = batch.encounters[0];
            CombatEncounterReport second = batch.encounters[1];
            Assert.That(second.betweenBattleWait, Is.EqualTo(15));
            foreach (CombatUnitSnapshot unit in first.battleEnd.units.Where(unit => unit.team == config.carryTeam))
            {
                CombatUnitSnapshot carried = second.battleStart.units.Single(value => value.id == unit.id);
                Assert.That(carried.hp, Is.EqualTo(unit.hp));
                Assert.That(carried.mp, Is.EqualTo(unit.mp));
                Assert.That(carried.statuses.All(status => status.expiresAt > second.battleStart.time), Is.True);
            }
            Assert.That(batch.experience.Sum(grant => grant.amount), Is.EqualTo(
                batch.encounters.Sum(encounter => encounter.experience.Sum(grant => grant.amount))));
        }
    }
}
