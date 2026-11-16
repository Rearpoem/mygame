using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatTacticsSamplesTests
    {
        private static CombatScenario Load(string filename)
        {
            return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", filename)));
        }

        private static CombatUnitConfig Unit(CombatScenario scenario, string id)
        {
            return scenario.units.Single(unit => unit.id == id);
        }

        private static CombatUnitSnapshot Unit(CombatRunResult run, string id)
        {
            return run.snapshot.units.Single(unit => unit.id == id);
        }

        [Test]
        public void MpReserve_ChangesOnlyTheActorThresholdAndKeepsEmergencyHealingAvailable()
        {
            CombatComparisonReport report = CombatTacticsSamples.CompareMpReserve(Load("round3_mp_reserve.json"));
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            Unit(normalized, "mage").instructions[1].condition.terms[0].threshold =
                Unit(report.baselineConfiguration, "mage").instructions[1].condition.terms[0].threshold;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(Unit(report.variantConfiguration, "mage").instructions[1].condition.terms[0].subject, Is.EqualTo("actor"));
            Assert.That(report.firstDivergence, Is.Not.Null);
            Assert.That(report.firstDivergence.baseline.time, Is.EqualTo(40));
            Assert.That(report.firstDivergence.variant.time, Is.EqualTo(40));
            Assert.That(report.firstDivergence.baseline.actionId, Is.Not.EqualTo(report.firstDivergence.variant.actionId));
            Assert.That(report.baseline.steps.Count(step => step.actionId == "tactics_first_aid"), Is.Zero);
            Assert.That(report.variant.steps.Count(step => step.actionId == "tactics_first_aid"), Is.GreaterThan(0));
            Assert.That(Unit(report.baseline, "ally").hp, Is.Zero);
            Assert.That(Unit(report.variant, "ally").hp, Is.GreaterThan(0));
        }

        [Test]
        public void FocusMark_ChangesOnlyTheSelectorAndUsesTheActualAppliedStatus()
        {
            CombatComparisonReport report = CombatTacticsSamples.CompareFocusMark(Load("round3_focus_mark.json"));
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            Unit(normalized, "ally").instructions[0].selector = Unit(report.baselineConfiguration, "ally").instructions[0].selector;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(report.baseline.steps[0].actionId, Is.EqualTo("tactics_focus_mark"));
            Assert.That(report.firstDivergence.baseline.time, Is.EqualTo(15));
            Assert.That(report.firstDivergence.variant.time, Is.EqualTo(15));
            Assert.That(report.firstDivergence.baseline.actorId, Is.EqualTo("ally"));
            Assert.That(report.firstDivergence.baseline.targetId, Is.EqualTo("enemy_decoy"));
            Assert.That(report.firstDivergence.variant.targetId, Is.EqualTo("enemy_caster"));
            Assert.That(Unit(report.baseline, "ally").hp, Is.Zero);
            Assert.That(Unit(report.variant, "ally").hp, Is.GreaterThan(0));
            Assert.That(report.variant.steps.Any(step => step.effects.Any(effect => effect.kind == "apply_status")), Is.True);
        }

        [Test]
        public void DefaultTacticsMaterial_RespectsHighAndLowIntelligenceAndPerUnitSkillMenus()
        {
            CombatScenario config = Load("round3_tactics.json");
            var session = new CombatSession(config);
            CombatSnapshot opening = session.GetSnapshot();
            CombatUnitSnapshot mage = opening.units.Single(unit => unit.id == "mage");
            CombatUnitSnapshot ally = opening.units.Single(unit => unit.id == "ally");
            Assert.That(mage.intelligence, Is.EqualTo(40));
            Assert.That(ally.intelligence, Is.EqualTo(10));
            Assert.That(mage.instructionCapacity, Is.EqualTo(5));
            Assert.That(ally.instructionCapacity, Is.EqualTo(2));
            Assert.That(mage.conditionTermCapacity, Is.EqualTo(3));
            Assert.That(ally.conditionTermCapacity, Is.EqualTo(1));
            Assert.That(mage.instructions.All(rule => rule.capacityAllowed && rule.actionAvailable), Is.True);
            Assert.That(ally.instructions.All(rule => rule.capacityAllowed && rule.actionAvailable), Is.True);
            Assert.That(session.GetAvailableActions("mage").Any(action => action.id == "tactics_caster_strike"), Is.False);
            Assert.That(session.GetAvailableActions("ally").Any(action => action.id == "tactics_power_attack"), Is.False);
            CombatRunResult run = session.RunToCompletion();
            var stepped = new CombatSession(config);
            while (!stepped.GetSnapshot().finished) stepped.Step();
            Assert.That(stepped.ExportLogText(), Is.EqualTo(session.ExportLogText()));
            Assert.That(JsonUtility.ToJson(stepped.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(run.snapshot)));
        }

        [TestCase("round3_tactics.json")]
        [TestCase("round3_mp_reserve.json")]
        [TestCase("round3_focus_mark.json")]
        public void ThirdRoundActionIds_CannotSilentlyRedefineAnOlderSkill(string filename)
        {
            CombatScenario next = Load(filename);
            string[] oldNames = { "default.json", "round2_ambush.json", "round2_pressure.json", "round2_equipment.json",
                "round3_tactics.json", "round3_mp_reserve.json", "round3_focus_mark.json" };
            foreach (CombatActionConfig action in next.actions)
            {
                Assert.That(action.id, Does.StartWith("tactics_"));
                foreach (string oldName in oldNames)
                {
                    CombatActionConfig old = Load(oldName).actions.FirstOrDefault(value => value.id == action.id);
                    if (old != null) Assert.That(JsonUtility.ToJson(action), Is.EqualTo(JsonUtility.ToJson(old)), action.id);
                }
            }
        }
    }
}
