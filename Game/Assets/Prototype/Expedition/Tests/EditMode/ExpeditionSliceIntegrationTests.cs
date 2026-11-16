using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using Prototype.BaseEconomy;
using Prototype.Combat;
using UnityEngine;

namespace Prototype.Expedition.Tests
{
    public sealed class ExpeditionSliceIntegrationTests
    {
        private ExpeditionSliceConfig configuration;
        private CombatExpeditionConfig combat;
        private PreparationScenario preparation;

        [SetUp]
        public void LoadTheActualThreeStreamingAssetsConfigurations()
        {
            configuration = Read<ExpeditionSliceConfig>("Prototype/Expedition/default.json");
            combat = Read<CombatExpeditionConfig>(configuration.combatConfigPath);
            preparation = Read<PreparationScenario>(configuration.preparationConfigPath);
        }

        [Test]
        public void WholeSlicePostsActualBattleRewardsPurchasesAndConsumesPublicMaterials()
        {
            var session = Create();
            AssertPreparation(session.GetSnapshot().preparation, 100, 20, 0, 0, 2, 0, 0, 120);
            Assert.That(session.StartExpedition().success, Is.True);
            var first = session.RunNextEncounter();
            Assert.That(first.success, Is.True, first.reason);
            Assert.That(first.encounters.Single().battleEnd.time, Is.EqualTo(190));
            AssertPreparation(session.GetSnapshot().preparation, 110, 20, 2, 0, 2, 0, 0, 130);
            AssertExperience(session.GetSnapshot().experience, 11, 10);

            var second = session.RunNextEncounter();
            Assert.That(second.success, Is.True, second.reason);
            Assert.That(second.encounters.Single().battleStart.time, Is.EqualTo(205));
            Assert.That(second.encounters.Single().battleEnd.time, Is.EqualTo(310));
            AssertPreparation(session.GetSnapshot().preparation, 125, 20, 5, 0, 2, 0, 0, 145);
            AssertExperience(session.GetSnapshot().experience, 27, 25);
            Assert.That(session.GetSnapshot().experience.Sum(value => value.amount), Is.EqualTo(52));

            Assert.That(session.PurchaseAuxiliary("integration-purchase", preparation.purchaseQuantity).success, Is.True);
            AssertPreparation(session.GetSnapshot().preparation, 113, 32, 5, 2, 0, 0, 0, 145);
            Assert.That(session.EnhanceEquippedWeapon("integration-enhance").success, Is.True);
            AssertPreparation(session.GetSnapshot().preparation, 113, 32, 2, 0, 0, 1, 4, 145);
            var accepted = session.GetAcceptance();
            Assert.That(accepted.snapshot.preparation.oreConsumed, Is.EqualTo(3));
            Assert.That(accepted.snapshot.preparation.auxiliaryConsumed, Is.EqualTo(2));
            Assert.That(accepted.ledger.Length, Is.EqualTo(5));
            Assert.That(accepted.ledger.Select(value => value.sequence), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            var purchase = accepted.ledger.Single(value => value.eventKind == "AuxiliaryPurchased");
            Assert.That(purchase.payerId, Is.EqualTo(preparation.publicOwnerId));
            Assert.That(purchase.payeeId, Is.EqualTo(preparation.residentId));
            Assert.That(purchase.amount, Is.EqualTo(12));
            Assert.That(purchase.itemChanges.Single().fromOwnerId, Is.EqualTo(preparation.residentId));
            Assert.That(purchase.itemChanges.Single().toOwnerId, Is.EqualTo(preparation.publicOwnerId));
            var enhancement = accepted.ledger.Single(value => value.eventKind == "WeaponEnhanced");
            Assert.That(enhancement.amount, Is.Zero);
            Assert.That(enhancement.itemChanges.Count(value => value.isConsumption), Is.EqualTo(2));
            Assert.That(enhancement.equipmentChange.equippedById, Is.EqualTo("guard-linen"));
            Assert.That(enhancement.equipmentChange.attackBonusBefore, Is.Zero);
            Assert.That(enhancement.equipmentChange.attackBonusAfter, Is.EqualTo(4));
        }

        [Test]
        public void PerEncounterAndBatchHaveIdenticalCompleteStatesEventsExperienceAndLedger()
        {
            var watched = Create();
            var batched = Create();
            Assert.That(watched.StartExpedition().success, Is.True);
            Assert.That(batched.StartExpedition().success, Is.True);
            watched.RunNextEncounter();
            watched.RunNextEncounter();
            batched.RunToEnd();
            var left = watched.GetSnapshot();
            var right = batched.GetSnapshot();
            Assert.That(left.currentExpeditionId, Is.Not.EqualTo(right.currentExpeditionId));
            Assert.That(CanonicalReport(left.report), Is.EqualTo(CanonicalReport(right.report)),
                "Compare every event, carry snapshot and reward field; normalize only unique dispatch/result IDs.");
            Assert.That(JsonUtility.ToJson(left.partyState), Is.EqualTo(JsonUtility.ToJson(right.partyState)));
            Assert.That(JsonUtility.ToJson(left.preparation), Is.EqualTo(JsonUtility.ToJson(right.preparation)));
            AssertExperience(left.experience, 27, 25);
            AssertExperience(right.experience, 27, 25);
            Assert.That(CanonicalLedger(watched), Is.EqualTo(CanonicalLedger(batched)));
            Assert.That(watched.ExportLedgerCsv().Replace(left.currentExpeditionId, "<dispatch>"),
                Is.EqualTo(batched.ExportLedgerCsv().Replace(right.currentExpeditionId, "<dispatch>")));
        }

        [Test]
        public void EveryRealDispatchHasUniqueIdentityAndAnExistingDispatchCannotRefillTheParty()
        {
            var first = Create();
            var second = Create();
            first.StartExpedition();
            second.StartExpedition();
            var firstId = first.GetSnapshot().currentExpeditionId;
            var secondId = second.GetSnapshot().currentExpeditionId;
            Assert.That(firstId, Is.Not.Empty);
            Assert.That(firstId, Is.Not.EqualTo(secondId));
            first.RunToEnd();
            var settled = StateFingerprint(first, true);
            var rejected = first.StartExpedition();
            Assert.That(rejected.success, Is.False);
            Assert.That(rejected.code, Is.EqualTo("ExistingExpedition"));
            Assert.That(StateFingerprint(first, true), Is.EqualTo(settled));
            first.Reset();
            AssertPreparation(first.GetSnapshot().preparation, 100, 20, 0, 0, 2, 0, 0, 120);
            Assert.That(first.GetSnapshot().experience, Is.Empty);
            Assert.That(first.GetSnapshot().settlements, Is.Empty);
            Assert.That(first.StartExpedition().success, Is.True);
            var resetId = first.GetSnapshot().currentExpeditionId;
            Assert.That(resetId, Is.Not.EqualTo(firstId));
            Assert.That(resetId, Is.Not.EqualTo(secondId));
        }

        [Test]
        public void EachResultIdSettlesExactlyOnceAndReportReadsOrRetriesCannotRewardAgain()
        {
            var session = Completed();
            var before = StateFingerprint(session, true);
            var report = session.GetSnapshot().report;
            Assert.That(report.encounters.Select(value => value.resultId).Distinct().Count(), Is.EqualTo(2));
            foreach (var encounter in report.encounters)
            {
                for (var retry = 0; retry < 3; retry++)
                {
                    var replay = session.RetrySettlement(encounter.resultId);
                    Assert.That(replay.success, Is.True, replay.reason);
                    Assert.That(replay.isReplay, Is.True);
                }
                Assert.That(session.GetAcceptance().ledger.Count(value => value.operationId == encounter.resultId), Is.EqualTo(1));
            }
            for (var read = 0; read < 3; read++)
            {
                session.GetSnapshot();
                session.GetAcceptance();
                session.ExportReportText();
                session.ExportLedgerText();
                session.ExportLedgerCsv();
                session.RunToEnd();
                session.RunNextEncounter();
            }
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
            Assert.That(session.GetSnapshot().settlements.Length, Is.EqualTo(2));
            AssertExperience(session.GetSnapshot().experience, 27, 25);
        }

        [Test]
        public void CombatOreIsMappedExplicitlyToTheBaseItemInsteadOfNormalizingSpelling()
        {
            Assert.That(configuration.itemMappings.Length, Is.EqualTo(1));
            Assert.That(configuration.itemMappings[0].combatItemId, Is.EqualTo("expedition_ore"));
            Assert.That(configuration.itemMappings[0].preparationItemId, Is.EqualTo("expedition-ore"));
            var session = Completed();
            Assert.That(session.GetSnapshot().report.publicDrops.Single().itemId, Is.EqualTo("expedition_ore"));
            foreach (var receipt in session.GetSnapshot().settlements)
            {
                Assert.That(receipt.success, Is.True, receipt.reason);
                Assert.That(receipt.rewardsEligible, Is.True);
                Assert.That(receipt.items.Single().itemId, Is.EqualTo(preparation.oreItemId));
            }
            configuration.itemMappings = new ExpeditionItemMapping[0];
            Assert.Throws<ArgumentException>(() => Create(), "Missing explicit mapping must be rejected before battle or settlement.");
        }

        [Test]
        public void ReadableMainlineReportIncludesTheActualAttemptAndCandidateReasons()
        {
            var session = Completed();
            var attempt = session.GetSnapshot().report.encounters.SelectMany(value => value.steps)
                .SelectMany(value => value.attempts)
                .First(value => !string.IsNullOrWhiteSpace(value.reason) && value.candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate.reason)));
            var candidate = attempt.candidates.First(value => !string.IsNullOrWhiteSpace(value.reason));
            var reportText = session.ExportReportText();
            Assert.That(reportText, Does.Contain(attempt.reason), "The real attempt explanation must be in the human-readable report.");
            Assert.That(reportText, Does.Contain(candidate.reason), "The real per-candidate explanation cannot exist only in JSON.");
        }

        [Test]
        public void InsufficientPurchaseCashIsAtomicAndTheSameFailedIdCanSucceedAfterRealRewards()
        {
            preparation.initialTreasuryCash = 0;
            var session = Create();
            var opening = StateFingerprint(session, false);
            var refused = session.PurchaseAuxiliary("recover-purchase", 2);
            Assert.That(refused.success, Is.False);
            Assert.That(refused.code, Is.EqualTo("InsufficientCash"));
            Assert.That(StateFingerprint(session, false), Is.EqualTo(opening));
            session.StartExpedition();
            session.RunNextEncounter();
            var firstRewards = StateFingerprint(session, true);
            Assert.That(session.PurchaseAuxiliary("recover-purchase", 2).success, Is.False);
            Assert.That(StateFingerprint(session, true), Is.EqualTo(firstRewards));
            session.RunNextEncounter();
            var recovered = session.PurchaseAuxiliary("recover-purchase", 2);
            Assert.That(recovered.success, Is.True, recovered.reason);
            Assert.That(recovered.isReplay, Is.False, "A failed command did not reserve its ID.");
            AssertPreparation(session.GetSnapshot().preparation, 13, 32, 5, 2, 0, 0, 0, 45);
            Assert.That(session.GetAcceptance().ledger.Count(value => value.operationId == "recover-purchase"), Is.EqualTo(1));
        }

        [Test]
        public void MissingOreDoesNotConsumePurchasedAuxiliaryAndCanRecoverOnTheNextEncounter()
        {
            var session = Create();
            session.StartExpedition();
            session.RunNextEncounter();
            Assert.That(session.PurchaseAuxiliary("ore-shortage-purchase", 2).success, Is.True);
            var before = StateFingerprint(session, true);
            var refused = session.EnhanceEquippedWeapon("recover-enhancement");
            Assert.That(refused.success, Is.False);
            Assert.That(refused.code, Is.EqualTo("InsufficientPublicOre"));
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
            AssertPreparation(session.GetSnapshot().preparation, 98, 32, 2, 2, 0, 0, 0, 130);
            session.RunNextEncounter();
            var recovered = session.EnhanceEquippedWeapon("recover-enhancement");
            Assert.That(recovered.success, Is.True, recovered.reason);
            Assert.That(recovered.isReplay, Is.False);
            AssertPreparation(session.GetSnapshot().preparation, 113, 32, 2, 0, 0, 1, 4, 145);
            Assert.That(session.GetAcceptance().ledger.Count(value => value.operationId == "recover-enhancement"), Is.EqualTo(1));
        }

        [Test]
        public void MissingPublicAuxiliaryLeavesOreUntouchedThenPurchaseAllowsTheSameEnhancement()
        {
            var session = Completed();
            var before = StateFingerprint(session, true);
            var refused = session.EnhanceEquippedWeapon("auxiliary-recovery");
            Assert.That(refused.success, Is.False);
            Assert.That(refused.code, Is.EqualTo("InsufficientPublicAuxiliary"));
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
            Assert.That(session.GetSnapshot().preparation.publicOre, Is.EqualTo(5));
            session.PurchaseAuxiliary("auxiliary-recovery-purchase", 2);
            Assert.That(session.EnhanceEquippedWeapon("auxiliary-recovery").success, Is.True);
            AssertPreparation(session.GetSnapshot().preparation, 113, 32, 2, 0, 0, 1, 4, 145);
        }

        [Test]
        public void PrivateStockFailureDoesNotPayTheResidentAndAValidSmallerPurchaseStillWorks()
        {
            preparation.initialResidentAuxiliary = 1;
            var session = Completed();
            var before = StateFingerprint(session, true);
            var refused = session.PurchaseAuxiliary("private-stock-recovery", 2);
            Assert.That(refused.success, Is.False);
            Assert.That(refused.code, Is.EqualTo("InsufficientPrivateMaterial"));
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
            var legal = session.PurchaseAuxiliary("private-stock-recovery", 1);
            Assert.That(legal.success, Is.True, legal.reason);
            AssertPreparation(session.GetSnapshot().preparation, 119, 26, 5, 1, 0, 0, 0, 145);
        }

        [Test]
        public void SuccessfulPreparationRetriesAndConflictingIdsNeverRepeatPaymentsOrConsumption()
        {
            var session = Prepared();
            var before = StateFingerprint(session, true);
            Assert.That(session.PurchaseAuxiliary("purchase-main", 2).isReplay, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("enhance-main").isReplay, Is.True);
            Assert.That(session.PurchaseAuxiliary("purchase-main", 1).success, Is.False);
            Assert.That(session.EnhanceEquippedWeapon("another-enhancement").success, Is.False);
            Assert.That(session.RetrySettlement("unknown-result").success, Is.False);
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
        }

        [Test]
        public void CarryUsesActualResourcesAbsoluteStatusesReadyTimesMaximumsAndGridPositions()
        {
            var session = Completed();
            var first = session.GetSnapshot().report.encounters[0];
            var second = session.GetSnapshot().report.encounters[1];
            Assert.That(JsonUtility.ToJson(second.previousBattleEnd), Is.EqualTo(JsonUtility.ToJson(first.battleEnd)));
            Assert.That(second.betweenBattleWait, Is.EqualTo(15));
            Assert.That(second.afterIntermission.time, Is.EqualTo(205));
            foreach (var member in first.battleEnd.units.Where(value => value.team == combat.carryTeam))
            {
                var carried = Unit(second.battleStart, member.id);
                AssertResourcesAndGrid(member, carried);
                Assert.That(carried.nextActionTime, Is.EqualTo(Math.Max(205, member.nextActionTime)));
                var remaining = member.statuses.Where(value => value.expiresAt > 205).ToArray();
                Assert.That(StatusSignature(carried.statuses), Is.EqualTo(StatusSignature(remaining)));
            }
            Assert.That(Unit(second.battleStart, "ally").statuses.Any(value => value.id == "short_vulnerability"), Is.False);
            Assert.That(Unit(second.battleStart, "ally").statuses.Single(value => value.id == "camp_guard").expiresAt, Is.EqualTo(260));
            Assert.That(Unit(second.battleEnd, "ally").statuses.Any(value => value.id == "camp_guard"), Is.False);
            Assert.That(second.steps.Any(value => value.time == 260 && value.status == "StateExpired" &&
                value.effects.Any(effect => effect.kind == "status_expired" && effect.targetId == "ally" && effect.description.Contains("camp_guard"))), Is.True);
            Assert.That(Unit(second.battleEnd, "mage").mp, Is.EqualTo(18));
            Assert.That(Unit(second.battleEnd, "ally").hp, Is.EqualTo(42));
            Assert.That(Unit(second.battleEnd, "mage").nextActionTime, Is.EqualTo(325));
            Assert.That(Unit(second.battleEnd, "ally").nextActionTime, Is.EqualTo(370));
        }

        [Test]
        public void ComparisonMapsTheRealPlusFourWeaponAndStartsBothBranchesFromWait310()
        {
            var session = Prepared();
            var before = StateFingerprint(session, true);
            var comparison = session.CompareEquipmentFromCurrentState();
            Assert.That(comparison.success, Is.True, comparison.reason);
            Assert.That(comparison.onlyEquipmentChanged, Is.True);
            Assert.That(comparison.combatUnitId, Is.EqualTo("ally"));
            Assert.That(comparison.equipmentSourceId, Is.EqualTo("guard-linen-sword-001"));
            Assert.That(comparison.baselineEquipmentBonus, Is.Zero);
            Assert.That(comparison.enhancedEquipmentBonus, Is.EqualTo(4));
            Assert.That(comparison.sourceSnapshot.time, Is.EqualTo(310));
            Assert.That(comparison.baselineStart.time, Is.EqualTo(310));
            Assert.That(comparison.enhancedStart.time, Is.EqualTo(310));
            foreach (var source in comparison.sourceSnapshot.units.Where(value => value.team == combat.carryTeam))
            {
                foreach (var opening in new[] { comparison.baselineStart, comparison.enhancedStart })
                {
                    var member = Unit(opening, source.id);
                    AssertResourcesAndGrid(source, member);
                    Assert.That(member.nextActionTime, Is.EqualTo(source.nextActionTime));
                    Assert.That(StatusSignature(member.statuses), Is.EqualTo(StatusSignature(source.statuses)));
                    Assert.That(string.Join(";", member.instructions.Select(value => JsonUtility.ToJson(value))),
                        Is.EqualTo(string.Join(";", source.instructions.Select(value => JsonUtility.ToJson(value)))));
                }
            }
            Assert.That(Unit(comparison.baselineStart, "ally").equipmentDamageBonus, Is.Zero);
            Assert.That(Unit(comparison.enhancedStart, "ally").equipmentDamageBonus, Is.EqualTo(4));
            Assert.That(Unit(comparison.enhancedStart, "ally").equipmentSourceId, Is.EqualTo(preparation.weaponInstanceId));
            var normalized = CombatSession.CopyScenario(comparison.enhancedConfiguration);
            normalized.units.Single(value => value.id == "ally").equipmentDamageBonus = 0;
            Assert.That(ConfigurationSignature(normalized), Is.EqualTo(ConfigurationSignature(comparison.baselineConfiguration)),
                "Every input except the mapped weapon bonus is identical.");
            Assert.That(comparison.baselineConfiguration.seed, Is.EqualTo(20261114));
            Assert.That(comparison.enhancedConfiguration.seed, Is.EqualTo(20261114));
            Assert.That(comparison.baselineConfiguration.randomUsed, Is.False);
            Assert.That(comparison.enhancedConfiguration.randomUsed, Is.False);
            var baselineHit = comparison.baseline.steps.Single(value => value.time == 370 && value.actorId == "ally" && value.actionId == "basic_attack");
            var enhancedHit = comparison.enhanced.steps.Single(value => value.time == 370 && value.actorId == "ally" && value.actionId == "basic_attack");
            Assert.That(baselineHit.targetId, Is.EqualTo(enhancedHit.targetId));
            Assert.That(baselineHit.effects.Single(value => value.kind == "damage").hpBefore, Is.EqualTo(20));
            Assert.That(baselineHit.effects.Single(value => value.kind == "damage").hpAfter, Is.EqualTo(2));
            Assert.That(enhancedHit.effects.Single(value => value.kind == "damage").hpBefore, Is.EqualTo(20));
            Assert.That(enhancedHit.effects.Single(value => value.kind == "damage").hpAfter, Is.Zero);
            Assert.That(comparison.firstDivergence.baseline.time, Is.EqualTo(370));
            Assert.That(comparison.firstDivergence.variant.time, Is.EqualTo(370));
            Assert.That(comparison.baseline.time, Is.EqualTo(490));
            Assert.That(comparison.enhanced.time, Is.EqualTo(430));
            Assert.That(comparison.baseline.outcome, Is.EqualTo("Victory"));
            Assert.That(comparison.enhanced.outcome, Is.EqualTo("Victory"));
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before), "A comparison is not a real dispatch or reward source.");
        }

        [Test]
        public void VerificationBattleCarriesTheRealPartyButAwardsNothingAndComparisonKeepsItsCapturedOrigin()
        {
            var session = Prepared();
            var origin = session.GetSnapshot().partyState;
            var beforeRewards = StateFingerprint(session, false);
            var comparison = session.CompareEquipmentFromCurrentState();
            var verification = session.RunVerificationBattle();
            Assert.That(verification.success, Is.True, verification.reason);
            Assert.That(verification.rewardsEligible, Is.False);
            Assert.That(JsonUtility.ToJson(verification.previousSnapshot), Is.EqualTo(JsonUtility.ToJson(origin)));
            Assert.That(verification.battleStart.time, Is.EqualTo(310));
            foreach (var member in origin.units.Where(value => value.team == combat.carryTeam))
            {
                var carried = Unit(verification.battleStart, member.id);
                AssertResourcesAndGrid(member, carried);
                Assert.That(carried.nextActionTime, Is.EqualTo(member.nextActionTime));
                Assert.That(StatusSignature(carried.statuses), Is.EqualTo(StatusSignature(member.statuses)));
            }
            Assert.That(verification.result.time, Is.EqualTo(430));
            Assert.That(Unit(verification.battleStart, "ally").equipmentDamageBonus, Is.EqualTo(4));
            Assert.That(session.GetSnapshot().partyState.time, Is.EqualTo(430));
            Assert.That(StateFingerprint(session, false), Is.EqualTo(beforeRewards));
            var afterRealBattle = JsonUtility.ToJson(session.GetSnapshot().partyState);
            Assert.That(JsonUtility.ToJson(session.CompareEquipmentFromCurrentState()), Is.EqualTo(JsonUtility.ToJson(comparison)));
            Assert.That(JsonUtility.ToJson(session.GetSnapshot().partyState), Is.EqualTo(afterRealBattle));
            var another = session.RunVerificationBattle();
            Assert.That(another.success, Is.True, another.reason);
            Assert.That(another.battleId, Is.Not.EqualTo(verification.battleId));
            Assert.That(StateFingerprint(session, false), Is.EqualTo(beforeRewards));
        }

        [Test]
        public void SessionCopiesItsInputsAndReturnedReportsCannotChangeSubsequentResults()
        {
            var session = Create();
            preparation.initialTreasuryCash = 0;
            combat.encounters[0].scenario.units.Single(value => value.id == "mage").hp = 1;
            configuration.itemMappings[0].preparationItemId = "caller-mutated";
            session.GetConfiguration().itemMappings[0].preparationItemId = "returned-copy-mutated";
            session.StartExpedition();
            session.RunToEnd();
            Assert.That(session.GetSnapshot().report.finalSnapshot.time, Is.EqualTo(310));
            AssertPreparation(session.GetSnapshot().preparation, 125, 20, 5, 0, 2, 0, 0, 145);
            var before = StateFingerprint(session, true);
            var external = session.GetAcceptance();
            external.snapshot.preparation.treasuryCash = 9999;
            external.experience[0].amount = 9999;
            external.expeditions[0].encounters[0].battleEnd.units[0].hp = 1;
            external.settlements[0].items[0].quantity = 9999;
            external.ledger[0].after.treasuryCash = 9999;
            Assert.That(StateFingerprint(session, true), Is.EqualTo(before));
        }

        [TestCase("experience")]
        [TestCase("cash")]
        [TestCase("ore")]
        public void AggregateReportOverflowIsRejectedBeforeTheCampCanStart(string amount)
        {
            if (amount == "experience") combat.encounters[0].experiencePool = int.MaxValue;
            if (amount == "cash") combat.encounters[0].publicCash = int.MaxValue;
            if (amount == "ore") combat.encounters[0].publicDrops[0].quantity = int.MaxValue;
            Assert.Throws<ArgumentException>(() => Create(), "Reports use int totals; invalid aggregate configurations must fail at construction.");
        }

        private ExpeditionSliceSession Create()
        {
            return new ExpeditionSliceSession(configuration, combat, preparation);
        }

        private ExpeditionSliceSession Completed()
        {
            var session = Create();
            Assert.That(session.StartExpedition().success, Is.True);
            Assert.That(session.RunToEnd().success, Is.True);
            return session;
        }

        private ExpeditionSliceSession Prepared()
        {
            var session = Completed();
            Assert.That(session.PurchaseAuxiliary("purchase-main", 2).success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("enhance-main").success, Is.True);
            return session;
        }

        private static T Read<T>(string relativePath)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, path);
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }

        private static string CanonicalReport(CombatExpeditionReport source)
        {
            if (source == null) return "<none>";
            return JsonUtility.ToJson(source).Replace(source.expeditionId, "<dispatch>");
        }

        private static string CanonicalLedger(ExpeditionSliceSession session)
        {
            var accepted = session.GetAcceptance();
            return string.Join("\n", accepted.ledger.Select(value => JsonUtility.ToJson(value)))
                .Replace(accepted.snapshot.currentExpeditionId, "<dispatch>");
        }

        private static string StateFingerprint(ExpeditionSliceSession session, bool includeParty)
        {
            var state = session.GetSnapshot();
            return JsonUtility.ToJson(state.preparation) + "|" +
                string.Join(";", state.experience.OrderBy(value => value.unitId).Select(value => value.unitId + ":" + value.amount)) + "|" +
                session.ExportLedgerCsv() + "|" + (includeParty ? JsonUtility.ToJson(state.partyState) + "|" + CanonicalReport(state.report) : "");
        }

        private static CombatUnitSnapshot Unit(CombatSnapshot state, string id)
        {
            return state.units.Single(value => value.id == id);
        }

        private static string StatusSignature(CombatStatusConfig[] statuses)
        {
            return string.Join(";", statuses.Select(value => JsonUtility.ToJson(value)));
        }

        private static string ConfigurationSignature(object value)
        {
            if (value == null) return "null";
            var text = value as string;
            if (text != null) return "string:" + text.Length + ":" + text;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal)
                return type.Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
            var array = value as Array;
            if (array != null) return "[" + string.Join("|", array.Cast<object>().Select(ConfigurationSignature)) + "]";
            return type.FullName + "{" + string.Join("|", type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => field.Name + "=" + ConfigurationSignature(field.GetValue(value)))) + "}";
        }

        private static void AssertResourcesAndGrid(CombatUnitSnapshot expected, CombatUnitSnapshot actual)
        {
            Assert.That(actual.id, Is.EqualTo(expected.id));
            Assert.That(actual.team, Is.EqualTo(expected.team));
            Assert.That(actual.hp, Is.EqualTo(expected.hp), expected.id + " HP must be carried, not restored.");
            Assert.That(actual.mp, Is.EqualTo(expected.mp), expected.id + " MP must be carried, not restored.");
            Assert.That(actual.maxHp, Is.EqualTo(expected.maxHp));
            Assert.That(actual.maxMp, Is.EqualTo(expected.maxMp));
            Assert.That(actual.row, Is.EqualTo(expected.row));
            Assert.That(actual.column, Is.EqualTo(expected.column));
        }

        private static void AssertExperience(CombatExperienceGrant[] values, int mage, int ally)
        {
            Assert.That(values.Length, Is.EqualTo(2));
            Assert.That(values.Single(value => value.unitId == "mage").amount, Is.EqualTo(mage));
            Assert.That(values.Single(value => value.unitId == "ally").amount, Is.EqualTo(ally));
        }

        private static void AssertPreparation(PreparationStateDto value, int treasury, int resident, int ore,
            int auxiliary, int privateAuxiliary, int level, int bonus, long total)
        {
            Assert.That(value.treasuryCash, Is.EqualTo(treasury));
            Assert.That(value.residentCash, Is.EqualTo(resident));
            Assert.That(value.totalCash, Is.EqualTo(total));
            Assert.That(value.publicOre, Is.EqualTo(ore));
            Assert.That(value.publicAuxiliary, Is.EqualTo(auxiliary));
            Assert.That(value.residentAuxiliary, Is.EqualTo(privateAuxiliary));
            Assert.That(value.weaponEnhancementLevel, Is.EqualTo(level));
            Assert.That(value.weaponAttackBonus, Is.EqualTo(bonus));
        }
    }
}
