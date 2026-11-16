using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.BaseEconomy.Tests
{
    [Serializable]
    public sealed class PreparationEvidenceSnapshot
    {
        public string scenarioId;
        public string publicOwnerId;
        public string residentId;
        public string combatantId;
        public string weaponInstanceId;
        public string weaponOwnerId;
        public int treasuryCash;
        public int residentCash;
        public long totalCash;
        public int publicOre;
        public int publicAuxiliary;
        public int residentAuxiliary;
        public int weaponEnhancementLevel;
        public int weaponAttackBonus;
        public int oreConsumed;
        public int auxiliaryConsumed;
        public bool hasEnhanced;
        public int ledgerCount;

        public static PreparationEvidenceSnapshot From(PreparationSnapshot state)
        {
            return new PreparationEvidenceSnapshot
            {
                scenarioId = state.ScenarioId, publicOwnerId = state.PublicOwnerId,
                residentId = state.ResidentId, combatantId = state.CombatantId,
                weaponInstanceId = state.WeaponInstanceId, weaponOwnerId = state.WeaponOwnerId,
                treasuryCash = state.TreasuryCash, residentCash = state.ResidentCash, totalCash = state.TotalCash,
                publicOre = state.PublicOre, publicAuxiliary = state.PublicAuxiliary, residentAuxiliary = state.ResidentAuxiliary,
                weaponEnhancementLevel = state.WeaponEnhancementLevel, weaponAttackBonus = state.WeaponAttackBonus,
                oreConsumed = state.OreConsumed, auxiliaryConsumed = state.AuxiliaryConsumed,
                hasEnhanced = state.HasEnhanced, ledgerCount = state.LedgerCount
            };
        }
    }

    [Serializable]
    public sealed class PreparationCommandEvidence
    {
        public string commandId;
        public int quantity;
    }

    [Serializable]
    public sealed class PreparationBattleEquipmentEvidence
    {
        public string combatantId;
        public string weaponInstanceId;
        public int weaponEnhancementLevel;
        public int weaponAttackBonus;
        public string interpretation;
    }

    [Serializable]
    public sealed class PreparationInputOutputEvidence
    {
        public string notice;
        public string entryPoint;
        public PreparationScenario testConfiguration;
        public ExpeditionDrop expeditionInput;
        public PreparationCommandEvidence purchaseInput;
        public PreparationCommandEvidence enhancementInput;
        public PreparationEvidenceSnapshot opening;
        public PreparationEvidenceSnapshot afterDrop;
        public PreparationEvidenceSnapshot afterPurchase;
        public PreparationEvidenceSnapshot afterEnhancement;
        public PreparationBattleEquipmentEvidence nextBattle;
        public bool cashConservedDuringPurchase;
        public bool reportReadAndReplayHadNoEffect;
        public int receiptSequence;
        public int replaySequence;
    }

    [Serializable]
    public sealed class PreparationFailureEvidence
    {
        public string name;
        public string operation;
        public bool success;
        public string code;
        public string reason;
        public PreparationEvidenceSnapshot before;
        public PreparationEvidenceSnapshot after;
        public bool stateUnchanged;
        public bool ledgerUnchanged;
        public bool earlierPurchaseRemainsCommitted;
    }

    [Serializable]
    public sealed class PreparationFailureEvidenceList
    {
        public string notice;
        public PreparationFailureEvidence[] cases;
    }

    public static class PreparationEvidenceExporter
    {
        public static PreparationScenario LoadScenario()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Prototype", "BaseEconomy", "preparation.json");
            Assert.That(File.Exists(path), Is.True, "Missing shared preparation configuration: " + path);
            var scenario = JsonUtility.FromJson<PreparationScenario>(File.ReadAllText(path));
            Assert.That(scenario, Is.Not.Null);
            return scenario;
        }

        public static string SnapshotJson(PreparationSnapshot state)
        {
            return JsonUtility.ToJson(PreparationEvidenceSnapshot.From(state));
        }

        public static void ExportAll(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var scenario = LoadScenario();
            var drop = CloneDrop(scenario.presetExpeditionDrop);
            var session = new PreparationSession(scenario);
            var record = new PreparationInputOutputEvidence
            {
                notice = "Actual pure-C# session execution in Unity EditMode. All prices, quantities and effects are replaceable test values.",
                entryPoint = "Prototype.BaseEconomy.PreparationSession",
                testConfiguration = scenario,
                expeditionInput = CloneDrop(drop),
                purchaseInput = new PreparationCommandEvidence { commandId = "example-purchase-001", quantity = scenario.purchaseQuantity },
                enhancementInput = new PreparationCommandEvidence { commandId = "example-enhance-001", quantity = 0 },
                opening = PreparationEvidenceSnapshot.From(session.GetSnapshot())
            };
            Assert.That(session.ApplyExpeditionDrop(drop).Success, Is.True);
            record.afterDrop = PreparationEvidenceSnapshot.From(session.GetSnapshot());
            Assert.That(session.PurchaseAuxiliary(record.purchaseInput.commandId, record.purchaseInput.quantity).Success, Is.True);
            record.afterPurchase = PreparationEvidenceSnapshot.From(session.GetSnapshot());
            Assert.That(session.EnhanceEquippedWeapon(record.enhancementInput.commandId).Success, Is.True);
            record.afterEnhancement = PreparationEvidenceSnapshot.From(session.GetSnapshot());
            var state = session.GetSnapshot();
            record.nextBattle = new PreparationBattleEquipmentEvidence
            {
                combatantId = state.CombatantId,
                weaponInstanceId = state.WeaponInstanceId,
                weaponEnhancementLevel = state.WeaponEnhancementLevel,
                weaponAttackBonus = state.WeaponAttackBonus,
                interpretation = "Equipment contribution only. Combat module decides damage conversion; this module never calculates combat damage."
            };
            record.cashConservedDuringPurchase = record.afterDrop.totalCash == record.afterPurchase.totalCash;
            string beforeRead = SnapshotJson(state);
            string ledgerBeforeRead = session.ExportLedgerCsv();
            var receipt = session.GetSettlementReceipt(drop.settlementId);
            record.receiptSequence = receipt.Sequence;
            var replay = session.ApplyExpeditionDrop(CloneDrop(drop));
            record.replaySequence = replay.LedgerSequence;
            record.reportReadAndReplayHadNoEffect = replay.Success && replay.IsReplay &&
                beforeRead == SnapshotJson(session.GetSnapshot()) && ledgerBeforeRead == session.ExportLedgerCsv();
            Assert.That(record.cashConservedDuringPurchase, Is.True);
            Assert.That(record.reportReadAndReplayHadNoEffect, Is.True);
            Assert.That(record.afterPurchase.treasuryCash, Is.EqualTo(88));
            Assert.That(record.afterPurchase.residentCash, Is.EqualTo(32));
            Assert.That(record.afterEnhancement.publicOre, Is.Zero);
            Assert.That(record.afterEnhancement.publicAuxiliary, Is.Zero);
            Assert.That(record.afterEnhancement.weaponEnhancementLevel, Is.EqualTo(1));
            Assert.That(record.afterEnhancement.weaponAttackBonus, Is.EqualTo(4));
            WriteText(outputDirectory, "success_ledger.csv", session.ExportLedgerCsv());
            WriteText(outputDirectory, "success_ledger.txt", session.ExportLedgerText());
            WriteText(outputDirectory, "input_output.json", JsonUtility.ToJson(record, true));

            var failures = new PreparationFailureEvidenceList
            {
                notice = "Each failed operation preserves its immediate pre-operation snapshot and ledger. A failed enhancement does not roll back a prior successful purchase.",
                cases = new[]
                {
                    MakeFailure("insufficient_cash", "PurchaseAuxiliary", config => config.initialTreasuryCash = 11, true, 2, false, PreparationResultCode.InsufficientCash),
                    MakeFailure("insufficient_private_auxiliary", "PurchaseAuxiliary", config => config.initialResidentAuxiliary = 1, true, 2, false, PreparationResultCode.InsufficientPrivateMaterial),
                    MakeFailure("insufficient_public_ore", "EnhanceEquippedWeapon", config => config.presetExpeditionDrop.items[0].quantity = 2, true, 2, true, PreparationResultCode.InsufficientPublicOre),
                    MakeFailure("insufficient_public_auxiliary", "EnhanceEquippedWeapon", config => { }, true, 1, true, PreparationResultCode.InsufficientPublicAuxiliary)
                }
            };
            Assert.That(failures.cases.All(item => !item.success && item.stateUnchanged && item.ledgerUnchanged), Is.True);
            WriteText(outputDirectory, "failure_cases.json", JsonUtility.ToJson(failures, true));
            foreach (var failure in failures.cases)
            {
                WriteText(outputDirectory, failure.name + ".json", JsonUtility.ToJson(failure, true));
            }
            ExportFirstMealRegression(outputDirectory);
            string configDirectory = Path.Combine(outputDirectory, "config_used");
            Directory.CreateDirectory(configDirectory);
            foreach (string filename in new[] { "default.json", "preparation.json" })
                File.Copy(Path.Combine(Application.streamingAssetsPath, "Prototype", "BaseEconomy", filename), Path.Combine(configDirectory, filename), true);
        }

        private static PreparationFailureEvidence MakeFailure(string name, string operation, Action<PreparationScenario> configure,
            bool applyDrop, int purchaseQuantity, bool enhance, PreparationResultCode expected)
        {
            var config = LoadScenario();
            configure(config);
            var session = new PreparationSession(config);
            if (applyDrop)
                Assert.That(session.ApplyExpeditionDrop(CloneDrop(config.presetExpeditionDrop)).Success, Is.True);
            if (enhance)
                Assert.That(session.PurchaseAuxiliary(name + "-purchase", purchaseQuantity).Success, Is.True);
            var before = PreparationEvidenceSnapshot.From(session.GetSnapshot());
            string ledger = session.ExportLedgerCsv();
            var result = enhance ? session.EnhanceEquippedWeapon(name + "-enhance") : session.PurchaseAuxiliary(name + "-purchase", purchaseQuantity);
            var after = PreparationEvidenceSnapshot.From(session.GetSnapshot());
            var record = new PreparationFailureEvidence
            {
                name = name, operation = operation, success = result.Success, code = result.Code.ToString(), reason = result.Reason,
                before = before, after = after,
                stateUnchanged = JsonUtility.ToJson(before) == JsonUtility.ToJson(after),
                ledgerUnchanged = ledger == session.ExportLedgerCsv(),
                earlierPurchaseRemainsCommitted = enhance && after.residentCash > config.initialResidentCash && after.residentAuxiliary < config.initialResidentAuxiliary
            };
            Assert.That(result.Code, Is.EqualTo(expected), result.Reason);
            Assert.That(record.success, Is.False);
            Assert.That(record.stateUnchanged, Is.True);
            Assert.That(record.ledgerUnchanged, Is.True);
            if (enhance)
                Assert.That(record.earlierPurchaseRemainsCommitted, Is.True);
            return record;
        }

        private static void ExportFirstMealRegression(string outputDirectory)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Prototype", "BaseEconomy", "default.json");
            var config = JsonUtility.FromJson<BaseScenario>(File.ReadAllText(path));
            var near = new BaseSession(config.WithKitchen(config.nearKitchenPosition));
            var far = new BaseSession(config.WithKitchen(config.farKitchenPosition));
            Assert.That(near.RecruitCook().Success && far.RecruitCook().Success, Is.True);
            var nearDone = near.RunUntilFirstMeal(config.runUntilFirstMealMaxTicks);
            var farDone = far.RunUntilFirstMeal(config.runUntilFirstMealMaxTicks);
            Assert.That(nearDone.FirstMealTick, Is.EqualTo(7));
            Assert.That(farDone.FirstMealTick, Is.EqualTo(17));
            foreach (var state in new[] { nearDone, farDone })
            {
                Assert.That(state.TreasuryCash, Is.EqualTo(88));
                Assert.That(state.SupplierCash, Is.EqualTo(16));
                Assert.That(state.CookCash, Is.EqualTo(16));
                Assert.That(state.CustomerCash, Is.EqualTo(10));
                Assert.That(state.TotalCash, Is.EqualTo(130));
                Assert.That(state.MealsConsumed, Is.EqualTo(1));
            }
            WriteText(outputDirectory, "base_near_regression.csv", near.ExportLedgerCsv());
            WriteText(outputDirectory, "base_near_regression.txt", near.ExportLedgerText());
            WriteText(outputDirectory, "base_far_regression.csv", far.ExportLedgerCsv());
            WriteText(outputDirectory, "base_far_regression.txt", far.ExportLedgerText());
        }

        private static ExpeditionDrop CloneDrop(ExpeditionDrop input)
        {
            return JsonUtility.FromJson<ExpeditionDrop>(JsonUtility.ToJson(input));
        }

        private static void WriteText(string directory, string filename, string content)
        {
            File.WriteAllText(Path.Combine(directory, filename), content, new UTF8Encoding(false));
        }
    }
}
