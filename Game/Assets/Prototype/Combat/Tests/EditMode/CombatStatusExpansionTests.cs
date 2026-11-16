using System.Linq;
using NUnit.Framework;
using static Prototype.Combat.Tests.CombatExpansionFixture;

namespace Prototype.Combat.Tests
{
    public sealed class CombatStatusExpansionTests
    {
        private static CombatStatusConfig[] AllStatuses()
        {
            return new[] { Status("guard", "guard"), Status("power", "empowered"), Status("barrier", "magic_guard"),
                Status("speed", "haste"), Status("mark", "vulnerable"), Status("weak", "weakened"),
                Status("delay", "slow"), Status("mute", "silenced"), Status("bind", "stunned") };
        }

        [TestCase("cleanse", new[] { "guard", "power", "barrier", "speed" }, false)]
        [TestCase("dispel", new[] { "mark", "weak", "delay", "mute", "bind" }, true)]
        public void RemovalEffects_KeepOppositeStatusClassesAndRespectPermanentVulnerability(string kind, string[] remaining, bool baseVulnerable)
        {
            CombatScenario config = Pair(); Get(config, "a").statuses = AllStatuses(); Get(config, "a").vulnerable = true;
            Configure(config, new[] { Rule(action: "remove") }, Action("remove", kind, 0, 3));
            var session = new CombatSession(config);
            CombatStepResult step = session.Step();
            CombatUnitSnapshot target = Get(session.GetSnapshot(), "a");
            Assert.That(step.actionId, Is.EqualTo("remove"));
            Assert.That(target.statuses.Select(status => status.id), Is.EqualTo(remaining));
            Assert.That(target.baseVulnerable, Is.EqualTo(baseVulnerable));
            Assert.That(target.vulnerable, Is.EqualTo(baseVulnerable));
            CombatEffect effect = step.effects.Single();
            Assert.That(effect.removedStatusIds.Length, Is.EqualTo(9 - remaining.Length));
            Assert.That(effect.baseVulnerableBefore, Is.True); Assert.That(effect.baseVulnerableAfter, Is.EqualTo(baseVulnerable));
            Assert.That(effect.hpBefore, Is.EqualTo(80)); Assert.That(effect.hpAfter, Is.EqualTo(80));
            Assert.That(effect.mpBefore, Is.EqualTo(5)); Assert.That(effect.mpAfter, Is.EqualTo(5));
        }

        [TestCase("cleanse")]
        [TestCase("dispel")]
        public void EmptyRemoval_SkipsWithoutSpendingManaOrWait(string kind)
        {
            CombatScenario config = Pair();
            Configure(config, new[] { Rule("empty", "remove"), Rule("fallback") }, Action("remove", kind, 0, 8, 90));
            CombatStepResult step = new CombatSession(config).Step();
            Assert.That(step.actionId, Is.EqualTo("hit")); Assert.That(step.attempts[0].outcome, Is.EqualTo("Skipped"));
            Assert.That(step.mpAfter, Is.EqualTo(30)); Assert.That(step.actualWait, Is.EqualTo(40));
        }

        [Test]
        public void Cleanse_CanRemoveOnlyTheLegacyPermanentVulnerabilityMarker()
        {
            CombatScenario config = Pair(); Get(config, "a").vulnerable = true;
            Configure(config, new[] { Rule(action: "clean") }, Action("clean", "cleanse", 0));
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo("clean")); Assert.That(step.effects.Single().removedStatusIds, Is.Empty);
            Assert.That(Get(session.GetSnapshot(), "a").baseVulnerable, Is.False);
            Assert.That(Get(session.GetSnapshot(), "a").vulnerable, Is.False);
        }

        [TestCase("cleanse", "slow", "guard")]
        [TestCase("dispel", "guard", "slow")]
        public void GroupRemoval_OnlyIncludesTargetsWithTheRelevantStatusClass(string effect, string removedKind, string retainedKind)
        {
            CombatScenario config = Pair();
            Get(config, "a").statuses = new[] { Status("remove", removedKind), Status("keep", retainedKind) };
            Get(config, "b").statuses = new[] { Status("keep", retainedKind) };
            Configure(config, new[] { Rule(action: "remove") }, Action("remove", effect, 0, 3, 40, "all"));
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(step.targetIds, Is.EqualTo(new[] { "a" }));
            Assert.That(Get(session.GetSnapshot(), "a").statuses.Select(status => status.id), Is.EqualTo(new[] { "keep" }));
            Assert.That(Get(session.GetSnapshot(), "b").statuses.Select(status => status.id), Is.EqualTo(new[] { "keep" }));
            Assert.That(step.mpAfter, Is.EqualTo(27));
        }

        [Test]
        public void RefreshingTheSameId_ReplacesMagnitudeAndExpiryWithoutDuplicatingTheStatus()
        {
            CombatScenario config = Pair(); Get(config, "a").statuses = new[] { Status("bind", "slow", 10, 10) };
            CombatActionConfig slow = Action("slow", "apply_status"); slow.statusId = "bind"; slow.statusKind = "slow";
            slow.statusMagnitude = 50; slow.statusDuration = 60;
            Configure(config, new[] { Rule(action: "slow") }, slow);
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            CombatStatusConfig applied = Get(session.GetSnapshot(), "a").statuses.Single();
            Assert.That(applied.id, Is.EqualTo("bind")); Assert.That(applied.magnitude, Is.EqualTo(50));
            Assert.That(applied.appliedAt, Is.Zero); Assert.That(applied.expiresAt, Is.EqualTo(60));
            Assert.That(step.effects.Single().statusId, Is.EqualTo("bind"));
            Assert.That(step.effects.Single().statusExpiresAt, Is.EqualTo(60));
            Assert.That(step.effects.Single().hpBefore, Is.EqualTo(80)); Assert.That(step.effects.Single().hpAfter, Is.EqualTo(80));
        }

        [Test]
        public void StatusExpiry_ReportsTrueHpMpAndReturnsIndependentRemovedIdArrays()
        {
            CombatScenario config = Pair(); Get(config, "actor").initialReadyTime = 30;
            Get(config, "a").statuses = new[] { Status("short", "magic_guard", 10, 10) };
            Configure(config, new[] { Rule() });
            var session = new CombatSession(config); CombatStepResult expiry = session.Step();
            Assert.That(expiry.status, Is.EqualTo("StateExpired")); Assert.That(expiry.time, Is.EqualTo(10));
            CombatEffect effect = expiry.effects.Single(); Assert.That(effect.hpBefore, Is.EqualTo(80)); Assert.That(effect.hpAfter, Is.EqualTo(80));
            Assert.That(effect.mpBefore, Is.EqualTo(5)); Assert.That(effect.mpAfter, Is.EqualTo(5));
            Assert.That(effect.removedStatusIds, Is.EqualTo(new[] { "short" }));
            CombatStepResult copy = CombatSession.CopyStep(expiry); copy.effects[0].removedStatusIds[0] = "changed"; copy.effects[0].mpAfter = 999;
            expiry.effects[0].removedStatusIds[0] = "also-changed";
            Assert.That(session.GetHistory()[0].effects[0].removedStatusIds, Is.EqualTo(new[] { "short" }));
            Assert.That(session.GetHistory()[0].effects[0].mpAfter, Is.EqualTo(5));
        }

        [TestCase(false, "magic", "spell")]
        [TestCase(false, "physical", "spell")]
        [TestCase(true, "physical", "hit")]
        [TestCase(true, "magic", "hit")]
        public void Silence_BlocksTheMagicFlagIndependentlyOfDamageType(bool isMagic, string damageType, string expected)
        {
            CombatScenario config = Pair(); Get(config, "actor").statuses = new[] { Status("silence", "silenced", 0) };
            CombatActionConfig spell = Action("spell", "damage", 5, 3); spell.isMagic = isMagic; spell.damageType = damageType;
            Configure(config, new[] { Rule("spell", "spell"), Rule("fallback") }, spell);
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(step.actionId, Is.EqualTo(expected));
            Assert.That(step.mpAfter, Is.EqualTo(isMagic ? 30 : 27));
            if (isMagic) Assert.That(step.attempts[0].reasonCode, Is.EqualTo("Silenced"));
            Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(isMagic ? 79 : 75));
        }

        [Test]
        public void Stun_SleepsTheActorUntilFixedExpiryThenAllowsItsQueuedAction()
        {
            CombatScenario config = Pair(); Get(config, "a").initialReadyTime = 100;
            Get(config, "actor").statuses = new[] { Status("bind", "stunned", 0, 25) };
            Configure(config, new[] { Rule() });
            var session = new CombatSession(config);
            CombatStepResult skipped = session.Step();
            Assert.That(skipped.status, Is.EqualTo("Skipped")); Assert.That(skipped.controlReason, Is.EqualTo("Stunned"));
            Assert.That(skipped.time, Is.Zero); Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(80));
            CombatStepResult expiry = session.Step(); Assert.That(expiry.status, Is.EqualTo("StateExpired")); Assert.That(expiry.time, Is.EqualTo(25));
            CombatStepResult acted = session.Step(); Assert.That(acted.status, Is.EqualTo("Executed")); Assert.That(acted.time, Is.EqualTo(25));
            Assert.That(acted.actionId, Is.EqualTo("hit")); Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(79));
        }

        [Test]
        public void StunExpiry_DoesNotEraseNormalWaitThatHasNotElapsed()
        {
            CombatScenario config = Pair(); Get(config, "actor").initialReadyTime = 100;
            Get(config, "a").initialReadyTime = 150; Get(config, "b").initialReadyTime = 150;
            Get(config, "actor").statuses = new[] { Status("bind", "stunned", 0, 25) };
            Configure(config, new[] { Rule() });
            var session = new CombatSession(config);
            Assert.That(session.Step().time, Is.EqualTo(25));
            CombatStepResult acted = session.Step(); Assert.That(acted.actorId, Is.EqualTo("actor")); Assert.That(acted.time, Is.EqualTo(100));
        }

        [Test]
        public void AllyCleanse_WakesAControlledTeammateWithoutWaitingForOriginalStatusExpiry()
        {
            CombatScenario config = Pair(); CombatUnitConfig a = Get(config, "a"); a.team = "blue"; a.column = 1;
            a.initialReadyTime = 0; a.statuses = new[] { Status("bind", "stunned", 0, 90) }; a.instructions = new[] { Rule("ally_hit") };
            Configure(config, new[] { Rule(action: "clean", scope: "allies", terms: new[] { Term("has_control") }) }, Action("clean", "cleanse", 0));
            var session = new CombatSession(config);
            Assert.That(session.Step().targetId, Is.EqualTo("a"));
            CombatStepResult ally = session.Step(); Assert.That(ally.actorId, Is.EqualTo("a")); Assert.That(ally.time, Is.Zero);
            Assert.That(ally.status, Is.EqualTo("Executed")); Assert.That(Get(session.GetSnapshot(), "b").hp, Is.EqualTo(39));
        }

        [TestCase(100, 0, 1, 20)]
        [TestCase(0, 100, 1, 80)]
        [TestCase(100, 100, 1, 40)]
        [TestCase(25, 0, .5, 16)]
        public void SpeedStates_ChangeSubsequentActionWaitButNotTheirFixedExpiry(int haste, int slow, double basis, double expectedWait)
        {
            CombatScenario config = Pair(); Get(config, "actor").agilityFactor = basis;
            Get(config, "actor").statuses = new[] { Status("haste", "haste", haste, 90), Status("slow", "slow", slow, 90) };
            Configure(config, new[] { Rule() });
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(step.actualWait, Is.EqualTo(expectedWait).Within(.000001));
            Assert.That(step.baseAgilityFactor, Is.EqualTo(basis));
            Assert.That(step.statusWaitFactor * basis, Is.EqualTo(step.agilityFactor).Within(.000001));
            Assert.That(Get(session.GetSnapshot(), "actor").effectiveAgilityFactor, Is.EqualTo(step.agilityFactor));
            Assert.That(Get(session.GetSnapshot(), "actor").statuses.All(status => status.expiresAt == 90), Is.True);
        }

        [Test]
        public void SpeedExpiryAtReadyTime_ResolvesBeforeTheNextAction()
        {
            CombatScenario config = Pair(); Get(config, "a").initialReadyTime = 100; Get(config, "b").initialReadyTime = 100;
            Get(config, "actor").statuses = new[] { Status("haste", "haste", 100, 20) }; Configure(config, new[] { Rule() });
            var session = new CombatSession(config); Assert.That(session.Step().actualWait, Is.EqualTo(20));
            CombatStepResult expiry = session.Step(); Assert.That(expiry.status, Is.EqualTo("StateExpired")); Assert.That(expiry.time, Is.EqualTo(20));
            CombatStepResult acted = session.Step(); Assert.That(acted.time, Is.EqualTo(20)); Assert.That(acted.actualWait, Is.EqualTo(40));
        }

        [Test]
        public void SelfHaste_UsesThePreActionFactorAndDoesNotRewriteTheAlreadyQueuedWait()
        {
            CombatScenario config = Pair(); Get(config, "a").initialReadyTime = 100; Get(config, "b").initialReadyTime = 100;
            CombatActionConfig haste = Action("haste", "apply_status"); haste.statusId = "speed"; haste.statusKind = "haste";
            haste.statusMagnitude = 100; haste.statusDuration = 200;
            Configure(config, new[] { Rule("haste", "haste", "self"), Rule("fallback") }, haste);
            var session = new CombatSession(config); CombatStepResult first = session.Step();
            Assert.That(first.actualWait, Is.EqualTo(40)); Assert.That(Get(session.GetSnapshot(), "actor").nextActionTime, Is.EqualTo(40));
            session.RemoveInstruction("actor", 0);
            CombatStepResult next = session.Step(); Assert.That(next.time, Is.EqualTo(40)); Assert.That(next.actualWait, Is.EqualTo(20));
        }

        [TestCase("legacy", 87)]
        [TestCase("physical", 87)]
        [TestCase("magic", 91)]
        public void DamageTypes_CombineEquipmentOutputModifiersAndTheApplicableBarriers(string type, int expectedHp)
        {
            CombatScenario config = Pair(); Get(config, "a").hp = 100;
            Get(config, "actor").equipmentDamageBonus = 7; Get(config, "actor").equipmentSourceId = "round4-equipment";
            Get(config, "actor").statuses = new[] { Status("power", "empowered", 3), Status("weak", "weakened", 5) };
            Get(config, "a").statuses = new[] { Status("guard", "guard", 2), Status("barrier", "magic_guard", 4) };
            CombatActionConfig attack = Action("typed", "damage", 10); attack.damageType = type;
            Configure(config, new[] { Rule(action: "typed") }, attack);
            var session = new CombatSession(config); CombatStepResult step = session.Step();
            Assert.That(Get(session.GetSnapshot(), "a").hp, Is.EqualTo(expectedHp));
            Assert.That(step.equipmentDamageBonus, Is.EqualTo(7));
            Assert.That(step.effects.Single().hpDelta, Is.EqualTo(expectedHp - 100));
        }

        [TestCase("heal")]
        [TestCase("restore_mp")]
        public void Weakness_DoesNotReduceHealingOrManaRestoration(string kind)
        {
            CombatScenario config = Pair(); Get(config, "actor").hp = 50; Get(config, "actor").mp = 5;
            Get(config, "actor").statuses = new[] { Status("weak", "weakened", 100) };
            Configure(config, new[] { Rule(action: "recover", scope: "self") }, Action("recover", kind, 20));
            var session = new CombatSession(config); session.Step(); CombatUnitSnapshot actor = Get(session.GetSnapshot(), "actor");
            Assert.That(kind == "heal" ? actor.hp : actor.mp, Is.EqualTo(kind == "heal" ? 70 : 25));
        }

        [Test]
        public void NewStatesAndResourceLoss_CarryAcrossABattleAndExpireByTheExplicitGap()
        {
            CombatScenario config = Pair(); Get(config, "a").hp = 1; Get(config, "b").hp = 0;
            Get(config, "actor").hp = 73; Get(config, "actor").mp = 4; Get(config, "actor").tags = new[] { "caster" };
            Get(config, "actor").statuses = new[] { Status("short", "silenced", 0, 10), Status("long", "haste", 25, 100), Status("weak", "weakened", 0, 100) };
            Configure(config, new[] { Rule() });
            var first = new CombatSession(config); CombatRunResult done = first.RunToCompletion(); Assert.That(done.outcome, Is.EqualTo("Victory"));
            CombatScenario next = CombatSession.CopyScenario(config); next.id = "second"; Get(next, "a").hp = 50;
            // Template resources/statuses must not overwrite carried data, including the expired silence.
            Get(next, "actor").hp = 100; Get(next, "actor").mp = 30;
            var second = new CombatSession(next, done.snapshot, "blue", 15);
            CombatUnitSnapshot carried = Get(second.GetSnapshot(), "actor");
            Assert.That(second.GetSnapshot().time, Is.EqualTo(15)); Assert.That(carried.hp, Is.EqualTo(73)); Assert.That(carried.mp, Is.EqualTo(4));
            Assert.That(carried.statuses.Select(status => status.id), Is.EqualTo(new[] { "long", "weak" }));
            Assert.That(carried.tags, Is.EqualTo(new[] { "caster" })); Assert.That(carried.effectiveAgilityFactor, Is.EqualTo(.8));
            done.snapshot.units.Single(unit => unit.id == "actor").statuses[1].magnitude = 999;
            Assert.That(Get(second.GetSnapshot(), "actor").statuses.Single(status => status.id == "long").magnitude, Is.EqualTo(25));
        }
    }
}
