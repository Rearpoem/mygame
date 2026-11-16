using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.BaseEconomy.Tests
{
    public sealed class PreparationSessionTests
    {
        private PreparationScenario _scenario;

        [SetUp]
        public void ReadReplaceablePreparationConfiguration()
        {
            _scenario = PreparationEvidenceExporter.LoadScenario();
        }

        [Test]
        public void DefaultOpeningAndEachTransactionMatchHandCalculation()
        {
            var session = new PreparationSession(_scenario);
            AssertState(session.GetSnapshot(), 100, 20, 0, 0, 2, 0, 0);
            Assert.That(session.ApplyExpeditionDrop(CloneDrop(_scenario.presetExpeditionDrop)).Success, Is.True);
            AssertState(session.GetSnapshot(), 100, 20, 3, 0, 2, 0, 0);
            Assert.That(session.PurchaseAuxiliary("purchase-hand", 2).Success, Is.True);
            AssertState(session.GetSnapshot(), 88, 32, 3, 2, 0, 0, 0);
            Assert.That(session.EnhanceEquippedWeapon("enhance-hand").Success, Is.True);
            AssertState(session.GetSnapshot(), 88, 32, 0, 0, 0, 1, 4);
            Assert.That(session.GetSnapshot().OreConsumed, Is.EqualTo(3));
            Assert.That(session.GetSnapshot().AuxiliaryConsumed, Is.EqualTo(2));
            Assert.That(session.GetSnapshot().HasEnhanced, Is.True);
            Assert.That(session.GetSnapshot().CombatantId, Is.EqualTo(_scenario.combatantId));
            Assert.That(session.GetSnapshot().WeaponInstanceId, Is.EqualTo(_scenario.weaponInstanceId));
        }

        [Test]
        public void LedgerShowsCounterpartiesOwnershipConsumptionAndEquipmentDelta()
        {
            var session = Complete();
            Assert.That(session.Ledger.Count, Is.EqualTo(4));
            for (int i = 0; i < session.Ledger.Count; i++)
            {
                Assert.That(session.Ledger[i].Sequence, Is.EqualTo(i + 1));
                Assert.That(session.Ledger[i].After.TotalCash, Is.EqualTo(120));
            }
            var purchase = session.Ledger[2];
            Assert.That(purchase.PayerId, Is.EqualTo(_scenario.publicOwnerId));
            Assert.That(purchase.PayeeId, Is.EqualTo(_scenario.residentId));
            Assert.That(purchase.Amount, Is.EqualTo(12));
            Assert.That(purchase.ItemChanges.Count, Is.EqualTo(1));
            Assert.That(purchase.ItemChanges[0].FromOwnerId, Is.EqualTo(_scenario.residentId));
            Assert.That(purchase.ItemChanges[0].ToOwnerId, Is.EqualTo(_scenario.publicOwnerId));
            Assert.That(purchase.ItemChanges[0].ItemId, Is.EqualTo(_scenario.auxiliaryItemId));
            Assert.That(purchase.ItemChanges[0].Quantity, Is.EqualTo(2));
            Assert.That(purchase.ItemChanges[0].IsConsumption, Is.False);
            var enhanced = session.Ledger[3];
            Assert.That(enhanced.Amount, Is.Zero);
            Assert.That(enhanced.ItemChanges.Count, Is.EqualTo(2));
            Assert.That(enhanced.ItemChanges.All(item => item.IsConsumption), Is.True);
            Assert.That(enhanced.ItemChanges.Single(item => item.ItemId == _scenario.oreItemId).Quantity, Is.EqualTo(3));
            Assert.That(enhanced.ItemChanges.Single(item => item.ItemId == _scenario.auxiliaryItemId).Quantity, Is.EqualTo(2));
            Assert.That(enhanced.EquipmentChange, Is.Not.Null);
            Assert.That(enhanced.EquipmentChange.WeaponInstanceId, Is.EqualTo(_scenario.weaponInstanceId));
            Assert.That(enhanced.EquipmentChange.EquippedById, Is.EqualTo(_scenario.combatantId));
            Assert.That(enhanced.EquipmentChange.EnhancementLevelBefore, Is.Zero);
            Assert.That(enhanced.EquipmentChange.EnhancementLevelAfter, Is.EqualTo(1));
            Assert.That(enhanced.EquipmentChange.AttackBonusBefore, Is.Zero);
            Assert.That(enhanced.EquipmentChange.AttackBonusAfter, Is.EqualTo(4));
        }

        [Test]
        public void ReopeningAndReapplyingTheSameDropNeverGrantsAgain()
        {
            var session = Complete();
            string before = StateKey(session);
            string ledgerBefore = session.ExportLedgerCsv();
            var oldReceipt = session.GetSettlementReceipt(_scenario.presetExpeditionDrop.settlementId);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(session.GetSettlementReceipt(_scenario.presetExpeditionDrop.settlementId).Sequence, Is.EqualTo(oldReceipt.Sequence));
                var replay = session.ApplyExpeditionDrop(CloneDrop(_scenario.presetExpeditionDrop));
                Assert.That(replay.Success, Is.True);
                Assert.That(replay.IsReplay, Is.True);
                Assert.That(replay.LedgerSequence, Is.EqualTo(oldReceipt.Sequence));
            }
            Assert.That(StateKey(session), Is.EqualTo(before));
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(ledgerBefore));
            Assert.That(oldReceipt.After.PublicOre, Is.EqualTo(3));
            Assert.That(oldReceipt.After.WeaponEnhancementLevel, Is.Zero);
            Assert.That(session.GetSettlementReceipt("missing-receipt"), Is.Null);
        }

        [Test]
        public void IdenticalPurchaseAndEnhancementCommandsReplayWithoutAnotherDebit()
        {
            var session = Complete();
            string before = StateKey(session);
            string ledger = session.ExportLedgerCsv();
            var purchase = session.PurchaseAuxiliary("purchase-success", 2);
            var enhanced = session.EnhanceEquippedWeapon("enhance-success");
            Assert.That(purchase.Success && purchase.IsReplay, Is.True);
            Assert.That(enhanced.Success && enhanced.IsReplay, Is.True);
            Assert.That(StateKey(session), Is.EqualTo(before));
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(ledger));
            AssertAtomicFailure(session, () => session.EnhanceEquippedWeapon("enhance-another"), PreparationResultCode.AlreadyEnhanced);
        }

        [Test]
        public void SameIdWithDifferentDropOrCommandPayloadIsRejected()
        {
            var session = Complete();
            var conflicting = CloneDrop(_scenario.presetExpeditionDrop);
            conflicting.cashReward = 1;
            AssertAtomicFailure(session, () => session.ApplyExpeditionDrop(conflicting), PreparationResultCode.DuplicateIdConflict);
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("purchase-success", 1), PreparationResultCode.DuplicateIdConflict);
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary(_scenario.presetExpeditionDrop.settlementId, 1), PreparationResultCode.DuplicateIdConflict);
        }

        [Test]
        public void InsufficientPublicCashDoesNotMoveMoneyOrPrivateMaterial()
        {
            _scenario.initialTreasuryCash = 11;
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("purchase-poor", 2), PreparationResultCode.InsufficientCash);
            Assert.That(session.GetSnapshot().TreasuryCash, Is.EqualTo(11));
            Assert.That(session.GetSnapshot().ResidentAuxiliary, Is.EqualTo(2));
        }

        [Test]
        public void FailedCommandDoesNotReserveItsIdAndCanBeRetriedAfterNewIncome()
        {
            _scenario.initialTreasuryCash = 11;
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("retry-purchase", 2), PreparationResultCode.InsufficientCash);
            var topUp = NewDrop("top-up", 1, new ItemQuantity[0]);
            Assert.That(session.ApplyExpeditionDrop(topUp).Success, Is.True);
            Assert.That(session.PurchaseAuxiliary("retry-purchase", 2).Success, Is.True);
            Assert.That(session.GetSnapshot().TreasuryCash, Is.Zero);
            Assert.That(session.GetSnapshot().ResidentCash, Is.EqualTo(32));
            Assert.That(session.GetSnapshot().TotalCash, Is.EqualTo(32));
        }

        [Test]
        public void InsufficientPrivateStockDoesNotChargePublicCash()
        {
            _scenario.initialResidentAuxiliary = 1;
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("purchase-short-stock", 2), PreparationResultCode.InsufficientPrivateMaterial);
        }

        [Test]
        public void MissingPublicOreDoesNotConsumeAuxiliaryOrUndoEarlierPurchase()
        {
            var session = new PreparationSession(_scenario);
            var shortDrop = CloneDrop(_scenario.presetExpeditionDrop);
            shortDrop.items[0].quantity = 2;
            Assert.That(session.ApplyExpeditionDrop(shortDrop).Success, Is.True);
            Assert.That(session.PurchaseAuxiliary("purchase-before-ore-failure", 2).Success, Is.True);
            AssertAtomicFailure(session, () => session.EnhanceEquippedWeapon("enhance-no-ore"), PreparationResultCode.InsufficientPublicOre);
            AssertState(session.GetSnapshot(), 88, 32, 2, 2, 0, 0, 0);
        }

        [Test]
        public void MissingPublicAuxiliaryDoesNotConsumeOreOrUndoEarlierPurchase()
        {
            var session = AfterDrop();
            Assert.That(session.PurchaseAuxiliary("purchase-one", 1).Success, Is.True);
            AssertAtomicFailure(session, () => session.EnhanceEquippedWeapon("enhance-no-aux"), PreparationResultCode.InsufficientPublicAuxiliary);
            AssertState(session.GetSnapshot(), 94, 26, 3, 1, 1, 0, 0);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidPurchaseQuantityHasNoPartialEffect(int quantity)
        {
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("invalid-quantity", quantity), PreparationResultCode.InvalidQuantity);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void InvalidCommandIdHasNoEffect(string id)
        {
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary(id, 1), PreparationResultCode.InvalidCommandId);
            AssertAtomicFailure(session, () => session.EnhanceEquippedWeapon(id), PreparationResultCode.InvalidCommandId);
        }

        [Test]
        public void PurchasePriceMultiplicationOverflowCannotPartiallyCommit()
        {
            _scenario.initialTreasuryCash = int.MaxValue;
            _scenario.initialResidentAuxiliary = int.MaxValue;
            _scenario.auxiliaryUnitPrice = 2;
            var session = new PreparationSession(_scenario);
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("overflow-price", int.MaxValue), PreparationResultCode.NumericOverflow);
        }

        [Test]
        public void ResidentCashOverflowCannotDebitTreasuryOrDeliverStock()
        {
            _scenario.initialResidentCash = int.MaxValue;
            var session = AfterDrop();
            AssertAtomicFailure(session, () => session.PurchaseAuxiliary("overflow-recipient", 2), PreparationResultCode.NumericOverflow);
        }

        [Test]
        public void DropCashOverflowDoesNotGrantItsOtherwiseValidOre()
        {
            _scenario.initialTreasuryCash = int.MaxValue;
            var session = new PreparationSession(_scenario);
            var drop = CloneDrop(_scenario.presetExpeditionDrop);
            drop.cashReward = 1;
            AssertAtomicFailure(session, () => session.ApplyExpeditionDrop(drop), PreparationResultCode.NumericOverflow);
        }

        [Test]
        public void DropInventoryOverflowDoesNotGrantOtherValidItems()
        {
            _scenario.initialPublicOre = int.MaxValue;
            var session = new PreparationSession(_scenario);
            var drop = NewDrop("overflow-inventory", 0, new[]
            {
                new ItemQuantity { itemId = _scenario.auxiliaryItemId, quantity = 1 },
                new ItemQuantity { itemId = _scenario.oreItemId, quantity = 1 }
            });
            AssertAtomicFailure(session, () => session.ApplyExpeditionDrop(drop), PreparationResultCode.NumericOverflow);
        }

        [Test]
        public void InvalidLaterDropItemDoesNotGrantEarlierValidItemOrCash()
        {
            var session = new PreparationSession(_scenario);
            var drop = NewDrop("invalid-later-item", 5, new[]
            {
                new ItemQuantity { itemId = _scenario.oreItemId, quantity = 3 },
                new ItemQuantity { itemId = "unsupported-test-item", quantity = 1 }
            });
            AssertAtomicFailure(session, () => session.ApplyExpeditionDrop(drop), PreparationResultCode.InvalidDrop);
        }

        [TestCase("level")]
        [TestCase("bonus")]
        public void EquipmentOverflowDoesNotConsumeEitherPublicMaterial(string field)
        {
            if (field == "level") _scenario.initialWeaponEnhancementLevel = int.MaxValue;
            else _scenario.initialWeaponAttackBonus = int.MaxValue;
            var session = AfterDrop();
            Assert.That(session.PurchaseAuxiliary("overflow-equipment-purchase", 2).Success, Is.True);
            AssertAtomicFailure(session, () => session.EnhanceEquippedWeapon("overflow-equipment-enhance"), PreparationResultCode.NumericOverflow);
            Assert.That(session.GetSnapshot().PublicOre, Is.EqualTo(3));
            Assert.That(session.GetSnapshot().PublicAuxiliary, Is.EqualTo(2));
        }

        [Test]
        public void InputMutationAndLaterEventsCannotRewriteHistoricalReceipts()
        {
            var session = new PreparationSession(_scenario);
            var input = CloneDrop(_scenario.presetExpeditionDrop);
            Assert.That(session.ApplyExpeditionDrop(input).Success, Is.True);
            string id = input.settlementId;
            var receipt = session.GetSettlementReceipt(id);
            string oldSnapshot = PreparationEvidenceExporter.SnapshotJson(receipt.After);
            input.cashReward = 999;
            input.items[0].quantity = 999;
            input.items[0].itemId = "changed-outside";
            Assert.That(session.PurchaseAuxiliary("later-purchase", 2).Success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("later-enhance").Success, Is.True);
            Assert.That(PreparationEvidenceExporter.SnapshotJson(receipt.After), Is.EqualTo(oldSnapshot));
            Assert.That(receipt.ItemChanges[0].Quantity, Is.EqualTo(3));
            Assert.That(session.GetSettlementReceipt(id).After.PublicOre, Is.EqualTo(3));
            Assert.That(session.GetSnapshot().PublicOre, Is.Zero);
        }

        [Test]
        public void SnapshotsAndLedgerExposeNoPublicMutationRoute()
        {
            var session = Complete();
            Assert.That(typeof(PreparationSnapshot).GetProperties().All(property => property.GetSetMethod() == null), Is.True);
            Assert.That(typeof(PreparationLedgerEntry).GetProperties().All(property => property.GetSetMethod() == null), Is.True);
            var list = session.Ledger as IList<PreparationLedgerEntry>;
            if (list != null)
                Assert.Throws<NotSupportedException>(() => list.Clear());
            var changes = session.Ledger[2].ItemChanges as IList<PreparationItemChange>;
            if (changes != null)
                Assert.Throws<NotSupportedException>(() => changes.Clear());
            Assert.That(session.Ledger.Count, Is.EqualTo(4));
        }

        [Test]
        public void SessionAndResetKeepTheOriginalConfigurationCopy()
        {
            var drop = CloneDrop(_scenario.presetExpeditionDrop);
            var session = new PreparationSession(_scenario);
            _scenario.initialTreasuryCash = 0;
            _scenario.initialResidentAuxiliary = 0;
            _scenario.enhancementAttackBonus = 99;
            _scenario.presetExpeditionDrop.items[0].quantity = 999;
            Assert.That(session.ApplyExpeditionDrop(drop).Success, Is.True);
            Assert.That(session.PurchaseAuxiliary("copy-purchase", 2).Success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("copy-enhance").Success, Is.True);
            Assert.That(session.GetSnapshot().WeaponAttackBonus, Is.EqualTo(4));
            string first = session.ExportLedgerCsv();
            session.Reset();
            AssertState(session.GetSnapshot(), 100, 20, 0, 0, 2, 0, 0);
            Assert.That(session.GetSettlementReceipt(drop.settlementId), Is.Null);
            Assert.That(session.ApplyExpeditionDrop(CloneDrop(drop)).Success, Is.True);
            Assert.That(session.PurchaseAuxiliary("copy-purchase", 2).Success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("copy-enhance").Success, Is.True);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(first));
        }

        [Test]
        public void ReplacingPricesQuantitiesAndEffectChangesTheSameRuntimePath()
        {
            _scenario.auxiliaryUnitPrice = 4;
            _scenario.purchaseQuantity = 1;
            _scenario.oreRequired = 2;
            _scenario.auxiliaryRequired = 1;
            _scenario.enhancementAttackBonus = 7;
            _scenario.presetExpeditionDrop.items[0].quantity = 2;
            var session = AfterDrop();
            Assert.That(session.PurchaseAuxiliary("configured-purchase", _scenario.purchaseQuantity).Success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("configured-enhance").Success, Is.True);
            AssertState(session.GetSnapshot(), 96, 24, 0, 0, 1, 1, 7);
            Assert.That(session.GetSnapshot().OreConsumed, Is.EqualTo(2));
            Assert.That(session.GetSnapshot().AuxiliaryConsumed, Is.EqualTo(1));
        }

        [Test]
        public void PositiveExpeditionCashIsPublicAndIsNotAnInternalTransfer()
        {
            var session = new PreparationSession(_scenario);
            var drop = CloneDrop(_scenario.presetExpeditionDrop);
            drop.cashReward = 5;
            Assert.That(session.ApplyExpeditionDrop(drop).Success, Is.True);
            Assert.That(session.GetSnapshot().TreasuryCash, Is.EqualTo(105));
            Assert.That(session.GetSnapshot().ResidentCash, Is.EqualTo(20));
            Assert.That(session.GetSnapshot().TotalCash, Is.EqualTo(125));
            string before = StateKey(session);
            Assert.That(session.ApplyExpeditionDrop(CloneDrop(drop)).IsReplay, Is.True);
            Assert.That(StateKey(session), Is.EqualTo(before));
        }

        [Test]
        public void ExportedExamplesContainActualSnapshotsAndPassingFailureEvidence()
        {
            string output = Environment.GetEnvironmentVariable("BASE_ECONOMY_ROUND2_REPORT_PATH");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(Path.GetTempPath(), "BaseEconomyRound2", Guid.NewGuid().ToString("N"));
            PreparationEvidenceExporter.ExportAll(output);
            var io = JsonUtility.FromJson<PreparationInputOutputEvidence>(File.ReadAllText(Path.Combine(output, "input_output.json")));
            Assert.That(io.afterPurchase.treasuryCash, Is.EqualTo(88));
            Assert.That(io.afterPurchase.residentCash, Is.EqualTo(32));
            Assert.That(io.afterEnhancement.publicOre, Is.Zero);
            Assert.That(io.afterEnhancement.publicAuxiliary, Is.Zero);
            Assert.That(io.nextBattle.weaponAttackBonus, Is.EqualTo(4));
            Assert.That(io.nextBattle.weaponInstanceId, Is.EqualTo(_scenario.weaponInstanceId));
            Assert.That(io.nextBattle.combatantId, Is.EqualTo(_scenario.combatantId));
            var failures = JsonUtility.FromJson<PreparationFailureEvidenceList>(File.ReadAllText(Path.Combine(output, "failure_cases.json")));
            Assert.That(failures.cases.Length, Is.EqualTo(4));
            Assert.That(failures.cases.All(item => !item.success && item.stateUnchanged && item.ledgerUnchanged), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(output, "success_ledger.csv")), Does.Contain(_scenario.residentId));
            Assert.That(File.ReadAllText(Path.Combine(output, "success_ledger.txt")), Does.Contain(_scenario.residentName));
            Assert.That(File.ReadAllText(Path.Combine(output, "base_near_regression.txt")), Does.Contain("7"));
            Assert.That(File.ReadAllText(Path.Combine(output, "base_far_regression.txt")), Does.Contain("17"));
        }

        private PreparationSession AfterDrop()
        {
            var session = new PreparationSession(_scenario);
            Assert.That(session.ApplyExpeditionDrop(CloneDrop(_scenario.presetExpeditionDrop)).Success, Is.True);
            return session;
        }

        private PreparationSession Complete()
        {
            var session = AfterDrop();
            Assert.That(session.PurchaseAuxiliary("purchase-success", _scenario.purchaseQuantity).Success, Is.True);
            Assert.That(session.EnhanceEquippedWeapon("enhance-success").Success, Is.True);
            return session;
        }

        private static ExpeditionDrop CloneDrop(ExpeditionDrop input)
        {
            return JsonUtility.FromJson<ExpeditionDrop>(JsonUtility.ToJson(input));
        }

        private static ExpeditionDrop NewDrop(string id, int cash, ItemQuantity[] items)
        {
            return new ExpeditionDrop { settlementId = id, expeditionId = "test-expedition", encounterId = "test-encounter", cashReward = cash, items = items };
        }

        private static string StateKey(PreparationSession session)
        {
            return PreparationEvidenceExporter.SnapshotJson(session.GetSnapshot());
        }

        private static void AssertAtomicFailure(PreparationSession session, Func<PreparationCommandResult> action, PreparationResultCode expected)
        {
            string before = StateKey(session);
            string ledger = session.ExportLedgerCsv();
            var result = action();
            Assert.That(result.Success, Is.False, result.Reason);
            Assert.That(result.Code, Is.EqualTo(expected), result.Reason);
            Assert.That(result.Reason, Is.Not.Empty);
            Assert.That(StateKey(session), Is.EqualTo(before));
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(ledger));
        }

        private static void AssertState(PreparationSnapshot state, int treasury, int resident, int ore, int publicAux, int privateAux, int level, int bonus)
        {
            Assert.That(state.TreasuryCash, Is.EqualTo(treasury));
            Assert.That(state.ResidentCash, Is.EqualTo(resident));
            Assert.That(state.TotalCash, Is.EqualTo((long)treasury + resident));
            Assert.That(state.PublicOre, Is.EqualTo(ore));
            Assert.That(state.PublicAuxiliary, Is.EqualTo(publicAux));
            Assert.That(state.ResidentAuxiliary, Is.EqualTo(privateAux));
            Assert.That(state.WeaponEnhancementLevel, Is.EqualTo(level));
            Assert.That(state.WeaponAttackBonus, Is.EqualTo(bonus));
        }
    }
}
