using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatMageSamplesTests
    {
        private static CombatScenario Load(string name)
        { return JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", name))); }
        private static CombatUnitConfig Mage(CombatScenario config)
        { return config.units.Single(unit => unit.id == "mage"); }

        [TestCase("round4_meditation.json", "meditation")]
        [TestCase("round4_meditation_risk.json", "meditation")]
        [TestCase("round4_area.json", "area")]
        [TestCase("round4_cleanse.json", "cleanse")]
        [TestCase("round4_control.json", "control")]
        public void FourthRoundComparisons_EditOneFieldAndReplayTheRealSessions(string filename, string kind)
        {
            CombatScenario config = Load(filename);
            string sourceBefore = JsonUtility.ToJson(config);
            CombatComparisonReport report = kind == "area" ? CombatMageSamples.CompareArea(config) :
                kind == "cleanse" ? CombatMageSamples.CompareCleanse(config) :
                kind == "control" ? CombatMageSamples.CompareControl(config) :
                filename == "round4_meditation_risk.json" ? CombatMageSamples.CompareMeditation(config, .35) : CombatMageSamples.CompareMeditation(config);
            CombatScenario normalized = CombatSession.CopyScenario(report.variantConfiguration);
            if (kind == "area") Mage(normalized).instructions[0].actionId = Mage(report.baselineConfiguration).instructions[0].actionId;
            else Mage(normalized).instructions[0].condition.terms[0].threshold = Mage(report.baselineConfiguration).instructions[0].condition.terms[0].threshold;
            Assert.That(JsonUtility.ToJson(normalized), Is.EqualTo(JsonUtility.ToJson(report.baselineConfiguration)));
            Assert.That(JsonUtility.ToJson(config), Is.EqualTo(sourceBefore));
            Assert.That(report.onlyDeclaredSettingChanged, Is.True);
            Assert.That(report.firstDivergence, Is.Not.Null);
            Assert.That(report.firstDivergence.baseline.actionId, Is.Not.EqualTo(report.firstDivergence.variant.actionId));
            Assert.That(report.observableImpact, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(report.baseline.snapshot), Is.Not.EqualTo(JsonUtility.ToJson(report.variant.snapshot)));
            foreach (CombatScenario branch in new[] { report.baselineConfiguration, report.variantConfiguration })
            {
                var stepped = new CombatSession(branch); var automated = new CombatSession(branch);
                while (!stepped.GetSnapshot().finished) stepped.Step();
                CombatRunResult run = automated.RunToCompletion();
                Assert.That(stepped.ExportLogText(), Is.EqualTo(automated.ExportLogText()));
                Assert.That(JsonUtility.ToJson(stepped.GetSnapshot()), Is.EqualTo(JsonUtility.ToJson(run.snapshot)));
                Assert.That(run.outcome, Is.EqualTo("Victory").Or.EqualTo("Defeat"));
                Assert.That(run.stopReason, Is.EqualTo("LastTeamStanding"));
            }
        }

        [Test]
        public void MageLibrary_HasRealPerUnitSkillsLimitedSlotsAndALowManaOpening()
        {
            CombatScenario config = Load("round4_mage_library.json"); var session = new CombatSession(config);
            CombatUnitSnapshot mage = session.GetSnapshot().units.Single(unit => unit.id == "mage");
            CombatUnitSnapshot ally = session.GetSnapshot().units.Single(unit => unit.id == "ally");
            Assert.That(mage.instructionCapacity, Is.EqualTo(5)); Assert.That(ally.instructionCapacity, Is.EqualTo(2));
            Assert.That(mage.conditionTermCapacity, Is.EqualTo(3)); Assert.That(ally.conditionTermCapacity, Is.EqualTo(1));
            Assert.That(mage.mp, Is.EqualTo(4)); Assert.That(mage.tags, Does.Contain("caster"));
            CombatActionConfig[] learned = session.GetAvailableActions("mage");
            Assert.That(learned.Select(action => action.effectKind).Distinct(), Is.EquivalentTo(new[] { "damage", "heal", "restore_mp", "drain_mp", "apply_status", "cleanse", "dispel" }));
            Assert.That(learned.Any(action => action.id == "mage_meditate" && !action.isMagic), Is.True);
            CombatStepResult opening = session.Step(); Assert.That(opening.actionId, Is.EqualTo("mage_meditate"));
            Assert.That(opening.mpAfter, Is.GreaterThan(opening.mpBefore)); Assert.That(opening.actualWait, Is.GreaterThan(0));
            CombatActionConfig[] allySkills = session.GetAvailableActions("ally");
            Assert.That(allySkills.Any(action => action.id == "mage_fire_rain"), Is.False);
        }

        [Test]
        public void EveryLearnedMageSkill_ExecutesAgainstProductionStateAndKeepsRealHpInItsEffects()
        {
            CombatScenario library = Load("round4_mage_library.json");
            CombatMageSkillProof[] proofs = CombatMageSamples.RunSkillProofs(library);
            string[] learned = new CombatSession(library).GetAvailableActions("mage").Select(action => action.id).ToArray();
            Assert.That(proofs.Select(proof => proof.actionId), Is.EquivalentTo(learned));
            foreach (CombatMageSkillProof proof in proofs)
            {
                Assert.That(proof.firstStep.status, Is.EqualTo("Executed"), proof.actionId);
                Assert.That(proof.firstStep.actionId, Is.EqualTo(proof.actionId));
                Assert.That(proof.firstStep.effects, Is.Not.Empty, proof.actionId);
                Assert.That(proof.firstStep.actualWait, Is.GreaterThan(0), proof.actionId);
                Assert.That(proof.before.units.Any(before =>
                {
                    CombatUnitSnapshot after = proof.after.units.Single(unit => unit.id == before.id);
                    return before.hp != after.hp || before.mp != after.mp || before.baseVulnerable != after.baseVulnerable ||
                        !before.statuses.Select(status => JsonUtility.ToJson(status)).SequenceEqual(after.statuses.Select(status => JsonUtility.ToJson(status)));
                }), Is.True, proof.actionId);
                foreach (CombatEffect effect in proof.firstStep.effects)
                {
                    CombatUnitSnapshot before = proof.before.units.Single(unit => unit.id == effect.targetId);
                    CombatUnitSnapshot after = proof.after.units.Single(unit => unit.id == effect.targetId);
                    Assert.That(effect.hpBefore, Is.EqualTo(before.hp), proof.actionId);
                    Assert.That(effect.hpAfter, Is.EqualTo(after.hp), proof.actionId);
                    Assert.That(effect.hpDelta, Is.EqualTo(effect.hpAfter - effect.hpBefore));
                }
                if (proof.effectKind == "apply_status")
                    Assert.That(proof.after.units.Any(unit => unit.statuses.Any(status => status.appliedAt == proof.firstStep.time)), Is.True);
            }
            Assert.That(proofs.Where(proof => proof.actionId == "mage_silence" || proof.actionId == "mage_freeze").All(proof => proof.controlObserved), Is.True,
                "Control skills need later actual blocked-action evidence, not only a stored status.");
        }

        [Test]
        public void NewSkillIds_DoNotRedefineOldActionsAndSharedFourthRoundDefinitionsAreEquivalent()
        {
            string[] names = { "default.json", "round2_ambush.json", "round2_pressure.json", "round2_equipment.json",
                "round3_tactics.json", "round3_mp_reserve.json", "round3_focus_mark.json",
                "round4_mage_library.json", "round4_meditation.json", "round4_meditation_risk.json", "round4_area.json", "round4_cleanse.json", "round4_control.json" };
            CombatScenario[] scenarios = names.Select(Load).ToArray();
            foreach (CombatActionConfig action in scenarios.Skip(7).SelectMany(scenario => scenario.actions))
                foreach (CombatActionConfig other in scenarios.SelectMany(scenario => scenario.actions).Where(other => other.id == action.id))
                    Assert.That(CombatSession.ActionDefinitionsEquivalent(action, other), Is.True, action.id);
        }

        [Test]
        public void FourthRoundExpedition_CarriesActualManaAndValidNewStatesWithoutRepeatingRewards()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "round4_expedition.json");
            CombatExpeditionConfig config = JsonUtility.FromJson<CombatExpeditionConfig>(File.ReadAllText(path));
            var expedition = new CombatExpeditionSession(config);
            CombatEncounterReport first = expedition.RunNextEncounter();
            CombatEncounterReport second = expedition.RunNextEncounter();
            Assert.That(first.outcome, Is.EqualTo("Victory")); Assert.That(second.outcome, Is.EqualTo("Victory"));
            Assert.That(second.battleStart.time, Is.EqualTo(first.battleEnd.time + config.encounters[1].waitBefore));
            foreach (CombatUnitSnapshot carried in first.battleEnd.units.Where(unit => unit.team == config.carryTeam))
            {
                CombatUnitSnapshot opening = second.battleStart.units.Single(unit => unit.id == carried.id);
                Assert.That(opening.hp, Is.EqualTo(carried.hp)); Assert.That(opening.mp, Is.EqualTo(carried.mp));
                Assert.That(opening.statuses.Select(status => status.id), Is.EqualTo(carried.statuses.Where(status => status.expiresAt > second.battleStart.time).Select(status => status.id)));
            }
            string settled = JsonUtility.ToJson(expedition.GetReport());
            Assert.That(JsonUtility.ToJson(expedition.GetReport()), Is.EqualTo(settled));
            Assert.That(expedition.RunNextEncounter(), Is.Null);
            Assert.That(JsonUtility.ToJson(expedition.GetReport()), Is.EqualTo(settled));
        }
    }
}
