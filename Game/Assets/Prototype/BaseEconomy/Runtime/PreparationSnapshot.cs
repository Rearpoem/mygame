using System.Collections.Generic;

namespace Prototype.BaseEconomy
{
    public enum PreparationResultCode
    {
        Success,
        InvalidCommandId,
        DuplicateIdConflict,
        InvalidDrop,
        InvalidQuantity,
        InsufficientCash,
        InsufficientPrivateMaterial,
        InsufficientPublicOre,
        InsufficientPublicAuxiliary,
        AlreadyEnhanced,
        NumericOverflow
    }

    public sealed class PreparationCommandResult
    {
        public bool Success { get; private set; }
        public bool IsReplay { get; private set; }
        public PreparationResultCode Code { get; private set; }
        public string Reason { get; private set; }
        public int LedgerSequence { get; private set; }

        internal PreparationCommandResult(bool success, bool isReplay, PreparationResultCode code,
            string reason, int ledgerSequence)
        {
            Success = success;
            IsReplay = isReplay;
            Code = code;
            Reason = reason;
            LedgerSequence = ledgerSequence;
        }
    }

    /// <summary>Detached immutable state. WeaponAttackBonus is equipment data, not a damage formula.</summary>
    public sealed class PreparationSnapshot
    {
        public string ScenarioId { get; private set; }
        public string PublicOwnerId { get; private set; }
        public string ResidentId { get; private set; }
        public string CombatantId { get; private set; }
        public string WeaponInstanceId { get; private set; }
        public string WeaponOwnerId { get; private set; }
        public int TreasuryCash { get; private set; }
        public int ResidentCash { get; private set; }
        public long TotalCash { get; private set; }
        public int PublicOre { get; private set; }
        public int PublicAuxiliary { get; private set; }
        public int ResidentAuxiliary { get; private set; }
        public int WeaponEnhancementLevel { get; private set; }
        public int WeaponAttackBonus { get; private set; }
        public int OreConsumed { get; private set; }
        public int AuxiliaryConsumed { get; private set; }
        public bool HasEnhanced { get; private set; }
        public int LedgerCount { get; private set; }

        internal PreparationSnapshot(PreparationScenario scenario, int treasuryCash, int residentCash,
            int publicOre, int publicAuxiliary, int residentAuxiliary, int enhancementLevel,
            int attackBonus, int oreConsumed, int auxiliaryConsumed, bool hasEnhanced, int ledgerCount)
        {
            ScenarioId = scenario.scenarioId;
            PublicOwnerId = scenario.publicOwnerId;
            ResidentId = scenario.residentId;
            CombatantId = scenario.combatantId;
            WeaponInstanceId = scenario.weaponInstanceId;
            WeaponOwnerId = scenario.publicOwnerId;
            TreasuryCash = treasuryCash;
            ResidentCash = residentCash;
            TotalCash = (long)treasuryCash + residentCash;
            PublicOre = publicOre;
            PublicAuxiliary = publicAuxiliary;
            ResidentAuxiliary = residentAuxiliary;
            WeaponEnhancementLevel = enhancementLevel;
            WeaponAttackBonus = attackBonus;
            OreConsumed = oreConsumed;
            AuxiliaryConsumed = auxiliaryConsumed;
            HasEnhanced = hasEnhanced;
            LedgerCount = ledgerCount;
        }
    }

    public enum PreparationLedgerEvent
    {
        InitialState,
        ExpeditionDropReceived,
        AuxiliaryPurchased,
        WeaponEnhanced
    }

    public sealed class PreparationItemChange
    {
        public string ItemId { get; private set; }
        public string FromOwnerId { get; private set; }
        public string ToOwnerId { get; private set; }
        public int Quantity { get; private set; }
        public bool IsConsumption { get; private set; }

        internal PreparationItemChange(string itemId, string fromOwnerId, string toOwnerId,
            int quantity, bool isConsumption)
        {
            ItemId = itemId;
            FromOwnerId = fromOwnerId;
            ToOwnerId = toOwnerId;
            Quantity = quantity;
            IsConsumption = isConsumption;
        }
    }

    public sealed class PreparationEquipmentChange
    {
        public string WeaponInstanceId { get; private set; }
        public string OwnerId { get; private set; }
        public string EquippedById { get; private set; }
        public int EnhancementLevelBefore { get; private set; }
        public int EnhancementLevelAfter { get; private set; }
        public int AttackBonusBefore { get; private set; }
        public int AttackBonusAfter { get; private set; }

        internal PreparationEquipmentChange(PreparationScenario scenario, int levelBefore, int levelAfter,
            int bonusBefore, int bonusAfter)
        {
            WeaponInstanceId = scenario.weaponInstanceId;
            OwnerId = scenario.publicOwnerId;
            EquippedById = scenario.combatantId;
            EnhancementLevelBefore = levelBefore;
            EnhancementLevelAfter = levelAfter;
            AttackBonusBefore = bonusBefore;
            AttackBonusAfter = bonusAfter;
        }
    }

    /// <summary>One successful event with immutable ownership changes and its exact post-event state.</summary>
    public sealed class PreparationLedgerEntry
    {
        public int Sequence { get; private set; }
        public string OperationId { get; private set; }
        public PreparationLedgerEvent EventKind { get; private set; }
        public string EventName { get; private set; }
        public string PayerId { get; private set; }
        public string PayeeId { get; private set; }
        public int Amount { get; private set; }
        public IReadOnlyList<PreparationItemChange> ItemChanges { get; private set; }
        public PreparationEquipmentChange EquipmentChange { get; private set; }
        public PreparationSnapshot After { get; private set; }

        internal PreparationLedgerEntry(int sequence, string operationId, PreparationLedgerEvent eventKind,
            string eventName, string payerId, string payeeId, int amount,
            IList<PreparationItemChange> itemChanges, PreparationEquipmentChange equipmentChange,
            PreparationSnapshot after)
        {
            Sequence = sequence;
            OperationId = operationId;
            EventKind = eventKind;
            EventName = eventName;
            PayerId = payerId;
            PayeeId = payeeId;
            Amount = amount;
            ItemChanges = new List<PreparationItemChange>(itemChanges).AsReadOnly();
            EquipmentChange = equipmentChange;
            After = after;
        }
    }
}
