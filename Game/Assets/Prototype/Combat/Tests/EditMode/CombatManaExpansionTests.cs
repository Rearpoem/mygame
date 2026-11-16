using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.Combat.Tests
{
    public sealed class CombatManaExpansionTests
    {
        private static CombatScenario Duel()
        {
            CombatScenario config = JsonUtility.FromJson<CombatScenario>(File.ReadAllText(Path.Combine(
                Application.streamingAssetsPath, "Prototype", "Combat", "default.json")));
            CombatUnitConfig mage = config.units.Single(unit => unit.id == "mage");
            CombatUnitConfig enemy = config.units.Single(unit => unit.id == "enemy_low");
            mage.hp = 100;
            mage.mp = 0;
            enemy.hp = 18;
            enemy.initialReadyTime = 10;
            enemy.instructions = new CombatInstruction[0];
            config.units = new[] { mage, enemy };
            return config;
        }

        private static CombatUnitConfig Unit(CombatScenario config, string id)
        { return config.units.Single(unit => unit.id == id); }
        private static CombatUnitSnapshot Unit(CombatSnapshot snapshot, string id)
        { return snapshot.units.Single(unit => unit.id == id); }
        private static CombatInstruction Rule(string id, string action, string scope = "self", string kind = "always", double threshold = 0)
        {
            return new CombatInstruction { id = id, label = id, actionId = action, scope = scope, selector = "configured_order",
                condition = new CombatCondition { operation = "all", terms = new[] { new CombatConditionTerm { kind = kind, threshold = threshold } } } };
        }
        private static CombatActionConfig Effect(string id, string kind, int amount, int cost = 0, double wait = 40)
        { return new CombatActionConfig { id = id, displayName = id, effectKind = kind, magnitude = amount, mpCost = cost, baseWait = wait }; }
        private static void Configure(CombatScenario config, CombatInstruction[] instructions, params CombatActionConfig[] actions)
        {
            config.actions = config.actions.Concat(actions).ToArray();
            CombatUnitConfig mage = Unit(config, "mage");
            mage.instructions = instructions;
            mage.availableActionIds = new[] { "basic_attack", "first_aid" }.Concat(actions.Select(action => action.id)).ToArray();
            mage.availableActionIdsExplicit = true;
        }

        [TestCase(0, 12, 12)]
        [TestCase(25, 12, 30)]
        [TestCase(29, int.MaxValue, 30)]
        public void Meditation_RestoresActualMpClampsToTheCapAndPaysOneWait(int opening, int amount, int expected)
        {
            CombatScenario config = Duel();
            Unit(config, "mage").mp = opening;
            Configure(config, new[] { Rule("meditate", "test_meditate") }, Effect("test_meditate", "restore_mp", amount));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.status, Is.EqualTo("Executed"));
            Assert.That(step.mpBefore, Is.EqualTo(opening));
            Assert.That(step.mpAfter, Is.EqualTo(expected));
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(expected));
            Assert.That(step.mpCost, Is.Zero);
            Assert.That(step.actualWait, Is.EqualTo(40));
            Assert.That(step.nextActionTime, Is.EqualTo(40));
            Assert.That(step.effects.All(effect => effect.hpBefore == effect.hpAfter && effect.hpDelta == 0), Is.True,
                "An MP effect must not look like a casualty to older report readers.");
        }

        [Test]
        public void Meditation_AtFullMpSkipsWithoutPayingItsCostOrWait()
        {
            CombatScenario config = Duel();
            Unit(config, "mage").mp = 30;
            Configure(config, new[] { Rule("full_meditate", "test_meditate"), Rule("fallback", "basic_attack", "enemies") },
                Effect("test_meditate", "restore_mp", 12, 2, 40));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(30));
            Assert.That(step.actualWait, Is.EqualTo(60), "Only the executed fallback pays WAIT.");
        }

        [Test]
        public void Meditation_WithoutAnMpCapacityHasNoEffectAndDoesNotSpin()
        {
            CombatScenario config = Duel();
            Unit(config, "mage").maxMp = 0;
            Configure(config, new[] { Rule("no_mana", "test_meditate") }, Effect("test_meditate", "restore_mp", 12));
            var session = new CombatSession(config);
            CombatRunResult run = session.RunToCompletion(30, 1000);
            Assert.That(run.outcome, Is.EqualTo("Stalemate"));
            Assert.That(run.executedActionCount, Is.Zero);
            Assert.That(Unit(run.snapshot, "mage").mp, Is.Zero);
            Assert.That(run.steps.Length, Is.LessThan(30));
        }

        [Test]
        public void LowMana_MeditatesBeforeTheSameConfiguredSpellCanActuallyFire()
        {
            CombatScenario config = Duel();
            Configure(config, new[] { Rule("cast", "test_spell", "enemies"), Rule("recover", "test_meditate") },
                Effect("test_spell", "damage", 18, 10, 60), Effect("test_meditate", "restore_mp", 12));
            config.actions.Single(action => action.id == "test_spell").isMagic = true;
            config.actions.Single(action => action.id == "test_spell").damageType = "magic";
            var session = new CombatSession(config);
            CombatRunResult run = session.RunToCompletion();
            CombatStepResult[] actions = run.steps.Where(step => step.status == "Executed").ToArray();
            Assert.That(actions.Select(step => step.actionId), Is.EqualTo(new[] { "test_meditate", "test_spell" }));
            Assert.That(actions[0].attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(actions[0].mpAfter, Is.EqualTo(12));
            Assert.That(actions[1].time, Is.EqualTo(40));
            Assert.That(actions[1].mpBefore, Is.EqualTo(12));
            Assert.That(actions[1].mpAfter, Is.EqualTo(2));
            Assert.That(run.outcome, Is.EqualTo("Victory"));
            Assert.That(Unit(run.snapshot, "enemy_low").hp, Is.Zero);
        }

        [Test]
        public void ManaAndRefreshOnlyCycle_StopsWithAReasonInsteadOfRunningForever()
        {
            CombatScenario config = Duel();
            Unit(config, "mage").mp = 30;
            var shield = Effect("test_refresh", "apply_status", 0, 10, 20);
            shield.statusId = "test_guard";
            shield.statusKind = "guard";
            shield.statusMagnitude = 1;
            shield.statusDuration = 80;
            Configure(config, new[] { Rule("refresh", "test_refresh", kind: "mp_ratio_at_least", threshold: 0.9),
                Rule("recover", "test_meditate") }, shield, Effect("test_meditate", "restore_mp", 10, 0, 20));
            CombatRunResult run = new CombatSession(config).RunToCompletion(50, 2000);
            Assert.That(run.outcome, Is.EqualTo("Stalemate"));
            Assert.That(run.stopReason, Is.Not.Empty);
            Assert.That(run.steps.Length, Is.LessThan(50));
            Assert.That(run.steps.Any(step => step.actionId == "test_refresh"), Is.True);
            Assert.That(run.steps.Any(step => step.actionId == "test_meditate"), Is.True);
            Assert.That(Unit(run.snapshot, "enemy_low").hp, Is.EqualTo(18));
        }

        [TestCase(25, 2, 27)]
        [TestCase(29, 10, 30)]
        public void ManaDrain_OnlyReturnsManaActuallyRemovedAndClampsCasterGain(int casterMp, int targetMp, int expectedCaster)
        {
            CombatScenario config = Duel();
            Unit(config, "mage").mp = casterMp;
            Unit(config, "enemy_low").mp = targetMp;
            Configure(config, new[] { Rule("drain", "test_drain", "enemies") }, Effect("test_drain", "drain_mp", 12));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("test_drain"));
            Assert.That(Unit(session.GetSnapshot(), "enemy_low").mp, Is.Zero);
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(expectedCaster));
            Assert.That(step.effects.All(effect => effect.hpBefore == effect.hpAfter && effect.hpDelta == 0), Is.True);
        }

        [Test]
        public void ManaDrain_CannotDrainTheCasterAndFallsBackWithoutAHiddenTransaction()
        {
            CombatScenario config = Duel();
            Unit(config, "mage").mp = 10;
            Configure(config, new[] { Rule("self_drain", "test_drain"), Rule("fallback", "basic_attack", "enemies") },
                Effect("test_drain", "drain_mp", 12));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("basic_attack"));
            Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(10));
            Assert.That(Unit(session.GetSnapshot(), "enemy_low").mp, Is.EqualTo(10));
        }

        [Test]
        public void ManaDrain_AnEmptyTargetFallsBackWithoutPayingTheDrainCost()
        {
            CombatScenario config = Duel(); Unit(config, "mage").mp = 30; Unit(config, "enemy_low").mp = 0;
            Configure(config, new[] { Rule("empty_drain", "test_drain", "enemies"), Rule("fallback", "basic_attack", "enemies") },
                Effect("test_drain", "drain_mp", 12, 3, 90));
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("basic_attack")); Assert.That(step.mpAfter, Is.EqualTo(30));
            Assert.That(step.actualWait, Is.EqualTo(60)); Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
        }

        [Test]
        public void ManaDrain_AFullCasterCanStillDepleteTheTargetsResource()
        {
            CombatScenario config = Duel(); Unit(config, "mage").mp = 30; Unit(config, "enemy_low").mp = 5;
            Configure(config, new[] { Rule("drain", "test_drain", "enemies") }, Effect("test_drain", "drain_mp", 12));
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(Unit(session.GetSnapshot(), "mage").mp, Is.EqualTo(30)); Assert.That(Unit(session.GetSnapshot(), "enemy_low").mp, Is.Zero);
            Assert.That(step.effects.Single(effect => effect.targetId == "enemy_low").mpDelta, Is.EqualTo(-5));
            Assert.That(step.effects.Single(effect => effect.targetId == "mage").mpDelta, Is.Zero);
        }
    }
}
