using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.Expedition;
using UnityEngine;

namespace Prototype.Tactics.Tests
{
    public sealed class TacticsRoundFourTests
    {
        [Test]
        public void MeditationHistoryAndCatalogKeepTheActualNewResourceAndMetadataFields()
        {
            var scenario = Read<CombatScenario>("round4_mage_library.json");
            var workbench = new TacticsWorkbenchSession(scenario);
            var core = new CombatSession(scenario);
            workbench.CommitBattle();
            var step = workbench.StepBattle();
            var expected = core.Step();
            Assert.That(step.actionId, Is.EqualTo("mage_meditate"));
            Assert.That(step.effects.Single().kind, Is.EqualTo("restore_mp"));
            Assert.That(step.effects.Single().mpBefore, Is.EqualTo(4));
            Assert.That(step.effects.Single().mpAfter, Is.EqualTo(16));
            Assert.That(step.effects.Single().mpDelta, Is.EqualTo(12));
            Assert.That(Signature(workbench.GetBattleHistory().Single()), Is.EqualTo(Signature(expected)));
            var exported = workbench.GetAcceptance();
            exported.battleHistory[0].effects[0].mpAfter = 999;
            exported.battleHistory[0].targetIds[0] = "caller-mutated";
            Assert.That(workbench.GetBattleHistory()[0].effects[0].mpAfter, Is.EqualTo(16));
            Assert.That(workbench.GetBattleHistory()[0].targetIds, Is.EqualTo(new[] { "mage" }));
            var catalog = workbench.GetCatalog("mage");
            Assert.That(Signature(catalog.statusKinds), Is.EqualTo(Signature(CombatTacticsCatalog.GetStatusKinds())));
            Assert.That(Signature(catalog.targetModes), Is.EqualTo(Signature(CombatTacticsCatalog.GetTargetModes())));
            Assert.That(Signature(catalog.damageTypes), Is.EqualTo(Signature(CombatTacticsCatalog.GetDamageTypes())));
            Assert.That(Signature(catalog.effectKinds), Is.EqualTo(Signature(CombatTacticsCatalog.GetEffectKinds())));
            Assert.That(catalog.conditions.Single(option => option.id == "mp_at_most").thresholdUnit, Is.EqualTo("points"));
            Assert.That(catalog.conditions.Single(option => option.id == "team_alive_at_least").thresholdUnit, Is.EqualTo("count"));
            Assert.That(catalog.conditions.Single(option => option.id == "wait_remaining_at_most").thresholdUnit, Is.EqualTo("wait"));
            Assert.That(catalog.conditions.Single(option => option.id == "has_status_kind").defaultValue, Is.Not.Empty);
        }

        [Test]
        public void FirstDivergenceUsesStructuredMpStatusRemovalCoverageAndControlRatherThanDescriptions()
        {
            // Produce every source record through the real engine, then change one
            // recorded outcome field to exercise this adapter's comparison contract.
            var meditation = new CombatSession(Read<CombatScenario>("round4_mage_library.json")).Step();
            var changed = CombatSession.CopyStep(meditation);
            changed.effects[0].mpAfter++;
            changed.effects[0].mpDelta++;
            Assert.That(Difference(meditation, changed), Is.Not.Null, "Target MP must matter even when actor headers are unchanged.");
            var prose = CombatSession.CopyStep(meditation);
            prose.message = "different presentation";
            prose.actorName = "another display name";
            prose.effects[0].description = "another explanation";
            prose.attempts[0].reason = "another attempt explanation";
            Assert.That(Difference(meditation, prose), Is.Null);
            var anchorOnly = CombatSession.CopyStep(meditation);
            anchorOnly.targetId = "another-anchor-with-identical-actual-coverage";
            Assert.That(Difference(meditation, anchorOnly), Is.Null, "A coverage anchor is not an extra affected target.");

            var controlCore = WithFirstThreshold("round4_control.json", 1);
            var applied = controlCore.Step();
            Assert.That(applied.effects[0].statusId, Is.Not.Empty);
            changed = CombatSession.CopyStep(applied);
            changed.effects[0].statusExpiresAt++;
            Assert.That(Difference(applied, changed), Is.Not.Null);
            var skipped = Until(controlCore, step => step.status == "Skipped" && step.controlReason == "Stunned");
            changed = CombatSession.CopyStep(skipped);
            changed.controlReason = "";
            Assert.That(Difference(skipped, changed), Is.Not.Null, "A real control skip must not disappear from comparison.");

            var cleanseScenario = Read<CombatScenario>("round4_cleanse.json");
            cleanseScenario.units.Single(unit => unit.id == "ally").vulnerable = true;
            var cleanseCore = new CombatSession(cleanseScenario);
            var cleanseRule = cleanseCore.GetScenario().units.Single(unit => unit.id == "mage").instructions[0];
            cleanseRule.condition.terms[0].threshold = 1;
            Assert.That(cleanseCore.ReplaceInstruction("mage", 0, cleanseRule).success, Is.True);
            var removed = cleanseCore.Step();
            Assert.That(removed.effects[0].removedStatusIds, Is.Not.Empty);
            changed = CombatSession.CopyStep(removed);
            changed.effects[0].removedStatusIds = new[] { "different-removed-status" };
            Assert.That(Difference(removed, changed), Is.Not.Null);
            changed = CombatSession.CopyStep(removed);
            changed.effects[0].baseVulnerableAfter = !changed.effects[0].baseVulnerableAfter;
            Assert.That(Difference(removed, changed), Is.Not.Null);

            var areaCore = new CombatSession(Read<CombatScenario>("round4_area.json"));
            var areaRule = areaCore.GetScenario().units.Single(unit => unit.id == "mage").instructions[0];
            areaRule.actionId = "mage_fire_rain";
            Assert.That(areaCore.ReplaceInstruction("mage", 0, areaRule).success, Is.True);
            var area = areaCore.Step();
            Assert.That(area.targetIds.Length, Is.GreaterThan(1));
            changed = CombatSession.CopyStep(area);
            changed.targetIds = area.targetIds.Take(1).ToArray();
            Assert.That(Difference(area, changed), Is.Not.Null);
        }

        [Test]
        public void SameOpeningComparisonRetainsRealRestoredMpInBothReturnedAndStoredRecords()
        {
            var workbench = new TacticsWorkbenchSession(Read<CombatScenario>("round4_meditation.json"));
            var rule = workbench.GetDraftScenario().units.Single(unit => unit.id == "mage").instructions[0];
            rule.condition.terms[0].threshold = 0.35;
            Assert.That(workbench.ReplaceInstruction("mage", 0, rule).success, Is.True);
            var result = workbench.CompareWithLoaded();
            Assert.That(result.success, Is.True, result.message);
            Assert.That(result.comparison.firstDivergence, Is.Not.Null);
            var restore = result.comparison.variant.steps.First(step => step.actionId == "mage_meditate");
            Assert.That(restore.effects.Single().mpAfter, Is.EqualTo(16));
            Assert.That(restore.effects.Single().mpDelta, Is.EqualTo(12));
            Assert.That(restore.targetIds, Is.EqualTo(new[] { "mage" }));
            var expected = new CombatSession(workbench.GetDraftScenario()).RunToCompletion();
            Assert.That(Signature(result.comparison.variant), Is.EqualTo(Signature(expected)));
            restore.effects[0].mpDelta = 999;
            Assert.That(workbench.GetAcceptance().lastComparison.comparison.variant.steps.First(step => step.actionId == "mage_meditate").effects[0].mpDelta, Is.EqualTo(12));
        }

        [Test]
        public void ExpandedDefinitionsImportWithAllMetadataAndConflictingCoverageNeverResetsCampMoney()
        {
            var source = new CombatSession(Read<CombatScenario>("round4_mage_library.json"));
            var expedition = Camp();
            Assert.That(expedition.PurchaseAuxiliary("round4-before-import", 2).success, Is.True);
            var money = Signature(expedition.GetSnapshot().preparation);
            var applied = expedition.ApplyTactics(source.GetScenario());
            Assert.That(applied.success, Is.True, applied.reason);
            Assert.That(Signature(expedition.GetSnapshot().preparation), Is.EqualTo(money));
            foreach (var encounter in expedition.GetCombatConfiguration().encounters)
            {
                foreach (var member in source.GetScenario().units.Where(unit => unit.team == "friendly"))
                {
                    var receiver = encounter.scenario.units.Single(unit => unit.id == member.id);
                    Assert.That(receiver.availableActionIdsExplicit, Is.True);
                    Assert.That(receiver.availableActionIds, Is.EqualTo(source.GetAvailableActions(member.id).Select(action => action.id)));
                    foreach (var definition in source.GetAvailableActions(member.id))
                        Assert.That(Signature(encounter.scenario.actions.Single(action => action.id == definition.id)), Is.EqualTo(Signature(definition)));
                }
            }
            var before = Signature(expedition.GetCombatConfiguration()) + Signature(expedition.GetSnapshot().preparation);
            var conflicting = source.GetScenario();
            conflicting.actions.Single(action => action.id == "mage_fire_rain").targetMode = "row";
            Assert.That(expedition.ApplyTactics(conflicting).code, Is.EqualTo("ActionDefinitionConflict"));
            Assert.That(Signature(expedition.GetCombatConfiguration()) + Signature(expedition.GetSnapshot().preparation), Is.EqualTo(before));
        }

        [Test]
        public void EquivalentLegacyDefaultsSurviveJsonRoundTripAndRepeatCampImport()
        {
            var original = Read<CombatScenario>("round3_tactics.json");
            var expedition = Camp();
            Assert.That(expedition.ApplyTactics(original).success, Is.True);
            var repeated = JsonUtility.FromJson<CombatScenario>(JsonUtility.ToJson(original));
            foreach (var action in repeated.actions)
            {
                if (string.IsNullOrEmpty(action.targetMode)) action.targetMode = "single";
                if (string.IsNullOrEmpty(action.damageType)) action.damageType = "legacy";
                if (action.statusId == null) action.statusId = "";
                if (action.statusKind == null) action.statusKind = "";
            }
            var money = Signature(expedition.GetSnapshot().preparation);
            var result = expedition.ApplyTactics(repeated);
            Assert.That(result.success, Is.True, result.reason);
            Assert.That(Signature(expedition.GetSnapshot().preparation), Is.EqualTo(money));
        }

        private static CombatSession WithFirstThreshold(string file, double threshold)
        {
            var core = new CombatSession(Read<CombatScenario>(file));
            var rule = core.GetScenario().units.Single(unit => unit.id == "mage").instructions[0];
            rule.condition.terms[0].threshold = threshold;
            Assert.That(core.ReplaceInstruction("mage", 0, rule).success, Is.True);
            return core;
        }

        private static CombatStepResult Until(CombatSession core, Func<CombatStepResult, bool> predicate)
        {
            for (int index = 0; index < 20 && !core.GetSnapshot().finished; index++)
            {
                var step = core.Step();
                if (predicate(step)) return step;
            }
            Assert.Fail("The actual scenario did not produce the required recorded event.");
            return null;
        }

        private static CombatFirstDivergence Difference(CombatStepResult left, CombatStepResult right)
        {
            var type = typeof(TacticsWorkbenchSession).Assembly.GetType("Prototype.Tactics.TacticsComparison", true);
            var method = type.GetMethod("FirstActualDivergence", BindingFlags.Static | BindingFlags.NonPublic);
            return (CombatFirstDivergence)method.Invoke(null, new object[] { new[] { left }, new[] { right } });
        }

        private static T Read<T>(string file)
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", file)));
        }

        private static ExpeditionSliceSession Camp()
        {
            var root = Path.Combine(Application.streamingAssetsPath, "Prototype");
            return new ExpeditionSliceSession(JsonUtility.FromJson<ExpeditionSliceConfig>(File.ReadAllText(Path.Combine(root, "Expedition", "default.json"))),
                Read<CombatExpeditionConfig>("round2_expedition.json"),
                JsonUtility.FromJson<PreparationScenario>(File.ReadAllText(Path.Combine(root, "BaseEconomy", "preparation.json"))));
        }

        private static string Signature(object value)
        {
            if (value == null) return "null";
            if (value is string) return "string:" + ((string)value).Length + ":" + value;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal) return type.Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
            var array = value as Array;
            if (array != null) return "[" + string.Join("|", array.Cast<object>().Select(Signature)) + "]";
            return type.FullName + "{" + string.Join("|", type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => field.Name + "=" + Signature(field.GetValue(value)))) + "}";
        }
    }
}
