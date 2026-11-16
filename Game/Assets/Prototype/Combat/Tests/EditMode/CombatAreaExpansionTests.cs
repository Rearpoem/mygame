using System.Linq;
using NUnit.Framework;
using static Prototype.Combat.Tests.CombatExpansionFixture;

namespace Prototype.Combat.Tests
{
    public sealed class CombatAreaExpansionTests
    {
        private static CombatScenario WoundedFormation()
        {
            CombatUnitConfig actor = Unit("actor", "blue", 2, 1); actor.initialReadyTime = 0;
            CombatUnitConfig a = Unit("a", "blue"); a.hp = 20;
            CombatUnitConfig b = Unit("b", "blue", 0, 1); b.hp = 80;
            CombatUnitConfig full = Unit("full", "blue", 0, 2);
            CombatUnitConfig c = Unit("c", "blue", 1); c.hp = 60;
            CombatUnitConfig dead = Unit("dead", "blue", 1, 1); dead.hp = 0;
            return Scenario(actor, a, b, full, c, dead, Unit("foe", "red"));
        }

        [TestCase("single", new[] { "a" })]
        [TestCase("row", new[] { "a", "b" })]
        [TestCase("column", new[] { "a", "c" })]
        [TestCase("all", new[] { "a", "b", "c" })]
        public void GroupHealing_ExpandsTheAnchorWithoutReapplyingItsCondition(string mode, string[] targets)
        {
            CombatScenario config = WoundedFormation();
            Configure(config, new[] { Rule(action: "group_heal", scope: "allies", selector: "lowest_hp_ratio",
                terms: new[] { Term("hp_ratio_at_most", .3) }) }, Action("group_heal", "heal", 15, 6, 40, mode));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetId, Is.EqualTo("a"));
            Assert.That(step.targetIds, Is.EqualTo(targets));
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == "a").affectedTargetIds, Is.EqualTo(targets));
            Assert.That(step.attempts[0].candidates.Single(candidate => candidate.unitId == "b").passesCondition, Is.False);
            Assert.That(step.mpBefore, Is.EqualTo(30)); Assert.That(step.mpAfter, Is.EqualTo(24));
            Assert.That(step.mpCost, Is.EqualTo(6)); Assert.That(step.actualWait, Is.EqualTo(40));
            foreach (CombatUnitConfig unit in config.units)
                Assert.That(Get(session.GetSnapshot(), unit.id).hp, Is.EqualTo(unit.hp + (targets.Contains(unit.id) ? 15 : 0)), unit.id);
            Assert.That(step.effects.Select(effect => effect.targetId), Is.EqualTo(targets));
        }

        [Test]
        public void GroupDamage_StaysOnTheAnchorsTeamEvenWithSeveralEnemyTeams()
        {
            CombatScenario config = Pair();
            Get(config, "a").tags = new[] { "anchor" };
            CombatUnitConfig foreign = Unit("foreign", "violet");
            CombatUnitConfig dead = Unit("dead", "red", 0, 1); dead.hp = 0;
            config.units = config.units.Concat(new[] { foreign, dead }).ToArray();
            Configure(config, new[] { Rule(action: "blast", terms: new[] { Term("has_tag", value: "anchor") }) },
                Action("blast", "damage", 10, 5, 40, "all"));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(70));
            Assert.That(Get(session.GetSnapshot(), "b").hp, Is.EqualTo(30));
            Assert.That(Get(session.GetSnapshot(), "foreign").hp, Is.EqualTo(100));
            Assert.That(Get(session.GetSnapshot(), "dead").hp, Is.Zero);
            Assert.That(Get(session.GetSnapshot(), "actor").mp, Is.EqualTo(25));
        }

        [Test]
        public void AllMode_CannotExpandASelfOnlyScope()
        {
            CombatScenario config = WoundedFormation(); Get(config, "actor").hp = 40;
            Configure(config, new[] { Rule(action: "self_heal", scope: "self") }, Action("self_heal", "heal", 20, mode: "all"));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetIds, Is.EqualTo(new[] { "actor" }));
            Assert.That(Get(session.GetSnapshot(), "actor").hp, Is.EqualTo(60));
            Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(20));
        }

        [Test]
        public void GroupMana_ExcludesFullAndCapacitylessBeforeChargingTheCaster()
        {
            CombatScenario config = WoundedFormation();
            Get(config, "a").mp = 0;
            Get(config, "b").mp = 0; Get(config, "b").maxMp = 0;
            Configure(config, new[] { Rule(action: "mana", scope: "allies") },
                Action("mana", "restore_mp", 12, 5, 40, "all"));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetId, Is.EqualTo("actor"), "A full anchor may identify a group containing another legal recipient.");
            Assert.That(step.targetIds, Is.EqualTo(new[] { "a" }));
            Assert.That(Get(session.GetSnapshot(), "a").mp, Is.EqualTo(12));
            Assert.That(Get(session.GetSnapshot(), "actor").mp, Is.EqualTo(25), "A previously full caster is not inserted after cost payment.");
            Assert.That(Get(session.GetSnapshot(), "b").mp, Is.Zero);
            CombatEffect recovery = step.effects.Single();
            Assert.That(recovery.mpBefore, Is.Zero); Assert.That(recovery.mpAfter, Is.EqualTo(12)); Assert.That(recovery.mpDelta, Is.EqualTo(12));
            Assert.That(recovery.hpBefore, Is.EqualTo(20)); Assert.That(recovery.hpAfter, Is.EqualTo(20));
        }

        [Test]
        public void GroupDrain_PaysOnceAndReportsPrimaryTargetsSeparatelyFromCasterRefunds()
        {
            CombatScenario config = Pair(); Get(config, "actor").mp = 28;
            Get(config, "a").mp = 2; Get(config, "b").mp = 20;
            Configure(config, new[] { Rule(action: "drain") }, Action("drain", "drain_mp", 12, 2, 40, "all"));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.targetIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(step.targetIds, Does.Not.Contain("actor"));
            Assert.That(Get(session.GetSnapshot(), "a").mp, Is.Zero);
            Assert.That(Get(session.GetSnapshot(), "b").mp, Is.EqualTo(8));
            Assert.That(Get(session.GetSnapshot(), "actor").mp, Is.EqualTo(30));
            Assert.That(step.mpCost, Is.EqualTo(2)); Assert.That(step.actualWait, Is.EqualTo(40));
            Assert.That(step.effects.Where(effect => effect.targetId == "a").Sum(effect => effect.mpDelta), Is.EqualTo(-2));
            Assert.That(step.effects.Where(effect => effect.targetId == "b").Sum(effect => effect.mpDelta), Is.EqualTo(-12));
            Assert.That(step.effects.All(effect => effect.hpBefore == effect.hpAfter && effect.hpBefore > 0), Is.True);
        }

        [Test]
        public void GroupAction_WithNoUsefulRecipientFallsBackWithoutCostOrWait()
        {
            CombatScenario config = WoundedFormation();
            foreach (CombatUnitConfig unit in config.units) unit.hp = unit.maxHp;
            Configure(config, new[] { Rule("empty", "empty_heal", "allies"), Rule("fallback") },
                Action("empty_heal", "heal", 20, 9, 90, "all"));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("hit"));
            Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(step.mpAfter, Is.EqualTo(30)); Assert.That(step.actualWait, Is.EqualTo(40));
        }

        [Test]
        public void AreaFieldsAndCoverage_SurviveCopyJsonAndReplayWithoutSharingArrays()
        {
            CombatScenario config = Pair();
            CombatActionConfig blast = Action("blast", "damage", 100, 2, 40, "row"); blast.isMagic = true; blast.damageType = "magic";
            Get(config, "b").row = 0; Get(config, "b").column = 1;
            Configure(config, new[] { Rule(action: "blast") }, blast);
            var session = new CombatSession(config);
            CombatScenario saved = UnityEngine.JsonUtility.FromJson<CombatScenario>(UnityEngine.JsonUtility.ToJson(session.GetScenario()));
            CombatActionConfig exported = session.GetAvailableActions("actor").Single(action => action.id == "blast");
            Assert.That(exported.targetMode, Is.EqualTo("row")); Assert.That(exported.isMagic, Is.True); Assert.That(exported.damageType, Is.EqualTo("magic"));
            exported.targetMode = "single";
            CombatRunResult first = session.RunToCompletion();
            var replay = new CombatSession(saved); CombatRunResult second = replay.RunToCompletion();
            Assert.That(session.ExportLogText(), Is.EqualTo(replay.ExportLogText()));
            Assert.That(first.steps[0].targetIds, Is.EqualTo(new[] { "a", "b" }));
            CombatStepResult copied = CombatSession.CopyStep(first.steps[0]); copied.targetIds[0] = "changed";
            copied.attempts[0].candidates[0].affectedTargetIds[0] = "changed";
            Assert.That(session.GetHistory()[0].targetIds[0], Is.EqualTo("a"));
            Assert.That(session.GetHistory()[0].attempts[0].candidates[0].affectedTargetIds[0], Is.EqualTo("a"));
            Assert.That(second.outcome, Is.EqualTo("Victory"));
        }
    }
}
