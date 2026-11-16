using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Prototype.BaseEconomy
{
    /// <summary>
    /// Single-threaded, in-memory preparation slice. No file IO, Unity, combat formulas or wall clock.
    /// Successful operation IDs are unique across drop, purchase and enhancement in this session.
    /// </summary>
    public sealed class PreparationSession
    {
        private sealed class AppliedOperation
        {
            public string Kind;
            public string Fingerprint;
            public PreparationLedgerEntry Entry;
        }

        private readonly PreparationScenario _scenario;
        private readonly List<PreparationLedgerEntry> _ledger = new List<PreparationLedgerEntry>();
        private readonly Dictionary<string, AppliedOperation> _operations =
            new Dictionary<string, AppliedOperation>(StringComparer.Ordinal);
        private int _treasuryCash;
        private int _residentCash;
        private int _publicOre;
        private int _publicAuxiliary;
        private int _residentAuxiliary;
        private int _weaponEnhancementLevel;
        private int _weaponAttackBonus;
        private int _oreConsumed;
        private int _auxiliaryConsumed;
        private bool _hasEnhanced;

        public PreparationSession(PreparationScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            _scenario = scenario.Clone();
            _scenario.Validate();
            Reset();
        }

        public IReadOnlyList<PreparationLedgerEntry> Ledger
        {
            get { return new List<PreparationLedgerEntry>(_ledger).AsReadOnly(); }
        }

        public void Reset()
        {
            _treasuryCash = _scenario.initialTreasuryCash;
            _residentCash = _scenario.initialResidentCash;
            _publicOre = _scenario.initialPublicOre;
            _publicAuxiliary = _scenario.initialPublicAuxiliary;
            _residentAuxiliary = _scenario.initialResidentAuxiliary;
            _weaponEnhancementLevel = _scenario.initialWeaponEnhancementLevel;
            _weaponAttackBonus = _scenario.initialWeaponAttackBonus;
            _oreConsumed = 0;
            _auxiliaryConsumed = 0;
            _hasEnhanced = false;
            _ledger.Clear();
            _operations.Clear();
            List<PreparationItemChange> items = new List<PreparationItemChange>();
            AddChange(items, _scenario.oreItemId, "", _scenario.publicOwnerId, _publicOre, false);
            AddChange(items, _scenario.auxiliaryItemId, "", _scenario.publicOwnerId, _publicAuxiliary, false);
            AddChange(items, _scenario.auxiliaryItemId, "", _scenario.residentId, _residentAuxiliary, false);
            Record("", PreparationLedgerEvent.InitialState, "开局：公共战士已装备武器", "", "", 0,
                items, new PreparationEquipmentChange(_scenario, _weaponEnhancementLevel,
                    _weaponEnhancementLevel, _weaponAttackBonus, _weaponAttackBonus));
        }

        public PreparationSnapshot GetSnapshot() { return Snapshot(_ledger.Count); }

        /// <summary>Read-only lookup. Looking at a receipt never applies a reward.</summary>
        public PreparationLedgerEntry GetSettlementReceipt(string settlementId)
        {
            AppliedOperation existing;
            if (settlementId != null && _operations.TryGetValue(settlementId, out existing) && existing.Kind == "drop")
                return existing.Entry;
            return null;
        }

        public PreparationCommandResult ApplyExpeditionDrop(ExpeditionDrop input)
        {
            int ore, auxiliary;
            string fingerprint, reason;
            if (!PreparationDropValidation.TryNormalize(_scenario, input,
                out ore, out auxiliary, out fingerprint, out reason))
                return Fail(PreparationResultCode.InvalidDrop, reason);
            PreparationCommandResult replay = Replay(input.settlementId, "drop", fingerprint);
            if (replay != null) return replay;
            int nextCash, nextOre, nextAuxiliary;
            try
            {
                nextCash = checked(_treasuryCash + input.cashReward);
                nextOre = checked(_publicOre + ore);
                nextAuxiliary = checked(_publicAuxiliary + auxiliary);
            }
            catch (OverflowException)
            { return Fail(PreparationResultCode.NumericOverflow, "Drop would overflow public cash or item inventory."); }
            List<PreparationItemChange> items = new List<PreparationItemChange>();
            string source = "expedition:" + input.expeditionId + "/" + input.encounterId;
            AddChange(items, _scenario.oreItemId, source, _scenario.publicOwnerId, ore, false);
            AddChange(items, _scenario.auxiliaryItemId, source, _scenario.publicOwnerId, auxiliary, false);
            _treasuryCash = nextCash;
            _publicOre = nextOre;
            _publicAuxiliary = nextAuxiliary;
            PreparationLedgerEntry entry = Record(input.settlementId, PreparationLedgerEvent.ExpeditionDropReceived,
                "出征收益进入公共库", input.cashReward == 0 ? "" : source,
                input.cashReward == 0 ? "" : _scenario.publicOwnerId, input.cashReward, items, null);
            return Complete(input.settlementId, "drop", fingerprint, entry);
        }

        public PreparationCommandResult PurchaseAuxiliary(string commandId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                return Fail(PreparationResultCode.InvalidCommandId, "Purchase needs a nonempty commandId.");
            if (quantity <= 0)
                return Fail(PreparationResultCode.InvalidQuantity, "Purchase quantity must be positive.");
            string fingerprint = quantity.ToString(CultureInfo.InvariantCulture);
            PreparationCommandResult replay = Replay(commandId, "purchase", fingerprint);
            if (replay != null) return replay;
            int amount;
            try { amount = checked(_scenario.auxiliaryUnitPrice * quantity); }
            catch (OverflowException)
            { return Fail(PreparationResultCode.NumericOverflow, "Unit price multiplied by quantity exceeds the cash range."); }
            if (_treasuryCash < amount)
                return Fail(PreparationResultCode.InsufficientCash,
                    "Public cash needs " + Number(amount) + ", available " + Number(_treasuryCash) + ".");
            if (_residentAuxiliary < quantity)
                return Fail(PreparationResultCode.InsufficientPrivateMaterial,
                    "Private auxiliary needs " + Number(quantity) + ", available " + Number(_residentAuxiliary) + ".");
            int nextResidentCash, nextPublicAuxiliary;
            try
            {
                nextResidentCash = checked(_residentCash + amount);
                nextPublicAuxiliary = checked(_publicAuxiliary + quantity);
            }
            catch (OverflowException)
            { return Fail(PreparationResultCode.NumericOverflow, "Purchase would overflow resident cash or public inventory."); }
            List<PreparationItemChange> items = new List<PreparationItemChange>
            {
                new PreparationItemChange(_scenario.auxiliaryItemId, _scenario.residentId,
                    _scenario.publicOwnerId, quantity, false)
            };
            _treasuryCash -= amount;
            _residentCash = nextResidentCash;
            _residentAuxiliary -= quantity;
            _publicAuxiliary = nextPublicAuxiliary;
            PreparationLedgerEntry entry = Record(commandId, PreparationLedgerEvent.AuxiliaryPurchased,
                "公款向居民采购私人辅料", _scenario.publicOwnerId, _scenario.residentId,
                amount, items, null);
            return Complete(commandId, "purchase", fingerprint, entry);
        }

        public PreparationCommandResult EnhanceEquippedWeapon(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                return Fail(PreparationResultCode.InvalidCommandId, "Enhancement needs a nonempty commandId.");
            PreparationCommandResult replay = Replay(commandId, "enhance", _scenario.weaponInstanceId);
            if (replay != null) return replay;
            if (_hasEnhanced)
                return Fail(PreparationResultCode.AlreadyEnhanced, "This slice permits one enhancement of the equipped weapon.");
            if (_publicOre < _scenario.oreRequired)
                return Fail(PreparationResultCode.InsufficientPublicOre,
                    "Public ore needs " + Number(_scenario.oreRequired) + ", available " + Number(_publicOre) + ".");
            if (_publicAuxiliary < _scenario.auxiliaryRequired)
                return Fail(PreparationResultCode.InsufficientPublicAuxiliary,
                    "Public auxiliary needs " + Number(_scenario.auxiliaryRequired) + ", available " + Number(_publicAuxiliary) + ".");
            int nextLevel, nextBonus, nextOreConsumed, nextAuxiliaryConsumed;
            try
            {
                nextLevel = checked(_weaponEnhancementLevel + 1);
                nextBonus = checked(_weaponAttackBonus + _scenario.enhancementAttackBonus);
                nextOreConsumed = checked(_oreConsumed + _scenario.oreRequired);
                nextAuxiliaryConsumed = checked(_auxiliaryConsumed + _scenario.auxiliaryRequired);
            }
            catch (OverflowException)
            { return Fail(PreparationResultCode.NumericOverflow, "Enhancement would overflow the equipment or consumption range."); }
            PreparationEquipmentChange equipment = new PreparationEquipmentChange(_scenario,
                _weaponEnhancementLevel, nextLevel, _weaponAttackBonus, nextBonus);
            List<PreparationItemChange> items = new List<PreparationItemChange>
            {
                new PreparationItemChange(_scenario.oreItemId, _scenario.publicOwnerId,
                    "", _scenario.oreRequired, true),
                new PreparationItemChange(_scenario.auxiliaryItemId, _scenario.publicOwnerId,
                    "", _scenario.auxiliaryRequired, true)
            };
            _publicOre -= _scenario.oreRequired;
            _publicAuxiliary -= _scenario.auxiliaryRequired;
            _weaponEnhancementLevel = nextLevel;
            _weaponAttackBonus = nextBonus;
            _oreConsumed = nextOreConsumed;
            _auxiliaryConsumed = nextAuxiliaryConsumed;
            _hasEnhanced = true;
            PreparationLedgerEntry entry = Record(commandId, PreparationLedgerEvent.WeaponEnhanced,
                "公共材料强化已装备武器", "", "", 0, items, equipment);
            return Complete(commandId, "enhance", _scenario.weaponInstanceId, entry);
        }

        public string ExportLedgerText()
        {
            StringBuilder text = new StringBuilder();
            text.Append("场景: ").Append(_scenario.scenarioId).Append('\n');
            text.Append("口径: ").Append(_scenario.testValueNotice).Append('\n');
            text.Append("公库: ").Append(_scenario.publicOwnerName).Append(" (").Append(_scenario.publicOwnerId).Append(")\n");
            text.Append("居民: ").Append(_scenario.residentName).Append(" (").Append(_scenario.residentId).Append(")\n");
            text.Append("公营战士: ").Append(_scenario.combatantName).Append(" (").Append(_scenario.combatantId).Append(")\n");
            text.Append("已装备武器: ").Append(_scenario.weaponName).Append(" (").Append(_scenario.weaponInstanceId).Append(")\n");
            foreach (PreparationLedgerEntry entry in _ledger)
            {
                PreparationSnapshot after = entry.After;
                text.Append('#').Append(Number(entry.Sequence)).Append(' ').Append(entry.EventName)
                    .Append(" [").Append(entry.OperationId).Append("]\n");
                text.Append("  付款方=").Append(OwnerName(entry.PayerId)).Append("; 收款方=")
                    .Append(OwnerName(entry.PayeeId)).Append("; 金额=").Append(Number(entry.Amount)).Append('\n');
                text.Append("  物品归属/实际消耗: ").Append(ItemChangesText(entry.ItemChanges)).Append('\n');
                text.Append("  公库=").Append(Number(after.TreasuryCash)).Append("; 居民=")
                    .Append(Number(after.ResidentCash)).Append("; 现金合计=").Append(Number(after.TotalCash)).Append('\n');
                text.Append("  公有矿料=").Append(Number(after.PublicOre)).Append("; 公有辅料=")
                    .Append(Number(after.PublicAuxiliary)).Append("; 私有辅料=").Append(Number(after.ResidentAuxiliary))
                    .Append("; 累计消耗矿料=").Append(Number(after.OreConsumed))
                    .Append("; 累计消耗辅料=").Append(Number(after.AuxiliaryConsumed)).Append('\n');
                text.Append("  装备归属=").Append(after.WeaponOwnerId).Append("; 装备者=").Append(after.CombatantId)
                    .Append("; 武器实例=").Append(after.WeaponInstanceId).Append("; 强化级=")
                    .Append(Number(after.WeaponEnhancementLevel)).Append("; WeaponAttackBonus=")
                    .Append(Number(after.WeaponAttackBonus));
                if (entry.EquipmentChange != null)
                    text.Append(" (本笔 ").Append(Number(entry.EquipmentChange.AttackBonusBefore)).Append("→")
                        .Append(Number(entry.EquipmentChange.AttackBonusAfter)).Append(')');
                text.Append('\n');
            }
            return text.ToString();
        }

        public string ExportLedgerCsv()
        {
            StringBuilder csv = new StringBuilder();
            csv.Append("sequence,operationId,event,payerId,payerName,payeeId,payeeName,amount,itemChanges,treasuryCash,residentCash,totalCash,publicOre,publicAuxiliary,residentAuxiliary,oreConsumed,auxiliaryConsumed,weaponOwnerId,equippedById,weaponInstanceId,enhancementBefore,enhancementAfter,attackBonusBefore,attackBonusAfter\n");
            foreach (PreparationLedgerEntry entry in _ledger)
            {
                PreparationSnapshot after = entry.After;
                PreparationEquipmentChange change = entry.EquipmentChange;
                string[] cells =
                {
                    Number(entry.Sequence), entry.OperationId, entry.EventName, entry.PayerId, OwnerName(entry.PayerId),
                    entry.PayeeId, OwnerName(entry.PayeeId),
                    Number(entry.Amount), ItemChangesText(entry.ItemChanges), Number(after.TreasuryCash),
                    Number(after.ResidentCash), Number(after.TotalCash), Number(after.PublicOre),
                    Number(after.PublicAuxiliary), Number(after.ResidentAuxiliary), Number(after.OreConsumed),
                    Number(after.AuxiliaryConsumed), after.WeaponOwnerId, after.CombatantId, after.WeaponInstanceId,
                    Number(change == null ? after.WeaponEnhancementLevel : change.EnhancementLevelBefore),
                    Number(change == null ? after.WeaponEnhancementLevel : change.EnhancementLevelAfter),
                    Number(change == null ? after.WeaponAttackBonus : change.AttackBonusBefore),
                    Number(change == null ? after.WeaponAttackBonus : change.AttackBonusAfter)
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0) csv.Append(',');
                    csv.Append(CsvCell(cells[i]));
                }
                csv.Append('\n');
            }
            return csv.ToString();
        }

        private PreparationSnapshot Snapshot(int ledgerCount)
        {
            return new PreparationSnapshot(_scenario, _treasuryCash, _residentCash, _publicOre,
                _publicAuxiliary, _residentAuxiliary, _weaponEnhancementLevel, _weaponAttackBonus,
                _oreConsumed, _auxiliaryConsumed, _hasEnhanced, ledgerCount);
        }

        private PreparationLedgerEntry Record(string id, PreparationLedgerEvent kind, string name,
            string payer, string payee, int amount, IList<PreparationItemChange> items,
            PreparationEquipmentChange equipment)
        {
            int sequence = _ledger.Count + 1;
            PreparationLedgerEntry entry = new PreparationLedgerEntry(sequence, id, kind, name,
                payer, payee, amount, items, equipment, Snapshot(sequence));
            _ledger.Add(entry);
            return entry;
        }

        private PreparationCommandResult Complete(string id, string kind, string fingerprint,
            PreparationLedgerEntry entry)
        {
            _operations.Add(id, new AppliedOperation { Kind = kind, Fingerprint = fingerprint, Entry = entry });
            return new PreparationCommandResult(true, false, PreparationResultCode.Success, "Applied.", entry.Sequence);
        }

        private PreparationCommandResult Replay(string id, string kind, string fingerprint)
        {
            AppliedOperation previous;
            if (!_operations.TryGetValue(id, out previous)) return null;
            if (previous.Kind != kind || previous.Fingerprint != fingerprint)
                return Fail(PreparationResultCode.DuplicateIdConflict, "Operation ID already belongs to different content or command kind.");
            return new PreparationCommandResult(true, true, PreparationResultCode.Success,
                "Already applied; returning the original receipt without changing state.", previous.Entry.Sequence);
        }

        private static PreparationCommandResult Fail(PreparationResultCode code, string reason)
        {
            return new PreparationCommandResult(false, false, code, reason, 0);
        }

        private static void AddChange(List<PreparationItemChange> items, string itemId,
            string from, string to, int quantity, bool consumed)
        {
            if (quantity > 0) items.Add(new PreparationItemChange(itemId, from, to, quantity, consumed));
        }

        private string OwnerName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "无现金交易";
            if (id == _scenario.publicOwnerId) return _scenario.publicOwnerName + "(" + id + ")";
            if (id == _scenario.residentId) return _scenario.residentName + "(" + id + ")";
            return id;
        }

        private static string ItemChangesText(IReadOnlyList<PreparationItemChange> changes)
        {
            if (changes.Count == 0) return "无";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < changes.Count; i++)
            {
                if (i > 0) text.Append("; ");
                PreparationItemChange item = changes[i];
                text.Append(item.ItemId).Append(" ×").Append(Number(item.Quantity)).Append(": ")
                    .Append(string.IsNullOrEmpty(item.FromOwnerId) ? "开局" : item.FromOwnerId).Append(" → ")
                    .Append(item.IsConsumption ? "实际消耗" : item.ToOwnerId);
            }
            return text.ToString();
        }

        private static string CsvCell(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Number(long value) { return value.ToString(CultureInfo.InvariantCulture); }
    }
}
